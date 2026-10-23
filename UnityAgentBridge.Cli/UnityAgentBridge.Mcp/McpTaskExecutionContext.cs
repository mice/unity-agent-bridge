using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

/// <summary>Carries the SDK-created MCP task identity across the task filter and background tool handler.</summary>
internal static class McpTaskExecutionContext
{
    private static readonly AsyncLocal<PendingTaskRequest?> Pending = new();
    public static string? CurrentTaskId => Pending.Value?.McpTaskId;

    public static void Prepare(string toolName, string argumentsJson, ProjectContext? projectContext = null)
    {
        Pending.Value = new PendingTaskRequest(toolName, argumentsJson) { ProjectContext = projectContext };
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
        public ProjectContext? ProjectContext { get; init; }

        public bool IsEditModeTest => string.Equals(ToolName, "unity_tests_run_edit_mode", StringComparison.Ordinal);
        public bool IsCompile => string.Equals(ToolName, "unity_project_compile", StringComparison.Ordinal);
        public bool IsPlayModeTest => string.Equals(ToolName, "unity_tests_run_play_mode", StringComparison.Ordinal);
        public bool IsVerifiedCompileFailureAnalysis => string.Equals(ToolName, "unity_analysis_compile_failure", StringComparison.Ordinal);
        public bool IsVerifiedAssetProblemAnalysis => string.Equals(ToolName, "unity_analysis_asset_problem", StringComparison.Ordinal);
        public bool IsTemplateRun => string.Equals(ToolName, "unity_agent_task_template_run", StringComparison.Ordinal);
        public bool IsAgentTaskOperation => IsTemplateRun || IsEditModeTest || IsCompile || IsPlayModeTest || IsVerifiedCompileFailureAnalysis || IsVerifiedAssetProblemAnalysis;
        internal string AgentTaskType => IsTemplateRun ? "template_run" : IsVerifiedCompileFailureAnalysis ? "analysis_compile_failure" : IsVerifiedAssetProblemAnalysis ? "analysis_asset_problem" : IsCompile ? "compile" : IsPlayModeTest ? "run_playmode_tests" : IsEditModeTest ? "run_editmode_tests" : string.Empty;

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
