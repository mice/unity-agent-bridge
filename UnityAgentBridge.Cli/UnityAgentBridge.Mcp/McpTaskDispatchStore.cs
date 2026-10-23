namespace UnityAgentBridge.Mcp;

internal sealed record McpTaskDispatch(string ProjectIdentity, string McpTaskId, string CommandId,
    string TaskType, string PayloadHash, DateTimeOffset CreatedAt, string? ResponseJson = null, int Version = 1, string? ProjectId = null, string? EditorSessionId = null, string? ConnectionId = null, string? QueueRoot = null);

/// <summary>A durable dispatch intent is never automatically replayed.</summary>
internal sealed class McpTaskDispatchStore(string projectPath)
{
    private readonly McpTaskPersistence persistence = new(projectPath);
    private string PathFor(string project, string task) => persistence.PathFor("mcp-task-dispatch", project + "\n" + task);

    internal McpTaskDispatch? Load(string project, string task)
    {
        using var lease = persistence.Enter();
        var record = McpTaskPersistence.Read<McpTaskDispatch>(PathFor(project, task));
        if (record is not null && (record.Version != 1 || record.ProjectIdentity != project || record.McpTaskId != task ||
            Path.GetFileName(record.CommandId) != record.CommandId || string.IsNullOrWhiteSpace(record.CommandId)))
            throw new InvalidDataException("MCP_TASK_DISPATCH_INCOMPATIBLE: " + task);
        return record;
    }

    internal bool TryCreate(McpTaskDispatch record)
    {
        using var lease = persistence.Enter();
        if (Load(record.ProjectIdentity, record.McpTaskId) is not null) return false;
        McpTaskPersistence.Write(PathFor(record.ProjectIdentity, record.McpTaskId), record);
        return true;
    }

    internal void SaveResponse(McpTaskDispatch record, string response)
    {
        using var lease = persistence.Enter();
        McpTaskPersistence.Write(PathFor(record.ProjectIdentity, record.McpTaskId), record with { ResponseJson = response });
    }

    internal void Delete(string project, string task)
    {
        using var lease = persistence.Enter();
        File.Delete(PathFor(project, task));
    }
}
