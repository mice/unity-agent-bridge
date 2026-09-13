using System.Text.Json;

namespace UnityAgentBridge.Mcp;

/// <summary>Carries the SDK-created MCP task identity across the task filter and background tool handler.</summary>
internal static class McpTaskExecutionContext
{
    private static readonly AsyncLocal<PendingTaskRequest?> Pending = new();
    public static string? CurrentTaskId => Pending.Value?.McpTaskId;

    public static void Prepare(string toolName, string argumentsJson)
    {
        Pending.Value = new PendingTaskRequest(toolName, argumentsJson);
    }

    public static PendingTaskRequest? ConsumePending()
    {
        return Pending.Value;
    }

    public static void Clear() => Pending.Value = null;

    public sealed class PendingTaskRequest
    {
        public PendingTaskRequest(string toolName, string argumentsJson)
        {
            ToolName = toolName;
            ArgumentsJson = argumentsJson;
        }

        public string ToolName { get; }
        public string ArgumentsJson { get; }
        public string? McpTaskId { get; set; }

        public bool IsEditModeTest => string.Equals(ToolName, "unity_tests_run_edit_mode", StringComparison.Ordinal);

        public string Payload
        {
            get
            {
                try
                {
                    using var document = JsonDocument.Parse(ArgumentsJson);
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("filter", out var filter) &&
                        filter.ValueKind == JsonValueKind.String)
                    {
                        return filter.GetString() ?? string.Empty;
                    }
                }
                catch (JsonException)
                {
                    // Invalid arguments are handled by the ordinary MCP schema validator.
                }

                return ArgumentsJson;
            }
        }

        public string StructuredPayload => ArgumentsJson;

        public int TimeoutMs
        {
            get
            {
                try
                {
                    using var document = JsonDocument.Parse(ArgumentsJson);
                    return document.RootElement.TryGetProperty("timeoutMs", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var timeout)
                        ? timeout
                        : 0;
                }
                catch (JsonException)
                {
                    return 0;
                }
            }
        }
    }
}
