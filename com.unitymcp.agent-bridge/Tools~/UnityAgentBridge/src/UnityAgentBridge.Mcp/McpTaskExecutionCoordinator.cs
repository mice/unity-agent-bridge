using System.Text.Json.Nodes;

namespace UnityAgentBridge.Mcp;

/// <summary>Coordinates standard MCP tasks with the project-bound Unity AgentTask projection.</summary>
public sealed class McpTaskExecutionCoordinator
{
    private readonly McpTaskAdapter _adapter;
    private McpTaskStore? _store;

    internal void AttachStore(McpTaskStore store) => _store = store;

    public McpTaskExecutionCoordinator(McpTaskAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    internal bool Supports(McpTaskExecutionContext.PendingTaskRequest request) => request?.IsAgentTaskOperation == true;

    internal async Task ValidateAsync(McpTaskExecutionContext.PendingTaskRequest? request, CancellationToken cancellationToken)
    {
        if (request?.IsTemplateRun != true) return;
        var response = await _adapter.ValidateTemplateAsync(request.StructuredPayload, cancellationToken);
        if (response["success"]?.GetValue<bool>() != true)
            throw new ArgumentException(response["errorMessage"]?.GetValue<string>() ?? "Template admission failed.");
    }

    internal async Task CreateAsync(string mcpTaskId, McpTaskExecutionContext.PendingTaskRequest request, CancellationToken cancellationToken)
    {
        if (!Supports(request)) return;
        if (_adapter.HasBinding(mcpTaskId)) return;
        var taskType = request.AgentTaskType;
        try
        {
            await _adapter.CreateForExternalTaskAsync(mcpTaskId, taskType, request.StructuredPayload, request.TimeoutMs, cancellationToken);
        }
        catch (Exception exception) when ((exception is TimeoutException or IOException) && _adapter.HasDispatch(mcpTaskId))
        { /* The store reconciles the original journal; never invoke the ordinary path. */ }
    }

    public Task<JsonObject> QueryAsync(string mcpTaskId, CancellationToken cancellationToken)
        => _adapter.ReconcileAsync(mcpTaskId, cancellationToken);

    public Task<JsonObject> WaitForTerminalAsync(string mcpTaskId, CancellationToken cancellationToken)
        => _store is null ? _adapter.WaitForTerminalAsync(mcpTaskId, cancellationToken) : _store.WaitForTerminalAsync(mcpTaskId, cancellationToken);

    internal void DeleteTracking(string mcpTaskId) => _adapter.DeleteTracking(mcpTaskId);

    public async Task<string> CancelAsync(string mcpTaskId, CancellationToken cancellationToken)
    {
        var response = await _adapter.CancelAsync(mcpTaskId, cancellationToken);
        return response["disposition"]?.GetValue<string>() ?? "NotFound";
    }

    public bool IsMapped(string mcpTaskId) => _adapter.HasBinding(mcpTaskId);
}
