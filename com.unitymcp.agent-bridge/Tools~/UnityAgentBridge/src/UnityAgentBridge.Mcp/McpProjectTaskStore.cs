using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

#pragma warning disable CS0067
public sealed class McpProjectTaskStore(McpTaskRouteIndex index, Func<ProjectContext, McpTaskStore> createStore,
    Func<McpTaskRoute, McpTaskStore> restoreStore, Func<ProjectContext?> currentContext) : IMcpTaskStore
{
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;
    public async Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken)
    {
        var context = currentContext() ?? throw new InvalidOperationException("PROJECT_NOT_RESOLVED: Task allocation requires a project context.");
        var task = await createStore(context).CreateTaskAsync(cancellationToken).ConfigureAwait(false);
        index.SaveTask(task.TaskId, McpTaskRoute.From(context));
        return task;
    }
    private McpTaskStore Store(string taskId) => restoreStore(index.FindTask(taskId) ?? throw new KeyNotFoundException("MCP_TASK_INDEX_MISSING: " + taskId));
    public Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        => index.FindTask(taskId) is null ? Task.FromResult<McpTaskInfo?>(null) : Store(taskId).GetTaskAsync(taskId, cancellationToken);
    public Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken) => Store(taskId).SetCompletedAsync(taskId, result, cancellationToken);
    public Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken) => Store(taskId).SetFailedAsync(taskId, error, cancellationToken);
    public Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken) => Store(taskId).SetCancelledAsync(taskId, cancellationToken);
    public Task ResolveInputRequestsAsync(string taskId, IDictionary<string, InputResponse> responses, CancellationToken cancellationToken)
        => Store(taskId).ResolveInputRequestsAsync(taskId, responses, cancellationToken);
    public Task SetInputRequestsAsync(string taskId, IDictionary<string, InputRequest> requests, CancellationToken cancellationToken)
        => Store(taskId).SetInputRequestsAsync(taskId, requests, cancellationToken);
}
#pragma warning restore CS0067
