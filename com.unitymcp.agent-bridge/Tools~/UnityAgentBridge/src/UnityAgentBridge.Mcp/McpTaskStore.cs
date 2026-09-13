using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityAgentBridge.Mcp;

/// <summary>Durable project-scoped store used by the MCP SDK task pipeline.</summary>
#pragma warning disable CS0067
public sealed class McpTaskStore : IMcpTaskStore
{
    private readonly string _root;
    private readonly object _gate = new();
    private readonly McpTaskExecutionCoordinator? _coordinator;

    public McpTaskStore(string projectPath, McpTaskExecutionCoordinator? coordinator = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("Project path is required.", nameof(projectPath));
        _root = Path.Combine(Path.GetFullPath(projectPath), "Temp", "AgentBridge", "mcp-tasks");
        Directory.CreateDirectory(_root);
        _coordinator = coordinator;
    }

    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;

    public Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var taskId = "mcp-" + Guid.NewGuid().ToString("N");
        var pending = McpTaskExecutionContext.ConsumePending();
        var task = NewTask(taskId, McpTaskStatus.Working, now, now, TimeSpan.FromHours(24), 250, "Task is running.", null, null, new Dictionary<string, InputRequest>(StringComparer.Ordinal));
        Save(task);
        if (pending is not null && _coordinator is not null)
        {
            try
            {
                _coordinator.CreateAsync(taskId, pending, cancellationToken).GetAwaiter().GetResult();
                pending.McpTaskId = taskId;
            }
            catch (Exception exception)
            {
                task = NewTask(taskId, McpTaskStatus.Failed, now, DateTimeOffset.UtcNow, task.TimeToLive, task.PollIntervalMs, "Unity AgentTask creation failed.", null,
                    JsonSerializer.SerializeToElement(new { code = "AGENT_TASK_CREATE_FAILED", message = exception.Message }), task.InputRequests);
                Save(task);
                McpTaskExecutionContext.Clear();
            }
        }
        return Task.FromResult(task);
    }

    public async Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(taskId)) return null;
        McpTaskInfo? task;
        lock (_gate)
        {
            task = Load(taskId);
            if (task is null) return null;
            if (task.TimeToLive is { } ttl && ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - task.CreatedAt > ttl)
            {
                Delete(taskId);
                return null;
            }
        }

        if (_coordinator is not null && _coordinator.IsMapped(taskId))
        {
            var projection = await _coordinator.QueryAsync(taskId, cancellationToken).ConfigureAwait(false);
            task = ApplyProjection(task!, projection);
            Save(task);
        }

        return task;
    }

    public async Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken)
    {
        if (_coordinator is not null && _coordinator.IsMapped(taskId))
        {
            var projection = await _coordinator.QueryAsync(taskId, cancellationToken).ConfigureAwait(false);
            var projected = ApplyProjection(Load(taskId) ?? throw new KeyNotFoundException($"MCP task '{taskId}' was not found."), projection);
            if (projected.Status is McpTaskStatus.Failed or McpTaskStatus.Cancelled or McpTaskStatus.Completed)
            {
                Save(projected with { Result = result.Clone() });
                return;
            }
        }
        await UpdateAsync(task => NewTask(task.TaskId, McpTaskStatus.Completed, task.CreatedAt, DateTimeOffset.UtcNow, task.TimeToLive, task.PollIntervalMs, "Task completed.", result.Clone(), null, task.InputRequests), taskId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken)
    {
        if (_coordinator is not null && _coordinator.IsMapped(taskId))
        {
            var projection = await _coordinator.QueryAsync(taskId, cancellationToken).ConfigureAwait(false);
            var projected = ApplyProjection(Load(taskId) ?? throw new KeyNotFoundException($"MCP task '{taskId}' was not found."), projection);
            if (projected.Status is McpTaskStatus.Failed or McpTaskStatus.Cancelled or McpTaskStatus.Completed)
            {
                Save(projected);
                return;
            }
        }
        await UpdateAsync(task => NewTask(task.TaskId, McpTaskStatus.Failed, task.CreatedAt, DateTimeOffset.UtcNow, task.TimeToLive, task.PollIntervalMs, "Task failed.", null, error.Clone(), task.InputRequests), taskId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_coordinator is not null && _coordinator.IsMapped(taskId))
        {
            var disposition = await _coordinator.CancelAsync(taskId, cancellationToken).ConfigureAwait(false);
            return disposition is "Accepted" or "AlreadyCancelled";
        }

        lock (_gate)
        {
            var task = Load(taskId);
            if (task is null || task.Status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled) return false;
            Save(NewTask(task.TaskId, task.Status, task.CreatedAt, DateTimeOffset.UtcNow, task.TimeToLive, task.PollIntervalMs, "Task cancellation requested; execution remains observable until it stops.", task.Result, task.Error, task.InputRequests));
            return true;
        }
    }

    public Task ResolveInputRequestsAsync(string taskId, IDictionary<string, InputResponse> inputResponses, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetInputRequestsAsync(string taskId, IDictionary<string, InputRequest> inputRequests, CancellationToken cancellationToken)
        => UpdateAsync(task => NewTask(task.TaskId, McpTaskStatus.InputRequired, task.CreatedAt, DateTimeOffset.UtcNow, task.TimeToLive, task.PollIntervalMs, "Input is required.", task.Result, task.Error, new Dictionary<string, InputRequest>(inputRequests, StringComparer.Ordinal)), taskId, cancellationToken);

    private Task UpdateAsync(Func<McpTaskInfo, McpTaskInfo> update, string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var task = Load(taskId) ?? throw new KeyNotFoundException($"MCP task '{taskId}' was not found.");
            Save(update(task));
        }
        return Task.CompletedTask;
    }

    private static McpTaskInfo NewTask(string taskId, McpTaskStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt, TimeSpan? ttl, long? pollIntervalMs, string? message, JsonElement? result, JsonElement? error, IReadOnlyDictionary<string, InputRequest>? inputRequests)
        => new(taskId, status, createdAt, updatedAt, ttl, pollIntervalMs, message, result, error, inputRequests);

    private static McpTaskInfo ApplyProjection(McpTaskInfo task, JsonObject projection)
    {
        var status = projection["status"]?.GetValue<string>()?.ToLowerInvariant() switch
        {
            "completed" => McpTaskStatus.Completed,
            "failed" => McpTaskStatus.Failed,
            "cancelled" => McpTaskStatus.Cancelled,
            _ => McpTaskStatus.Working
        };
        var message = projection["resultReason"]?.GetValue<string>() ?? projection["errorMessage"]?.GetValue<string>() ?? task.StatusMessage;
        var projectedJson = JsonSerializer.SerializeToElement(projection);
        return status switch
        {
            McpTaskStatus.Completed => task with { Status = status, LastUpdatedAt = DateTimeOffset.UtcNow, StatusMessage = message, Result = projectedJson, Error = null },
            McpTaskStatus.Failed or McpTaskStatus.Cancelled => task with { Status = status, LastUpdatedAt = DateTimeOffset.UtcNow, StatusMessage = message, Error = projectedJson },
            _ => task with { Status = status, LastUpdatedAt = DateTimeOffset.UtcNow, StatusMessage = message }
        };
    }

    private void Save(McpTaskInfo task)
    {
        lock (_gate)
        {
            var path = PathFor(task.TaskId);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(task));
            File.Move(temp, path, true);
        }
    }

    private McpTaskInfo? Load(string taskId)
    {
        var path = PathFor(taskId);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<McpTaskInfo>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    private void Delete(string taskId)
    {
        var path = PathFor(taskId);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string taskId)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(taskId))).ToLowerInvariant();
        return Path.Combine(_root, safe + ".json");
    }
}
#pragma warning restore CS0067
