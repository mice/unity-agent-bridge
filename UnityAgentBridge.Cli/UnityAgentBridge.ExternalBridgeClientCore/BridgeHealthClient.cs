using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class BridgeHealthClient
{
    public BridgeLifecycleStatus EvaluateLifecycle(QueuePaths queuePaths)
    {
        var statusPath = Path.Combine(queuePaths.StatusDirectory, "unity_bridge_status.json");
        var statusFileExists = File.Exists(statusPath);
        var status = statusFileExists ? SafeParseJsonObject(File.ReadAllText(statusPath, Encoding.UTF8)) : new JObject();
        return ClassifyLifecycle(statusFileExists, GetHeartbeatAgeMs(status), status);
    }

    public bool TryHandleLocalCommand(QueuePaths queuePaths, BridgeCommandSpec commandSpec, string commandId, out ToolResultEnvelope result)
    {
        result = default!;
        switch (commandSpec.Tool)
        {
            case "mcp.echo":
            {
                var payload = new JObject
                {
                    ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
                    ["commandId"] = commandId,
                    ["tool"] = "mcp.echo",
                    ["success"] = true,
                    ["status"] = "success",
                    ["summary"] = "mcp_echo completed locally.",
                    ["echo"] = JToken.Parse(commandSpec.ArgsJson)
                };
                result = CreateLocalResult(payload);
                return true;
            }
            case "unity.bridge_health":
            {
                var statusPath = Path.Combine(queuePaths.StatusDirectory, "unity_bridge_status.json");
                var status = File.Exists(statusPath) ? SafeParseJsonObject(File.ReadAllText(statusPath, Encoding.UTF8)) : new JObject();
                var lifecycle = ClassifyLifecycle(File.Exists(statusPath), GetHeartbeatAgeMs(status), status);
                var payload = new JObject
                {
                    ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
                    ["commandId"] = commandId,
                    ["tool"] = "unity.bridge_health",
                    ["success"] = true,
                    ["status"] = "success",
                    ["summary"] = "Bridge health collected.",
                    ["queueRoot"] = queuePaths.QueueRoot,
                    ["inboxCount"] = CommandStore.CountJsonFiles(queuePaths.InboxDirectory),
                    ["processingCount"] = CommandStore.CountJsonFiles(queuePaths.ProcessingDirectory),
                    ["outboxRecentCount"] = CommandStore.CountJsonFiles(queuePaths.OutboxDirectory),
                    ["statusFileExists"] = File.Exists(statusPath),
                    ["heartbeatAgeMs"] = GetHeartbeatAgeMs(status),
                    ["lifecycleState"] = lifecycle.LifecycleState,
                    ["healthReason"] = lifecycle.HealthReason,
                    ["reconnectRequired"] = lifecycle.ReconnectRequired,
                    ["recommendedActionCode"] = lifecycle.RecommendedActionCode,
                    ["recommendedAction"] = lifecycle.RecommendedAction,
                    ["toolExecution"] = lifecycle.ToolExecution,
                    ["currentCommandId"] = status.Value<string>("currentCommandId") ?? string.Empty,
                    ["currentStage"] = status.Value<string>("currentStage") ?? string.Empty,
                    ["isCompiling"] = status["isCompiling"] ?? JValue.CreateNull(),
                    ["isUpdating"] = status["isUpdating"] ?? JValue.CreateNull(),
                    ["isPlaying"] = status["isPlaying"] ?? JValue.CreateNull(),
                    ["projectPath"] = status.Value<string>("projectPath") ?? string.Empty,
                    ["currentCompileEpoch"] = status["currentCompileEpoch"] ?? JValue.CreateNull(),
                    ["activeTargetEpochs"] = status["activeTargetEpochs"] ?? new JArray(),
                    ["activeCompileCommandIds"] = status["activeCompileCommandIds"] ?? new JArray(),
                    ["compileLifecycleStage"] = status.Value<string>("compileLifecycleStage") ?? string.Empty,
                    ["compileLastTransition"] = status.Value<string>("compileLastTransition") ?? string.Empty,
                    ["compileLastTransitionAtUtc"] = status.Value<string>("compileLastTransitionAtUtc") ?? string.Empty,
                    ["compileTimeoutReason"] = status.Value<string>("compileTimeoutReason") ?? string.Empty,
                    ["stalePrimaryClassification"] = status.Value<string>("stalePrimaryClassification") ?? string.Empty,
                    ["staleEvidencePriorityPath"] = status.Value<string>("staleEvidencePriorityPath") ?? string.Empty,
                    ["staleHeartbeatAgeMs"] = status["staleHeartbeatAgeMs"] ?? JValue.CreateNull(),
                    ["staleConfiguredProjectPath"] = status.Value<string>("staleConfiguredProjectPath") ?? string.Empty,
                    ["staleDetectedProjectPath"] = status.Value<string>("staleDetectedProjectPath") ?? string.Empty,
                    ["staleProjectBindingKind"] = status.Value<string>("staleProjectBindingKind") ?? string.Empty,
                    ["staleRuntimeIdentity"] = status.Value<string>("staleRuntimeIdentity") ?? string.Empty,
                    ["lastError"] = status.Value<string>("lastError") ?? string.Empty,
                    ["statusPath"] = statusPath.Replace('\\', '/')
                };
                result = CreateLocalResult(payload);
                return true;
            }
            case "unity.bridge_submit_only":
            {
                var args = SafeParseJsonObject(commandSpec.ArgsJson);
                var targetTool = args.Value<string>("tool");
                if (string.IsNullOrWhiteSpace(targetTool))
                {
                    result = CreateLocalResult(CreateInvalidArgs(commandId, "unity.bridge_submit_only", "CLI_BRIDGE_TOOL_REQUIRED", "bridge-submit-only requires a tool."));
                    return true;
                }

                var targetTimeoutMs = args.Value<int?>("timeoutMs") ?? commandSpec.TimeoutMs;
                EnsurePositiveTimeout(targetTimeoutMs);
                var lifecycle = EvaluateLifecycle(queuePaths);
                if (lifecycle.ToolExecution == BridgeLifecycleStatus.BlockedBeforeDispatch)
                {
                    result = CreateLocalResult(CreateLifecycleBlocked(commandId, "unity.bridge_submit_only", targetTool, lifecycle));
                    return true;
                }

                var targetArgs = args["args"] is JObject argsObject ? argsObject.ToString(Formatting.None) : "{}";
                var targetCommandJson = AgentCommandEnvelope.Build(commandId, targetTool, targetTimeoutMs, targetArgs);
                new CommandStore().WriteInboxAtomic(queuePaths, commandId, targetCommandJson);

                var payload = new JObject
                {
                    ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
                    ["commandId"] = commandId,
                    ["tool"] = "unity.bridge_submit_only",
                    ["success"] = true,
                    ["status"] = "success",
                    ["summary"] = "Command submitted.",
                    ["queueRoot"] = queuePaths.QueueRoot,
                    ["submittedTool"] = targetTool
                };
                result = CreateLocalResult(payload);
                return true;
            }
            case "unity.bridge_wait_result":
            {
                var args = SafeParseJsonObject(commandSpec.ArgsJson);
                var targetCommandId = args.Value<string>("commandId");
                if (string.IsNullOrWhiteSpace(targetCommandId))
                {
                    result = CreateLocalResult(CreateInvalidArgs(commandId, "unity.bridge_wait_result", "CLI_COMMAND_ID_REQUIRED", "bridge-wait-result requires a commandId."));
                    return true;
                }

                var targetTimeoutMs = args.Value<int?>("timeoutMs") ?? commandSpec.TimeoutMs;
                EnsurePositiveTimeout(targetTimeoutMs);
                try
                {
                    result = new CommandStore().WaitForResultAsync(queuePaths, targetCommandId, targetTimeoutMs, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (TimeoutException)
                {
                    var payload = new JObject
                    {
                        ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
                        ["commandId"] = targetCommandId,
                        ["tool"] = "unity.bridge_wait_result",
                        ["success"] = false,
                        ["status"] = "timeout",
                        ["summary"] = $"Timed out waiting for result '{targetCommandId}'."
                    };
                    result = CreateLocalResult(payload);
                }

                return true;
            }
            default:
                return false;
        }
    }

    public static JObject CreateLifecycleBlocked(string commandId, string tool, string targetTool, BridgeLifecycleStatus lifecycle)
    {
        var message = $"Unity bridge tool execution is blocked before dispatch because lifecycleState={lifecycle.LifecycleState}, healthReason={lifecycle.HealthReason}. {lifecycle.RecommendedAction}";
        return new JObject
        {
            ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
            ["commandId"] = commandId,
            ["tool"] = tool,
            ["success"] = false,
            ["status"] = "blocked",
            ["summary"] = message,
            ["targetTool"] = targetTool ?? string.Empty,
            ["lifecycleState"] = lifecycle.LifecycleState,
            ["healthReason"] = lifecycle.HealthReason,
            ["reconnectRequired"] = lifecycle.ReconnectRequired,
            ["recommendedActionCode"] = lifecycle.RecommendedActionCode,
            ["recommendedAction"] = lifecycle.RecommendedAction,
            ["toolExecution"] = lifecycle.ToolExecution,
            ["errors"] = new JArray
            {
                new JObject
                {
                    ["code"] = "CLI_BRIDGE_LIFECYCLE_BLOCKED",
                    ["message"] = message
                }
            }
        };
    }

    private static ToolResultEnvelope CreateLocalResult(JObject payload)
    {
        var rawJson = payload.ToString(Formatting.None);
        var status = payload.Value<string>("status") ?? string.Empty;
        return new ToolResultEnvelope(rawJson, status, !CommandStoreIsKnownStatus(status));
    }

    private static JObject CreateInvalidArgs(string commandId, string tool, string code, string message)
    {
        return new JObject
        {
            ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
            ["commandId"] = commandId,
            ["tool"] = tool,
            ["success"] = false,
            ["status"] = "invalid_args",
            ["summary"] = message,
            ["errors"] = new JArray
            {
                new JObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            }
        };
    }

    private static JObject SafeParseJsonObject(string rawJson)
    {
        var parsed = JToken.Parse(rawJson);
        return parsed as JObject ?? new JObject();
    }

    private static JToken GetHeartbeatAgeMs(JObject status)
    {
        var heartbeatUtc = status.Value<string>("heartbeatUtc");
        if (string.IsNullOrWhiteSpace(heartbeatUtc))
        {
            return JValue.CreateNull();
        }

        if (!DateTime.TryParse(heartbeatUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return JValue.CreateNull();
        }

        return Math.Max(0, (long)(DateTime.UtcNow - parsed).TotalMilliseconds);
    }

    private static void EnsurePositiveTimeout(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            throw new BridgeCommandValidationException("--timeout-ms must be greater than 0.");
        }
    }

    private static BridgeLifecycleStatus ClassifyLifecycle(
        bool statusFileExists,
        JToken heartbeatAgeMs,
        JObject status)
    {
        if (!statusFileExists)
        {
            return BridgeLifecycleStatus.Degraded("UnityUnavailable", "Reconnect", "Start Unity or reconnect the MCP server so the bridge can publish status.");
        }

        if (heartbeatAgeMs.Type is JTokenType.Null)
        {
            return new BridgeLifecycleStatus("starting", "BridgeQueueUnavailable", false, "Retry", "Wait for the Unity bridge heartbeat to initialize.", "RetryableTimeout");
        }

        var ageMs = heartbeatAgeMs.Value<long>();
        if (ageMs > 10_000)
        {
            return BridgeLifecycleStatus.Degraded("UnityUnavailable", "Reconnect", "Restart Unity or reconnect the MCP server because the bridge heartbeat is stale.");
        }

        var currentStage = status.Value<string>("currentStage") ?? string.Empty;
        if (currentStage.IndexOf("shutting", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new BridgeLifecycleStatus("stopping", "ShutdownRequested", true, "Reconnect", "Wait for shutdown to finish, then reconnect the MCP server.", BridgeLifecycleStatus.BlockedBeforeDispatch);
        }

        var projectBindingKind = status.Value<string>("staleProjectBindingKind") ?? string.Empty;
        if (string.Equals(projectBindingKind, "explicit", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeLifecycleStatus.Degraded("ProjectMismatch", "UpdateConfig", "Update the configured Unity project binding or reconnect the MCP server for the intended project.");
        }

        var stalePrimaryClassification = status.Value<string>("stalePrimaryClassification") ?? string.Empty;
        if (string.Equals(stalePrimaryClassification, "stale_runtime", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeLifecycleStatus.Degraded("RuntimePathMismatch", "Reconnect", "Reconnect the MCP server so runtime identity and project binding can be refreshed.");
        }

        return new BridgeLifecycleStatus("ready", "None", false, "None", "No action required.", "Allowed");
    }

    private static bool CommandStoreIsKnownStatus(string status)
    {
        return status is "success" or "failed" or "timeout" or "invalid_args" or "unsupported" or "blocked" or "exception" or "cancelled";
    }
}

public sealed record BridgeLifecycleStatus(
    string LifecycleState,
    string HealthReason,
    bool ReconnectRequired,
    string RecommendedActionCode,
    string RecommendedAction,
    string ToolExecution)
{
    public const string BlockedBeforeDispatch = "BlockedBeforeDispatch";

    public static BridgeLifecycleStatus Degraded(string healthReason, string recommendedActionCode, string recommendedAction)
    {
        return new BridgeLifecycleStatus("degraded", healthReason, true, recommendedActionCode, recommendedAction, BlockedBeforeDispatch);
    }
}
