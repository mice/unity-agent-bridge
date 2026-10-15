using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityAgentBridge.Mcp;

internal sealed record McpStoredTask(int Version, string ProjectIdentity, McpTaskInfo Task,
    DateTimeOffset ExecutionDeadline, DateTimeOffset? ObservationFailedAt = null,
    JsonElement? LastObservation = null, string? OwnedReport = null, bool CancellationRequested = false);

#pragma warning disable CS0067
public sealed class McpTaskStore : IMcpTaskStore
{
    private const int Version = 2;
    private readonly McpTaskPersistence persistence;
    private readonly McpTaskExecutionCoordinator? coordinator;
    private readonly McpTaskPolicy policy;
    private readonly TimeProvider clock;

    public McpTaskStore(string projectPath, McpTaskExecutionCoordinator? coordinator = null)
        : this(projectPath, coordinator, new McpTaskPolicy(), TimeProvider.System) { }

    internal McpTaskStore(string projectPath, McpTaskExecutionCoordinator? coordinator, McpTaskPolicy policy, TimeProvider clock)
    {
        persistence = new(projectPath);
        this.coordinator = coordinator;
        this.policy = policy;
        this.clock = clock;
        persistence.ImportOnce("mcp-tasks", ImportLegacy);
        coordinator?.AttachStore(this);
    }

    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    public async Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken)
    {
        var pending = McpTaskExecutionContext.ConsumePending();
        if (coordinator is not null) await coordinator.ValidateAsync(pending, cancellationToken);
        using var lease = persistence.Enter(cancellationToken);
        if (ReadRecords().Count(record => !IsTerminal(record.Task.Status)) >= policy.MaxActiveRecords)
            throw new McpProtocolException("MCP_TASK_CAPACITY: Too many active tasks; existing tracking is preserved.", McpErrorCode.InternalError);
        var now = clock.GetUtcNow();
        var task = new McpTaskInfo("mcp-" + Guid.NewGuid().ToString("N"), McpTaskStatus.Working, now, now,
            null, 250, "Allocated; awaiting validated execution.", null, null, new Dictionary<string, InputRequest>());
        var window = pending?.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(Math.Min((long)pending.TimeoutMs + 30000, 86400000)) : policy.DefaultExecutionWindow;
        Save(new(Version, persistence.ProjectPath, task, now + window));
        if (pending is not null) pending.McpTaskId = task.TaskId;
        return task;
    }

    public async Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return null;
        McpStoredTask? record;
        using (persistence.Enter(cancellationToken))
        {
            record = Load(taskId);
            if (record is null) return null;
            if (IsTerminal(record.Task.Status))
            {
                if (Expired(record)) { Delete(record); return null; }
                return record.Task;
            }
            if (clock.GetUtcNow() >= record.ExecutionDeadline)
                return CommitFailure(record, "AGENT_TASK_EXECUTION_DEADLINE", "Execution could not be reconciled before its host deadline.").Task;
        }
        if (coordinator is null) return record.Task;
        JsonObject projection;
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observation.CancelAfter(policy.ObservationBudget);
        try { projection = await coordinator.QueryAsync(taskId, observation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { projection = McpTaskAdapter.ObservationError("AGENT_TASK_OBSERVATION_TIMEOUT", "Observation timed out; execution remains unconfirmed.", taskId); }
        catch (Exception exception) when (exception is IOException or JsonException)
        { projection = McpTaskAdapter.ObservationError("AGENT_TASK_OBSERVATION_FAILED", exception.Message, taskId); }
        using (persistence.Enter(cancellationToken))
        {
            record = Load(taskId);
            if (record is null || IsTerminal(record.Task.Status)) return record?.Task;
            var now = clock.GetUtcNow();
            record = record with { LastObservation = JsonSerializer.SerializeToElement(projection) };
            if (projection["observationFailure"]?.GetValue<bool>() == true)
            {
                record = record with { ObservationFailedAt = record.ObservationFailedAt ?? now,
                    Task = record.Task with { LastUpdatedAt = now, StatusMessage = projection["errorCode"]?.GetValue<string>() + ": " +
                        (projection["errorMessage"]?.GetValue<string>() ?? projection["summary"]?.GetValue<string>() ?? "Observation unavailable.") } };
                if (now - record.ObservationFailedAt >= policy.RecoveryWindow || now >= record.ExecutionDeadline)
                    return CommitFailure(record, "AGENT_TASK_OUTCOME_UNKNOWN", "Recovery deadline exhausted; the original operation was not replayed.").Task;
            }
            else
            {
                var status = projection["status"]?.GetValue<string>();
                record = record with { ObservationFailedAt = null };
                if (status is "completed" or "failed")
                    record = Complete(record, McpTaskStatus.Completed, McpToolResultConverter.Serialize(projection), null, "Operation completed.");
                else if (status == "cancelled")
                    record = Complete(record, McpTaskStatus.Cancelled, null, null, "Unity execution cancelled.");
                else record = record with { Task = record.Task with { LastUpdatedAt = now, StatusMessage = "Unity operation is running." } };
            }
            Save(record);
            return record.Task;
        }
    }

    internal async Task<JsonObject> WaitForTerminalAsync(string taskId, CancellationToken token)
    {
        while (true)
        {
            var task = await GetTaskAsync(taskId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException(taskId);
            if (task.Status == McpTaskStatus.Completed)
            {
                var result = JsonNode.Parse(task.Result!.Value.GetRawText())!.AsObject();
                return result["structuredContent"]?.AsObject() ?? new JsonObject { ["success"] = result["isError"]?.GetValue<bool>() != true, ["status"] = "completed" };
            }
            if (task.Status == McpTaskStatus.Failed)
                throw new McpProtocolException(task.Error?.GetProperty("message").GetString() ?? "Task failed.", McpErrorCode.InternalError);
            if (task.Status == McpTaskStatus.Cancelled)
                return new JsonObject { ["taskId"] = taskId, ["state"] = "Cancelled", ["status"] = "cancelled", ["success"] = false };
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken)
        => Update(taskId, record => Complete(record, McpTaskStatus.Completed, result.Clone(), null, "Task completed."), cancellationToken);
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken)
        => Update(taskId, record => Complete(record, McpTaskStatus.Failed, null, error.Clone(), "Task failed."), cancellationToken);

    public async Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken)
    {
        using (persistence.Enter(cancellationToken))
        {
            var current = Load(taskId);
            if (current is null) throw new McpProtocolException("Unknown task: " + taskId, McpErrorCode.InvalidParams);
            if (IsTerminal(current.Task.Status)) return false;
            if (current.CancellationRequested) return true;
        }
        if (coordinator is not null)
        {
            if (!coordinator.IsMapped(taskId))
                throw new McpProtocolException("AGENT_TASK_CANCEL_PENDING: Wait until validated dispatch is observed.", McpErrorCode.InvalidRequest);
            var disposition = await coordinator.CancelAsync(taskId, cancellationToken).ConfigureAwait(false);
            // SDK 2.2.0 ignores bool and cancels its wait token on any normal return.
            if (disposition == "Unsupported")
                throw new McpProtocolException("AGENT_TASK_CANCEL_UNSUPPORTED: Unity execution cannot be cancelled and remains queryable.", McpErrorCode.InvalidRequest);
            if (disposition is not ("Accepted" or "AlreadyCancelled" or "AlreadyCompleted" or "AlreadyTerminal"))
                throw new McpProtocolException("AGENT_TASK_CANCEL_UNCONFIRMED: " + disposition, McpErrorCode.InvalidRequest);
        }
        await Update(taskId, record => record with { CancellationRequested = true, Task = record.Task with
        { LastUpdatedAt = clock.GetUtcNow(), StatusMessage = "Cancellation requested; execution remains observable until it stops." } }, cancellationToken);
        return true;
    }

    public Task ResolveInputRequestsAsync(string taskId, IDictionary<string, InputResponse> inputResponses, CancellationToken cancellationToken)
        => throw new NotSupportedException("Interactive MCP task input is not supported by this server.");
    public Task SetInputRequestsAsync(string taskId, IDictionary<string, InputRequest> inputRequests, CancellationToken cancellationToken)
        => throw new NotSupportedException("Interactive MCP task input is not supported by this server.");

    private Task Update(string taskId, Func<McpStoredTask, McpStoredTask> update, CancellationToken token)
    {
        using var lease = persistence.Enter(token);
        var record = Load(taskId) ?? throw new KeyNotFoundException(taskId);
        if (!IsTerminal(record.Task.Status)) Save(update(record));
        return Task.CompletedTask;
    }

    private McpStoredTask Complete(McpStoredTask record, McpTaskStatus status, JsonElement? result, JsonElement? error, string message)
    {
        var now = clock.GetUtcNow();
        string? reportPath = null;
        if (result.HasValue) result = RetainReport(record.Task.TaskId, result.Value, out reportPath);
        return record with { OwnedReport = reportPath, Task = record.Task with { Status = status, Result = result, Error = error,
            LastUpdatedAt = now, TimeToLive = now + policy.Retention - record.Task.CreatedAt, StatusMessage = message } };
    }

    private McpStoredTask CommitFailure(McpStoredTask record, string code, string message)
    {
        var error = JsonSerializer.SerializeToElement(new { code = (int)McpErrorCode.InternalError,
            message = code + ": " + message, data = new { diagnosticCode = code, taskId = record.Task.TaskId, lastObservation = record.LastObservation } });
        record = Complete(record, McpTaskStatus.Failed, null, error, message);
        Save(record);
        return record;
    }

    internal async Task MaintainAsync(CancellationToken token)
    {
        string[] active;
        using (persistence.Enter(token)) active = ReadRecords().Where(record => !IsTerminal(record.Task.Status)).Select(record => record.Task.TaskId).ToArray();
        await Parallel.ForEachAsync(active, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (id, ct) =>
        {
            try { await GetTaskAsync(id, ct).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or McpException)
            { Console.Error.WriteLine("MCP task reconciliation: " + exception.Message); }
        });
        Cleanup(token);
    }

    internal void Cleanup(CancellationToken token = default)
    {
        using var lease = persistence.Enter(token);
        var terminal = ReadRecords().Where(record => IsTerminal(record.Task.Status)).OrderBy(record => record.Task.LastUpdatedAt).ToList();
        CleanupOrphans();
        var bytes = StorageBytes();
        var count = terminal.Count;
        foreach (var record in terminal)
        {
            if (!Expired(record) && count <= policy.MaxTerminalRecords && bytes <= policy.MaxStorageBytes) continue;
            var reclaimed = OwnedFiles(record).Where(File.Exists).Sum(path => new FileInfo(path).Length);
            Delete(record);
            bytes -= reclaimed;
            count--;
        }
    }

    private static readonly string[] StorageCategories = ["mcp-tasks", "mcp-task-mappings", "mcp-task-dispatch", "mcp-task-reports"];
    private long StorageBytes() => StorageCategories.Sum(category => Directory.EnumerateFiles(persistence.DirectoryFor(category), "*.json").Sum(path => new FileInfo(path).Length));

    private IEnumerable<string> OwnedFiles(McpStoredTask record)
    {
        yield return PathFor(record.Task.TaskId);
        yield return persistence.PathFor("mcp-task-mappings", persistence.ProjectPath + "\n" + record.Task.TaskId);
        yield return persistence.PathFor("mcp-task-dispatch", persistence.ProjectPath + "\n" + record.Task.TaskId);
        if (record.OwnedReport is { } report && IsOwnedReport(report)) yield return report;
    }

    private void CleanupOrphans()
    {
        var records = ReadRecords();
        var retainedIds = records.Select(record => record.Task.TaskId).ToHashSet(StringComparer.Ordinal);
        var retainedReports = records.Where(record => record.OwnedReport is not null).Select(record => record.OwnedReport!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var category in StorageCategories)
            foreach (var path in Directory.EnumerateFiles(persistence.DirectoryFor(category)))
            {
                if (clock.GetUtcNow() - File.GetLastWriteTimeUtc(path) < policy.Retention) continue;
                if (Path.GetFileName(path).Contains(".tmp-", StringComparison.Ordinal)) { File.Delete(path); continue; }
                if (!path.EndsWith(".json", StringComparison.Ordinal)) continue;
                if (category == "mcp-task-reports" && !retainedReports.Contains(path)) { File.Delete(path); continue; }
                if (category is not ("mcp-task-mappings" or "mcp-task-dispatch")) continue;
                try
                {
                    var document = McpTaskPersistence.Read<JsonElement>(path);
                    if (document.TryGetProperty("McpTaskId", out var identity) && !retainedIds.Contains(identity.GetString()!)) File.Delete(path);
                }
                catch (InvalidDataException exception) { Console.Error.WriteLine(exception.Message); }
            }
    }

    private bool Expired(McpStoredTask record) => record.Task.TimeToLive is { } ttl && clock.GetUtcNow() >= record.Task.CreatedAt + ttl;
    private static bool IsTerminal(McpTaskStatus status) => status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;
    private string PathFor(string id) => persistence.PathFor("mcp-tasks", id);
    private void Save(McpStoredTask record) => McpTaskPersistence.Write(PathFor(record.Task.TaskId), record);
    private McpStoredTask? Load(string id)
    {
        var record = McpTaskPersistence.Read<McpStoredTask>(PathFor(id));
        if (record is not null && (record.Version != Version || record.ProjectIdentity != persistence.ProjectPath || record.Task?.TaskId != id))
            throw new InvalidDataException("MCP_TASK_RECORD_INCOMPATIBLE: " + id);
        return record;
    }

    private List<McpStoredTask> ReadRecords()
    {
        var records = new List<McpStoredTask>();
        foreach (var path in Directory.EnumerateFiles(persistence.DirectoryFor("mcp-tasks"), "*.json"))
        {
            try
            {
                var record = McpTaskPersistence.Read<McpStoredTask>(path);
                if (record is not null && record.Version == Version && record.ProjectIdentity == persistence.ProjectPath && record.Task is not null) records.Add(record);
                else Console.Error.WriteLine("MCP_TASK_RECORD_INCOMPATIBLE: " + path);
            }
            catch (InvalidDataException exception) { Console.Error.WriteLine(exception.Message); }
        }
        return records;
    }

    private void Delete(McpStoredTask record)
    {
        coordinator?.DeleteTracking(record.Task.TaskId);
        foreach (var path in OwnedFiles(record)) File.Delete(path);
    }

    private bool IsOwnedReport(string path) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), persistence.DirectoryFor("mcp-task-reports"), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private JsonElement RetainReport(string taskId, JsonElement result, out string? ownedReport)
    {
        ownedReport = null;
        var node = JsonNode.Parse(result.GetRawText())?.AsObject();
        if (node?["structuredContent"] is not JsonObject payload || payload["reportPath"] is not JsonValue value ||
            !value.TryGetValue<string>(out var source) || string.IsNullOrWhiteSpace(source)) return result;
        try
        {
            source = Path.GetFullPath(source, persistence.ProjectPath);
            if (!source.StartsWith(persistence.ProjectPath + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Report is outside this project.");
            if (!File.Exists(source)) throw new IOException("Report is unavailable.");
            if (new FileInfo(source).Length > policy.MaxReportBytes) throw new IOException("Report exceeds the retained report size limit.");
            ownedReport = persistence.PathFor("mcp-task-reports", taskId);
            if (!string.Equals(source, ownedReport, StringComparison.OrdinalIgnoreCase))
            {
                var temporary = ownedReport + ".tmp-" + Guid.NewGuid().ToString("N");
                try { File.Copy(source, temporary); File.Move(temporary, ownedReport, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            ReplaceReportReferences(payload, payload["reportPath"]!.GetValue<string>(), Path.GetRelativePath(persistence.ProjectPath, ownedReport).Replace('\\', '/'));
            payload["reportRetention"] = new JsonObject { ["available"] = true, ["owned"] = true };
        }
        catch (IOException exception)
        {
            ownedReport = null;
            payload["reportRetention"] = new JsonObject { ["available"] = false, ["reason"] = exception.Message };
        }
        node!["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = payload.ToJsonString() });
        return JsonSerializer.SerializeToElement(node);
    }

    private static void ReplaceReportReferences(JsonNode node, string source, string replacement)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Select(pair => pair.Key).ToArray())
            {
                if (key == "reportPath" && obj[key] is JsonValue value && value.TryGetValue<string>(out var path) && path == source) obj[key] = replacement;
                else if (obj[key] is { } child) ReplaceReportReferences(child, source, replacement);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child is not null) ReplaceReportReferences(child, source, replacement);
    }

    private void ImportLegacy(string source, string destination)
    {
        try
        {
            var task = McpTaskPersistence.Read<McpTaskInfo>(source);
            if (task is null || string.IsNullOrWhiteSpace(task.TaskId) || Path.GetFileName(destination) != Path.GetFileName(PathFor(task.TaskId))) throw new InvalidDataException();
            if (task.Result is { } old && old.ValueKind == JsonValueKind.Object && !old.TryGetProperty("content", out _))
                task = task with { Result = McpToolResultConverter.Serialize(JsonNode.Parse(old.GetRawText())!.AsObject()) };
            McpTaskPersistence.Write(destination, new McpStoredTask(Version, persistence.ProjectPath, task, task.CreatedAt + policy.DefaultExecutionWindow));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            File.Copy(source, destination);
            Console.Error.WriteLine("MCP_TASK_LEGACY_RECORD_UNAVAILABLE: " + source);
        }
    }
}
#pragma warning restore CS0067
