using System.Text.Json.Nodes;

namespace UnityAgentBridge.Mcp;

/// <summary>Coordinates standard MCP tasks with the project-bound Unity AgentTask projection.</summary>
public sealed class McpTaskExecutionCoordinator
{
    private readonly McpTaskAdapter _adapter;

    public McpTaskExecutionCoordinator(McpTaskAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    internal bool Supports(McpTaskExecutionContext.PendingTaskRequest request) => request?.IsAgentTaskOperation == true;

    internal async Task CreateAsync(string mcpTaskId, McpTaskExecutionContext.PendingTaskRequest request, CancellationToken cancellationToken)
    {
        if (!Supports(request)) return;
        var taskType = request.IsCompile ? "compile" : request.IsPlayModeTest ? "run_playmode_tests" : "run_editmode_tests";
        var response = await _adapter.CreateForExternalTaskAsync(mcpTaskId, taskType, request.StructuredPayload, request.TimeoutMs, cancellationToken);
        if (response["success"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException(response["errorMessage"]?.GetValue<string>() ?? "Unity AgentTask creation failed.");
        }
    }

    public Task<JsonObject> QueryAsync(string mcpTaskId, CancellationToken cancellationToken)
        => _adapter.GetAsync(mcpTaskId, cancellationToken);

    public Task<JsonObject> WaitForTerminalAsync(string mcpTaskId, CancellationToken cancellationToken)
        => _adapter.WaitForTerminalAsync(mcpTaskId, cancellationToken);

    public async Task<string> CancelAsync(string mcpTaskId, CancellationToken cancellationToken)
    {
        var response = await _adapter.CancelAsync(mcpTaskId, cancellationToken);
        return response["disposition"]?.GetValue<string>() ?? "NotFound";
    }

    public bool IsMapped(string mcpTaskId) => _adapter.HasBinding(mcpTaskId);
}
