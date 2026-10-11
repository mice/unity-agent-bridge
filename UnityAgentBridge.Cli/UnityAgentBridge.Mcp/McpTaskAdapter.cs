using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public sealed class McpTaskAdapter
{
    public const string ExtensionId = "io.modelcontextprotocol/tasks";
    private readonly ExternalBridgeClient _client;
    private readonly QueuePaths _queuePaths;
    private readonly McpTaskBindingStore _bindings;
    private readonly McpTaskDispatchStore _dispatches;
    private readonly string _projectIdentity;

    public McpTaskAdapter(ExternalBridgeClient client, QueuePaths queuePaths, string projectIdentity)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queuePaths = queuePaths;
        _projectIdentity = string.IsNullOrWhiteSpace(projectIdentity) ? throw new ArgumentException("Project identity is required.", nameof(projectIdentity)) : projectIdentity;
        _bindings = new McpTaskBindingStore(queuePaths.ProjectPath);
        _dispatches = new McpTaskDispatchStore(queuePaths.ProjectPath);
    }

    public bool Supports(JsonElement? clientCapabilities)
    {
        if (clientCapabilities is not { } value || value.ValueKind != JsonValueKind.Object) return false;
        return value.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty(ExtensionId, out _)
            || value.TryGetProperty("tasks", out _);
    }

    public async Task<JsonObject> CreateAsync(string taskType, string payload, CancellationToken cancellationToken)
    {
        var mcpId = "mcp-" + Guid.NewGuid().ToString("N");
        return await CreateForExternalTaskAsync(mcpId, taskType, payload, 0, cancellationToken);
    }

    public async Task<JsonObject> CreateForExternalTaskAsync(string mcpId, string taskType, string payload, int timeoutMs, CancellationToken cancellationToken)
    {
        if (HasBinding(mcpId)) return await GetAsync(mcpId, cancellationToken);
        var dispatch = new McpTaskDispatch(_projectIdentity, mcpId, _client.CreateCommandId(), taskType,
            McpTaskPersistence.Hash(payload + "\n" + timeoutMs), DateTimeOffset.UtcNow);
        if (!_dispatches.TryCreate(dispatch))
        {
            var previous = _dispatches.Load(_projectIdentity, mcpId)!;
            if (previous.TaskType != taskType || previous.PayloadHash != dispatch.PayloadHash)
                throw new InvalidOperationException("MCP_TASK_DISPATCH_CONFLICT: A task identity cannot be reused for different arguments.");
            return await ReconcileAsync(mcpId, cancellationToken);
        }
        var response = await SendAsync("create", null, taskType, payload, timeoutMs, cancellationToken, dispatch.CommandId);
        _dispatches.SaveResponse(dispatch, response.ToJsonString());
        return BindCreation(dispatch, response);
    }

    private JsonObject BindCreation(McpTaskDispatch dispatch, JsonObject response)
    {
        var agentId = response["agentTaskId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(agentId))
        {
            response["taskId"] = dispatch.McpTaskId;
            response["dispatchCommandId"] = dispatch.CommandId;
            response["creationRejected"] = true;
            response["status"] = "failed";
            return response;
        }
        _bindings.Save(new McpTaskBinding(_projectIdentity, dispatch.McpTaskId, agentId, dispatch.CommandId));
        var state = response["state"]?.GetValue<string>();
        var status = string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase) ? "failed" : string.Equals(state, "Cancelled", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "working";
        return new JsonObject { ["success"] = status == "working", ["taskId"] = dispatch.McpTaskId, ["status"] = status, ["pollIntervalMs"] = 250 };
    }

    internal bool HasDispatch(string taskId) => _dispatches.Load(_projectIdentity, taskId) is not null;

    internal async Task<JsonObject> ReconcileAsync(string taskId, CancellationToken cancellationToken)
    {
        if (HasBinding(taskId)) return await GetAsync(taskId, cancellationToken);
        var dispatch = _dispatches.Load(_projectIdentity, taskId);
        if (dispatch is null) return ObservationError("AGENT_TASK_DISPATCH_PENDING", "No validated dispatch has been recorded yet.", taskId);
        var response = dispatch.ResponseJson;
        if (response is null)
        {
            var resultPath = Path.Combine(_queuePaths.OutboxDirectory, dispatch.CommandId + ".result.json");
            if (!File.Exists(resultPath))
            {
                var unknown = ObservationError("AGENT_TASK_OUTCOME_UNKNOWN", "Creation outcome is not yet observable; the original command will not be replayed.", taskId);
                unknown["dispatchCommandId"] = dispatch.CommandId;
                return unknown;
            }
            response = Unwrap(File.ReadAllText(resultPath)).ToJsonString();
            _dispatches.SaveResponse(dispatch, response);
        }
        var creation = BindCreation(dispatch, JsonNode.Parse(response)!.AsObject());
        return HasBinding(taskId) ? await GetAsync(taskId, cancellationToken) : creation;
    }

    internal void DeleteTracking(string taskId)
    {
        _bindings.Delete(_projectIdentity, taskId);
        _dispatches.Delete(_projectIdentity, taskId);
    }

    public Task<JsonObject> GetAsync(string mcpTaskId, CancellationToken cancellationToken) => SendMappedAsync("query", mcpTaskId, cancellationToken);
    public Task<JsonObject> ResultAsync(string mcpTaskId, CancellationToken cancellationToken) => SendMappedAsync("result", mcpTaskId, cancellationToken);

    public bool HasBinding(string mcpTaskId) => _bindings.Load(_projectIdentity, mcpTaskId) is not null;

    public async Task<JsonObject> WaitForTerminalAsync(string mcpTaskId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await GetAsync(mcpTaskId, cancellationToken);
            var status = MapStatus(response);
            if (status is "completed" or "failed" or "cancelled" || response["observationFailure"]?.GetValue<bool>() == true || response["errorCode"] is not null)
                return response;
            await Task.Delay(250, cancellationToken);
        }
    }

    public async Task<JsonObject> CancelAsync(string mcpTaskId, CancellationToken cancellationToken)
    {
        var response = await SendMappedAsync("cancel", mcpTaskId, cancellationToken);
        var disposition = response["cancellation"]?.GetValue<string>() ?? "NotFound";
        return new JsonObject { ["taskId"] = mcpTaskId, ["disposition"] = disposition, ["status"] = disposition == "Accepted" ? "working" : MapStatus(response) };
    }

    private async Task<JsonObject> SendMappedAsync(string operation, string mcpTaskId, CancellationToken cancellationToken)
    {
        var binding = _bindings.Load(_projectIdentity, mcpTaskId);
        if (binding is null)
        {
            var conflictingBinding = _bindings.FindByMcpTaskId(mcpTaskId);
            return conflictingBinding is not null
                ? Error("IDENTITY_CONFLICT", "MCP task binding belongs to another project identity.")
                : Error("STALE_BINDING", "MCP task binding was not found for this project.");
        }
        JsonObject response;
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        observation.CancelAfter(TimeSpan.FromSeconds(5));
        try { response = await SendAsync(operation, binding.AgentTaskId, null, null, 0, observation.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ObservationError("AGENT_TASK_OBSERVATION_TIMEOUT", "Unity did not answer this observation within five seconds.", mcpTaskId); }
        if (response["errorCode"]?.GetValue<string>() == "NOT_FOUND") return ObservationError("STALE_BINDING", "MCP task binding points to an unavailable AgentTask.", mcpTaskId);
        response["taskId"] = mcpTaskId;
        response["dispatchCommandId"] = binding.CommandId;
        if (response["state"] is null && response["snapshot"]?["state"] is null)
        {
            response["observationFailure"] = true;
            response["status"] = "observation_failed";
            response["success"] = false;
            return response;
        }
        var mappedStatus = MapStatus(response);
        response["status"] = mappedStatus;
        response["success"] = mappedStatus == "completed" && string.Equals(response["resultKind"]?.GetValue<string>(), "Succeeded", StringComparison.OrdinalIgnoreCase);
        response["summary"] = response["resultReason"]?.GetValue<string>() ?? response["errorMessage"]?.GetValue<string>() ?? (mappedStatus == "working" ? "Agent task is in progress." : "Agent task completed.");
        var payloadJson = response["resultPayloadJson"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try { response["metrics"] = JsonNode.Parse(payloadJson); } catch (JsonException) { }
        }
        return response;
    }

    private async Task<JsonObject> SendAsync(string operation, string? agentTaskId, string? taskType, string? payload, int timeoutMs, CancellationToken cancellationToken, string? commandId = null)
    {
        var request = new JsonObject { ["version"] = 1, ["operation"] = operation, ["projectIdentity"] = _projectIdentity };
        if (agentTaskId != null) request["agentTaskId"] = agentTaskId;
        if (taskType != null) request["taskType"] = taskType;
        if (payload != null) request["payload"] = payload;
        if (timeoutMs > 0) request["timeoutMs"] = timeoutMs;
        commandId ??= _client.CreateCommandId();
        var result = await _client.ExecuteAsync(_queuePaths, commandId, new BridgeCommandSpec("unity.agent_task_control", 90000, request.ToJsonString()), cancellationToken);
        return Unwrap(result.RawJson);
    }

    private static JsonObject Unwrap(string json)
    {
        var envelope = JsonNode.Parse(json)?.AsObject();
        if (envelope is null) return Error("INVALID_RESPONSE", "Unity returned an invalid task response.");
        var embedded = envelope["metricsObjectJson"]?.GetValue<string>();
        var response = !string.IsNullOrWhiteSpace(embedded) ? JsonNode.Parse(embedded)?.AsObject() : envelope["metrics"]?.AsObject();
        return response ?? envelope;
    }

    private static string MapStatus(JsonObject response)
    {
        var state = response["state"]?.GetValue<string>() ?? response["snapshot"]?["state"]?.GetValue<string>();
        return state?.ToLowerInvariant() switch { "completed" => "completed", "failed" => "failed", "cancelled" => "cancelled", "created" or "running" => "working", _ => response["success"]?.GetValue<bool>() == false ? "failed" : "working" };
    }

    private static JsonObject Error(string code, string message) => new() { ["success"] = false, ["errorCode"] = code, ["errorMessage"] = message, ["status"] = "failed" };

    internal static JsonObject ObservationError(string code, string message, string taskId) => new()
    { ["success"] = false, ["errorCode"] = code, ["errorMessage"] = message, ["status"] = "observation_failed", ["observationFailure"] = true, ["taskId"] = taskId };
}
