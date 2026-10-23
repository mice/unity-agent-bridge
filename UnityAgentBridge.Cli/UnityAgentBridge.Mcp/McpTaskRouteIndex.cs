using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public sealed record McpTaskRoute(string ProjectId, string ProjectPath, string QueueRoot, string? EditorSessionId,
    string? ConnectionId, DateTimeOffset CreatedAt, int Version = 1)
{
    public static McpTaskRoute From(ProjectContext context) => new(context.ProjectId, context.ProjectPath,
        context.QueuePaths.QueueRoot, context.EditorSessionId, context.ConnectionId, DateTimeOffset.UtcNow);
    public QueuePaths CreateQueuePaths() => new(ProjectPath, QueueRoot, EditorSessionId is null ? null : ProjectId, EditorSessionId);
}

/// <summary>Durable ownership only; execution and results remain in the original project.</summary>
internal sealed record McpTaskRouteIndexEntry(string HandleId, string Kind, McpTaskRoute Route);

public sealed class McpTaskRouteIndex
{
    private readonly string root;
    private readonly int maxRecords;
    public McpTaskRouteIndex(string? root = null, int maxRecords = 10000)
    {
        if (maxRecords < 1) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        this.maxRecords = maxRecords;
        this.root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityAgentBridge", "projects"), "task-index"));
    }
    public void SaveTask(string taskId, McpTaskRoute route) => Save("task", taskId, route);
    public void SaveCommand(string commandId, McpTaskRoute route) => Save("command", commandId, route);
    public McpTaskRoute? FindTask(string taskId) => Find("task", taskId);
    public McpTaskRoute? FindCommand(string commandId) => Find("command", commandId);
    public static void AssertSelectors(McpTaskRoute route, string? projectPath, string? projectId)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((!string.IsNullOrWhiteSpace(projectPath) && !string.Equals(Path.GetFullPath(projectPath), Path.GetFullPath(route.ProjectPath), comparison)) ||
            (!string.IsNullOrWhiteSpace(projectId) && projectId != route.ProjectId))
            throw new InvalidOperationException("TASK_PROJECT_CONFLICT: Selectors disagree with persistent ownership.");
    }
    private string PathFor(string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Identity is required.", nameof(id));
        return Path.Combine(root, kind + "-" + McpTaskPersistence.Hash(id) + ".json");
    }
    private void Save(string kind, string id, McpTaskRoute route)
    {
        if (route.Version != 1 || string.IsNullOrWhiteSpace(route.ProjectId) || !Path.IsPathFullyQualified(route.ProjectPath) || !Path.IsPathFullyQualified(route.QueueRoot))
            throw new InvalidDataException("MCP_TASK_INDEX_INCOMPATIBLE: " + id);
        Directory.CreateDirectory(root);
        using var lease = Enter();
        var previous = Find(kind, id);
        if (previous is not null)
        {
            if (previous.ProjectId != route.ProjectId || previous.ProjectPath != route.ProjectPath ||
                previous.QueueRoot != route.QueueRoot || previous.EditorSessionId != route.EditorSessionId)
                throw new InvalidOperationException("TASK_PROJECT_CONFLICT: Persistent ownership is immutable.");
            return;
        }
        Cleanup();
        if (Directory.EnumerateFiles(root, "*.json").Take(maxRecords).Count() >= maxRecords)
            throw new IOException("MCP_TASK_INDEX_CAPACITY: Existing ownership is preserved.");
        McpTaskPersistence.Write(PathFor(kind, id), new McpTaskRouteIndexEntry(id, kind, route));
    }
    private McpTaskRoute? Find(string kind, string id)
    {
        var document = McpTaskPersistence.Read<System.Text.Json.JsonElement>(PathFor(kind, id));
        if (document.ValueKind == System.Text.Json.JsonValueKind.Undefined) return null;
        if (document.TryGetProperty("HandleId", out var handle) && (handle.GetString() != id || document.GetProperty("Kind").GetString() != kind))
            throw new InvalidDataException("MCP_TASK_INDEX_INCOMPATIBLE: " + id);
        var route = document.TryGetProperty("Route", out var nested)
            ? System.Text.Json.JsonSerializer.Deserialize<McpTaskRoute>(nested.GetRawText())
            : System.Text.Json.JsonSerializer.Deserialize<McpTaskRoute>(document.GetRawText());
        if (route is not null && (route.Version != 1 || string.IsNullOrWhiteSpace(route.ProjectId) ||
            !Path.IsPathFullyQualified(route.ProjectPath) || !Path.IsPathFullyQualified(route.QueueRoot)))
            throw new InvalidDataException("MCP_TASK_INDEX_INCOMPATIBLE: " + id);
        return route;
    }
    /// <summary>Repairs ownership solely from registered project-local retained records, never from queues.</summary>
    public void RepairRegisteredProjects(IEnumerable<UnityMcp.AgentBridge.ProjectRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            try
            {
                var projectRoot = Path.Combine(registration.projectPath, "Library", "AgentBridge");
                foreach (var category in new[] { "mcp-tasks", "mcp-task-mappings", "mcp-task-dispatch", "command-history" })
                {
                    var directory = Path.Combine(projectRoot, category);
                    if (!Directory.Exists(directory)) continue;
                    foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
                    {
                        try
                        {
                            var record = McpTaskPersistence.Read<System.Text.Json.JsonElement>(path);
                            string? Value(string name) => record.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                            var identity = Value("ProjectIdentity") ?? Value("projectPath");
                            if (identity is null || !SamePath(identity, registration.projectPath)) continue;
                            var payload = record.TryGetProperty("payload", out var content) ? content : record;
                            string? Metadata(string name) => payload.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;
                            var storedProjectId = Value("ProjectId") ?? Metadata("projectId");
                            if (storedProjectId is not null && storedProjectId != registration.projectId) continue;
                            var route = new McpTaskRoute(registration.projectId, registration.projectPath, Value("QueueRoot") ?? Value("queueRoot") ?? registration.queueRoot,
                                Value("EditorSessionId") ?? Metadata("editorSessionId"), Value("ConnectionId") ?? Metadata("connectionId"), File.GetLastWriteTimeUtc(path));
                            if (category == "command-history")
                            {
                                if (Value("commandId") is { } command && !command.StartsWith("task-", StringComparison.Ordinal)) SaveCommand(command, route);
                            }
                            else
                            {
                                var task = Value("McpTaskId");
                                if (record.TryGetProperty("Task", out var taskInfo) && taskInfo.TryGetProperty("TaskId", out var taskId)) task = taskId.GetString();
                                if (!string.IsNullOrWhiteSpace(task)) SaveTask(task, route);
                                if (Value("CommandId") is { } command) SaveCommand(command, route);
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
                        { Console.Error.WriteLine("MCP_TASK_INDEX_REPAIR_SKIPPED: " + e.Message); }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { Console.Error.WriteLine("MCP_TASK_INDEX_PROJECT_UNREADABLE: " + registration.projectId + ": " + e.Message); }
        }
    }
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public void Cleanup()
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7)) continue;
            try
            {
                var document = McpTaskPersistence.Read<System.Text.Json.JsonElement>(path);
                if (!document.TryGetProperty("Route", out _)) continue;
                var entry = System.Text.Json.JsonSerializer.Deserialize<McpTaskRouteIndexEntry>(document.GetRawText());
                if (entry is null) continue;
                var projectRoot = Path.Combine(entry.Route.ProjectPath, "Library", "AgentBridge");
                var exists = entry.Kind == "task"
                    ? File.Exists(Path.Combine(projectRoot, "mcp-tasks", McpTaskPersistence.Hash(entry.HandleId) + ".json")) ||
                        File.Exists(Path.Combine(projectRoot, "mcp-task-dispatch", McpTaskPersistence.Hash(entry.Route.ProjectPath + "\n" + entry.HandleId) + ".json")) ||
                        File.Exists(Path.Combine(projectRoot, "command-history", McpTaskPersistence.Hash("task-" + entry.HandleId) + ".json"))
                    : File.Exists(Path.Combine(projectRoot, "command-history", McpTaskPersistence.Hash(entry.HandleId) + ".json"));
                // Preserve live or retained tracking; only old orphan mappings are reclaimable.
                if (entry.Kind == "task" && !exists) File.Delete(path);
                else if (entry.Kind == "command" && exists)
                {
                    var historyPath = Path.Combine(projectRoot, "command-history", McpTaskPersistence.Hash(entry.HandleId) + ".json");
                    var history = McpTaskPersistence.Read<System.Text.Json.JsonElement>(historyPath);
                    if (history.TryGetProperty("expiresAt", out var expires) && expires.GetDateTimeOffset() <= DateTimeOffset.UtcNow) File.Delete(path);
                }
            }
            catch (InvalidDataException e) { Console.Error.WriteLine(e.Message); }
        }
    }
    private IDisposable Enter()
    {
        var mutex = new Mutex(false, "UnityAgentBridge.TaskIndex." + McpTaskPersistence.Hash(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root));
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        return new Lease(mutex);
    }
    private sealed class Lease(Mutex mutex) : IDisposable
    { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
}
