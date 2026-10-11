using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityAgentBridge.Mcp;

/// <summary>One standard result representation for ordinary, observed and recovered operations.</summary>
internal static class McpToolResultConverter
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CallToolResult Convert(JsonObject source, McpHostDiagnostics? diagnostics = null)
    {
        var result = Normalize(source);
        if (diagnostics is not null)
        {
            result["resolvedCliPath"] = diagnostics.ResolvedCliPath;
            result["cliMode"] = diagnostics.CliMode;
            result["cliWarnings"] = new JsonArray(diagnostics.CliWarnings.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            result["packageVersion"] = RuntimeIdentity.PackageVersion;
            result["runtimeVersion"] = RuntimeIdentity.RuntimeVersion;
            result["protocolVersion"] = RuntimeIdentity.ProtocolVersion;
            result["runtimeMode"] = RuntimeIdentity.RuntimeMode;
        }
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.ToJsonString(JsonOptions) }],
            StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions),
            IsError = result["success"]?.GetValue<bool>() != true
        };
    }

    public static JsonElement Serialize(JsonObject source, McpHostDiagnostics? diagnostics = null)
        => JsonSerializer.SerializeToElement(Convert(source, diagnostics), JsonOptions);

    private static JsonObject Normalize(JsonObject source)
    {
        var result = (JsonObject)source.DeepClone();
        var payload = source["metrics"] as JsonObject ?? ParseObject(source["resultPayloadJson"]);
        // Compile tasks carry the existing ToolResult as their business payload.
        if (source["resultKind"] is not null && payload?["success"] is not null && payload["status"] is not null)
        {
            result = (JsonObject)payload.DeepClone();
            foreach (var name in new[] { "taskId", "agentTaskId", "state", "resultKind", "cancellation" })
                if (source[name] is { } value) result[name] = value.DeepClone();
        }
        else if (payload is not null && result["metrics"] is null)
        {
            result["metrics"] = payload.DeepClone();
        }

        if (ParseObject(result["metricsObjectJson"]) is { } metrics)
        {
            result["metrics"] = metrics;
            result.Remove("metricsObjectJson");
        }
        result.Remove("resultPayloadJson");
        var kind = source["resultKind"]?.GetValue<string>();
        var success = kind is not null
            ? string.Equals(kind, "Succeeded", StringComparison.OrdinalIgnoreCase)
            : result["success"]?.GetValue<bool>() ?? string.Equals(result["status"]?.GetValue<string>(), "success", StringComparison.OrdinalIgnoreCase);
        result["success"] = success;
        if (!success && result["errors"] is null && (source["state"] is not null || source["resultKind"] is not null || source["errorCode"] is not null))
        {
            result["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = source["errorCode"]?.GetValue<string>() ?? "AGENT_TASK_FAILURE",
                ["message"] = source["errorMessage"]?.GetValue<string>() ?? source["resultReason"]?.GetValue<string>() ?? source["summary"]?.GetValue<string>() ?? "Operation failed."
            });
        }
        return result;
    }

    private static JsonObject? ParseObject(JsonNode? value)
    {
        if (value is not JsonValue json || !json.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }
}
