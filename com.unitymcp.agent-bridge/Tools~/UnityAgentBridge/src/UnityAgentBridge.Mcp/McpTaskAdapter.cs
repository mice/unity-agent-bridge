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
    private readonly string _projectIdentity;

    public McpTaskAdapter(ExternalBridgeClient client, QueuePaths queuePaths, string projectIdentity)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queuePaths = queuePaths;
        _projectIdentity = string.IsNullOrWhiteSpace(projectIdentity) ? throw new ArgumentException("Project identity is required.", nameof(projectIdentity)) : projectIdentity;
        _bindings = new McpTaskBindingStore(queuePaths.ProjectPath);
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
        var response = await SendAsync("create", null, taskType, payload, timeoutMs, cancellationToken);
        var agentId = response["agentTaskId"]?.GetValue<string>();
        if (response["success"]?.GetValue<bool>() != true || string.IsNullOrWhiteSpace(agentId)) return response;
        _bindings.Save(new McpTaskBinding(_projectIdentity, mcpId, agentId, response["commandId"]?.GetValue<string>()));
        return new JsonObject { ["success"] = true, ["taskId"] = mcpId, ["status"] = "working", ["pollIntervalMs"] = 250 };
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
            if (status is "completed" or "failed" or "cancelled" || response["success"]?.GetValue<bool>() == false)
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
        var response = await SendAsync(operation, binding.AgentTaskId, null, null, 0, cancellationToken);
        if (response["errorCode"]?.GetValue<string>() == "NOT_FOUND") return Error("STALE_BINDING", "MCP task binding points to a missing AgentTask.");
        response["taskId"] = mcpTaskId;
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

    private async Task<JsonObject> SendAsync(string operation, string? agentTaskId, string? taskType, string? payload, int timeoutMs, CancellationToken cancellationToken)
    {
        var request = new JsonObject { ["version"] = 1, ["operation"] = operation, ["projectIdentity"] = _projectIdentity };
        if (agentTaskId != null) request["agentTaskId"] = agentTaskId;
        if (taskType != null) request["taskType"] = taskType;
        if (payload != null) request["payload"] = payload;
        if (timeoutMs > 0) request["timeoutMs"] = timeoutMs;
        var commandId = _client.CreateCommandId();
        var result = await _client.ExecuteAsync(_queuePaths, commandId, new BridgeCommandSpec("unity.agent_task_control", 120000, request.ToJsonString()), cancellationToken);
        var envelope = JsonNode.Parse(result.RawJson)?.AsObject();
        if (envelope is null) return Error("INVALID_RESPONSE", "Unity returned an invalid task response.");
        var embedded = envelope["metricsObjectJson"]?.GetValue<string>();
        var response = !string.IsNullOrWhiteSpace(embedded) ? JsonNode.Parse(embedded)?.AsObject() : null;
        return response ?? envelope;
    }

    private static string MapStatus(JsonObject response)
    {
        var state = response["state"]?.GetValue<string>() ?? response["snapshot"]?["state"]?.GetValue<string>();
        return state?.ToLowerInvariant() switch { "completed" => "completed", "failed" => "failed", "cancelled" => "cancelled", _ => "working" };
    }

    private static JsonObject Error(string code, string message) => new() { ["success"] = false, ["errorCode"] = code, ["errorMessage"] = message, ["status"] = "failed" };
}
