using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class BridgeHealthClient
{
    private readonly IUnityEditorProcessDiscovery _windowsProcessDiscovery;

    public BridgeHealthClient()
        : this(new WindowsUnityEditorProcessDiscovery())
    {
    }

    internal BridgeHealthClient(IUnityEditorProcessDiscovery windowsProcessDiscovery)
    {
        _windowsProcessDiscovery = windowsProcessDiscovery;
    }

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
            case "unity.editor_list":
            {
                result = CreateLocalResult(HandleUnityEditorList(queuePaths, commandId, commandSpec.ArgsJson));
                return true;
            }
            case "unity.editor_open":
            {
                result = CreateLocalResult(HandleUnityEditorOpen(queuePaths, commandId, commandSpec.ArgsJson));
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

    private JObject HandleUnityEditorList(QueuePaths queuePaths, string commandId, string argsJson)
    {
        var requestArgs = SafeParseJsonObject(argsJson);
        var filterProjectPath = requestArgs.Value<string>("projectPath");
        var includeBridgeHealth = requestArgs.Value<bool?>("includeBridgeHealth") ?? false;

        if (!OperatingSystem.IsWindows())
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_list",
                "UnsupportedPlatform",
                "unity_editor_list is only validated on Windows in the current release.",
                string.Empty,
                null,
                null,
                null,
                "Run this tool on Windows or add platform-specific validation first.");
        }

        var editors = _windowsProcessDiscovery.Discover();
        if (!string.IsNullOrWhiteSpace(filterProjectPath))
        {
            var normalizedFilter = UnityEditorPathUtility.NormalizePath(filterProjectPath);
            editors = editors
                .Where(editor => !string.IsNullOrWhiteSpace(editor.ProjectPath) &&
                                 string.Equals(UnityEditorPathUtility.NormalizePath(editor.ProjectPath), normalizedFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var payload = new JObject
        {
            ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
            ["commandId"] = commandId,
            ["tool"] = "unity.editor_list",
            ["success"] = true,
            ["status"] = "success",
            ["summary"] = $"Detected {editors.Count} running Unity Editor process(es).",
            ["editorCount"] = editors.Count,
            ["editors"] = new JArray(editors.Select(ToJson).ToArray())
        };

        if (includeBridgeHealth)
        {
            var statusPath = Path.Combine(queuePaths.StatusDirectory, "unity_bridge_status.json");
            var status = File.Exists(statusPath) ? SafeParseJsonObject(File.ReadAllText(statusPath, Encoding.UTF8)) : new JObject();
            var lifecycle = ClassifyLifecycle(File.Exists(statusPath), GetHeartbeatAgeMs(status), status);
            payload["bridgeHealth"] = new JObject
            {
                ["statusFileExists"] = File.Exists(statusPath),
                ["lifecycleState"] = lifecycle.LifecycleState,
                ["healthReason"] = lifecycle.HealthReason,
                ["recommendedActionCode"] = lifecycle.RecommendedActionCode,
                ["recommendedAction"] = lifecycle.RecommendedAction,
                ["projectPath"] = status.Value<string>("projectPath") ?? string.Empty,
                ["statusPath"] = statusPath.Replace('\\', '/')
            };
        }

        return payload;
    }

    private JObject HandleUnityEditorOpen(QueuePaths queuePaths, string commandId, string argsJson)
    {
        var args = SafeParseJsonObject(argsJson);
        var request = new UnityEditorOpenRequest(
            args.Value<string>("projectPath") ?? string.Empty,
            args.Value<string>("unityExecutablePath"),
            args.Value<bool?>("allowVersionFallback") ?? false,
            args.Value<bool?>("waitForBridge") ?? false,
            args.Value<int?>("bridgeReadyTimeoutMs") ?? UnityEditorLaunchSettings.DefaultBridgeReadyTimeoutMs,
            args.Value<int?>("bridgePollIntervalMs") ?? UnityEditorLaunchSettings.DefaultBridgePollIntervalMs,
            args.Value<int?>("maxRunningUnityEditors"));

        if (!OperatingSystem.IsWindows())
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                "UnsupportedPlatform",
                "unity_editor_open is only validated on Windows in the current release.",
                request.ProjectPath,
                null,
                request.UnityExecutablePath,
                null,
                "Run this tool on Windows or add platform-specific validation first.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            return CreateLifecycleFailure(commandId, "unity.editor_open", "ProjectPathMissing", "projectPath is required.", string.Empty, null, request.UnityExecutablePath, null, "Provide a Unity project path.");
        }

        var normalizedProjectPath = UnityEditorPathUtility.NormalizePath(request.ProjectPath);
        if (!Directory.Exists(normalizedProjectPath))
        {
            return CreateLifecycleFailure(commandId, "unity.editor_open", "ProjectPathNotFound", $"Unity project path '{normalizedProjectPath}' was not found.", normalizedProjectPath, null, request.UnityExecutablePath, null, "Fix the path or sync the launcher config.");
        }

        if (!UnityEditorPathUtility.IsUnityProject(normalizedProjectPath))
        {
            return CreateLifecycleFailure(commandId, "unity.editor_open", "NotUnityProject", $"Path '{normalizedProjectPath}' is not a Unity project.", normalizedProjectPath, null, request.UnityExecutablePath, null, "Choose a folder containing Assets and ProjectSettings/ProjectVersion.txt.");
        }

        var lockFilePath = Path.Combine(normalizedProjectPath, "Temp", "UnityLockfile");
        if (File.Exists(lockFilePath))
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                "ProjectLocked",
                $"Unity project '{normalizedProjectPath}' is locked by an existing or stale lock file.",
                normalizedProjectPath,
                null,
                request.UnityExecutablePath,
                null,
                "Clear the stale lock or reuse the existing Unity session.");
        }

        if (!UnityEditorProjectVersionReader.TryReadVersion(normalizedProjectPath, out var projectVersion, out var versionErrorCode))
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                versionErrorCode ?? "ProjectVersionInvalid",
                $"Project version evidence is unavailable for '{normalizedProjectPath}'.",
                normalizedProjectPath,
                null,
                request.UnityExecutablePath,
                null,
                "Open the project manually with the correct Unity version or supply unityExecutablePath.");
        }

        var editors = _windowsProcessDiscovery.Discover();
        var duplicate = editors.FirstOrDefault(editor =>
            !string.IsNullOrWhiteSpace(editor.ProjectPath) &&
            string.Equals(UnityEditorPathUtility.NormalizePath(editor.ProjectPath), normalizedProjectPath, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                "UnityAlreadyOpenForProject",
                $"Unity is already running for '{normalizedProjectPath}'.",
                normalizedProjectPath,
                projectVersion,
                duplicate.ExecutablePath,
                duplicate.ProcessId,
                "Reuse the existing Unity Editor session for this project.",
                duplicate.Warnings);
        }

        var (maxRunningEditors, limitWarnings) = UnityEditorLaunchSettings.ResolveMaxRunningEditors(request.MaxRunningUnityEditors);
        if (editors.Count >= maxRunningEditors)
        {
            var failure = CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                "UnityEditorLimitReached",
                $"Running Unity Editor count {editors.Count} reached the configured limit {maxRunningEditors}.",
                normalizedProjectPath,
                projectVersion,
                request.UnityExecutablePath,
                null,
                "Close another Unity Editor or raise maxRunningUnityEditors.");
            failure["runningEditorCount"] = editors.Count;
            failure["configuredLimit"] = maxRunningEditors;
            failure["warnings"] = new JArray(limitWarnings.Select(w => (JToken)new JValue(w)).ToArray());
            return failure;
        }

        var executablePath = ResolveUnityExecutablePath(request, projectVersion, out var executableCode, out var executableSummary, out var executableWarnings);
        if (executablePath is null)
        {
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                executableCode ?? "UnityExecutableNotFound",
                executableSummary ?? "A matching Unity executable could not be resolved.",
                normalizedProjectPath,
                projectVersion,
                request.UnityExecutablePath,
                null,
                request.AllowVersionFallback
                    ? "Install the matching Unity version or provide a valid unityExecutablePath."
                    : "Install the matching Unity version, or set allowVersionFallback true with a validated unityExecutablePath.",
                executableWarnings.Concat(limitWarnings).ToArray());
        }

        if (!WindowsUnityEditorProcessDiscovery.TryStartUnity(executablePath, normalizedProjectPath, out var processId, out var launchError))
        {
            var launchCode = string.IsNullOrWhiteSpace(launchError) ? "LaunchFailed" : "PermissionDenied";
            return CreateLifecycleFailure(
                commandId,
                "unity.editor_open",
                launchCode,
                launchError ?? $"Failed to start Unity with '{executablePath}'.",
                normalizedProjectPath,
                projectVersion,
                executablePath,
                processId,
                launchCode == "PermissionDenied"
                    ? "Check filesystem and process launch permissions."
                    : "Retry launch or open the project manually.",
                executableWarnings.Concat(limitWarnings).ToArray());
        }

        var response = new JObject
        {
            ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
            ["commandId"] = commandId,
            ["tool"] = "unity.editor_open",
            ["success"] = true,
            ["status"] = "success",
            ["code"] = "Opened",
            ["summary"] = $"Started Unity for '{normalizedProjectPath}'.",
            ["projectPath"] = normalizedProjectPath,
            ["projectVersion"] = projectVersion,
            ["unityExecutablePath"] = executablePath,
            ["processId"] = processId.HasValue ? processId.Value : JValue.CreateNull(),
            ["bridgeReady"] = false,
            ["recommendedAction"] = request.WaitForBridge ? "Wait for bridge readiness to complete." : "Use unity_editor_list or unity_bridge_health to monitor startup.",
            ["warnings"] = new JArray(executableWarnings.Concat(limitWarnings).Distinct(StringComparer.Ordinal).Select(w => (JToken)new JValue(w)).ToArray())
        };

        if (!request.WaitForBridge)
        {
            return response;
        }

        var readyResult = WaitForBridgeReady(new QueuePaths(normalizedProjectPath, QueuePaths.DefaultQueueRoot), normalizedProjectPath, request);
        if (!string.Equals(readyResult.Value<string>("status"), "success", StringComparison.Ordinal))
        {
            readyResult["schemaVersion"] = AgentCommandEnvelope.SchemaVersion;
            readyResult["commandId"] = commandId;
            readyResult["tool"] = "unity.editor_open";
            readyResult["projectVersion"] = projectVersion;
            readyResult["unityExecutablePath"] = executablePath;
            readyResult["processId"] = processId.HasValue ? processId.Value : JValue.CreateNull();
            readyResult["warnings"] = new JArray(executableWarnings.Concat(limitWarnings).Distinct(StringComparer.Ordinal).Select(w => (JToken)new JValue(w)).ToArray());
            return readyResult;
        }

        response["bridgeReady"] = true;
        response["statusPath"] = readyResult["statusPath"];
        response["heartbeatAgeMs"] = readyResult["heartbeatAgeMs"];
        response["recommendedAction"] = "Bridge ready. Unity queue tools can execute.";
        return response;
    }

    private static JObject WaitForBridgeReady(QueuePaths queuePaths, string projectPath, UnityEditorOpenRequest request)
    {
        var statusPath = Path.Combine(queuePaths.StatusDirectory, "unity_bridge_status.json");
        var deadline = DateTime.UtcNow.AddMilliseconds(request.BridgeReadyTimeoutMs);
        while (DateTime.UtcNow <= deadline)
        {
            if (File.Exists(statusPath))
            {
                var status = SafeParseJsonObject(File.ReadAllText(statusPath, Encoding.UTF8));
                var heartbeatAgeToken = GetHeartbeatAgeMs(status);
                if (heartbeatAgeToken.Type != JTokenType.Null)
                {
                    var heartbeatAgeMs = heartbeatAgeToken.Value<long>();
                    var statusProjectPath = status.Value<string>("projectPath");
                    if (!string.IsNullOrWhiteSpace(statusProjectPath) &&
                        string.Equals(UnityEditorPathUtility.NormalizePath(statusProjectPath), projectPath, StringComparison.OrdinalIgnoreCase) &&
                        heartbeatAgeMs <= 10_000 &&
                        ClassifyLifecycle(true, heartbeatAgeToken, status).ToolExecution == "Allowed")
                    {
                        return new JObject
                        {
                            ["success"] = true,
                            ["status"] = "success",
                            ["code"] = "Opened",
                            ["summary"] = "Bridge is ready for the requested project.",
                            ["statusPath"] = statusPath.Replace('\\', '/'),
                            ["heartbeatAgeMs"] = heartbeatAgeMs
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(statusProjectPath) &&
                        !string.Equals(UnityEditorPathUtility.NormalizePath(statusProjectPath), projectPath, StringComparison.OrdinalIgnoreCase) &&
                        heartbeatAgeMs <= 10_000)
                    {
                        return CreateLifecycleFailure(
                            string.Empty,
                            "unity.editor_open",
                            "BridgeProjectMismatch",
                            "Bridge heartbeat became ready for a different Unity project.",
                            projectPath,
                            null,
                            null,
                            null,
                            "Reconnect the MCP server or update the configured project binding.",
                            null,
                            statusPath.Replace('\\', '/'),
                            heartbeatAgeMs);
                    }
                }
            }

            Thread.Sleep(request.BridgePollIntervalMs);
        }

        return CreateLifecycleFailure(
            string.Empty,
            "unity.editor_open",
            "BridgeReadyTimeout",
            $"Bridge did not become ready within {request.BridgeReadyTimeoutMs} ms.",
            projectPath,
            null,
            null,
            null,
            "Wait longer, inspect unity_bridge_health, or open the project manually.",
            null,
            statusPath.Replace('\\', '/'),
            null);
    }

    private string? ResolveUnityExecutablePath(UnityEditorOpenRequest request, string? projectVersion, out string? code, out string? summary, out IReadOnlyList<string> warnings)
    {
        code = null;
        summary = null;
        var collectedWarnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.UnityExecutablePath))
        {
            var normalizedExecutable = UnityEditorPathUtility.NormalizePath(request.UnityExecutablePath);
            if (!UnityEditorPathUtility.IsUnityEditorExecutable(normalizedExecutable))
            {
                code = "UnityExecutableInvalid";
                summary = $"unityExecutablePath '{normalizedExecutable}' is not a Unity Editor executable.";
                warnings = collectedWarnings;
                return null;
            }

            if (!request.AllowVersionFallback &&
                !string.IsNullOrWhiteSpace(projectVersion) &&
                normalizedExecutable.IndexOf(projectVersion, StringComparison.OrdinalIgnoreCase) < 0)
            {
                code = "VersionMismatch";
                summary = $"unityExecutablePath '{normalizedExecutable}' does not match project version '{projectVersion}'.";
                warnings = collectedWarnings;
                return null;
            }

            warnings = collectedWarnings;
            return normalizedExecutable;
        }

        if (string.IsNullOrWhiteSpace(projectVersion))
        {
            code = "ProjectVersionInvalid";
            summary = "Project version is unavailable.";
            warnings = collectedWarnings;
            return null;
        }

        var hubEditorRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
        if (!Directory.Exists(hubEditorRoot))
        {
            code = "UnityExecutableNotFound";
            summary = $"Unity Hub editor root '{hubEditorRoot}' was not found.";
            warnings = collectedWarnings;
            return null;
        }

        var candidate = Path.Combine(hubEditorRoot, projectVersion, "Editor", "Unity.exe");
        if (File.Exists(candidate))
        {
            warnings = collectedWarnings;
            return candidate;
        }

        if (request.AllowVersionFallback)
        {
            var fallbackCandidate = Directory.GetDirectories(hubEditorRoot)
                .Select(path => Path.Combine(path, "Editor", "Unity.exe"))
                .Where(File.Exists)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fallbackCandidate))
            {
                collectedWarnings.Add("VersionFallbackUsed");
                warnings = collectedWarnings;
                return fallbackCandidate;
            }
        }

        code = "UnityExecutableNotFound";
        summary = $"No Unity executable matched project version '{projectVersion}' under '{hubEditorRoot}'.";
        warnings = collectedWarnings;
        return null;
    }

    private static JObject ToJson(UnityEditorInstance instance)
    {
        return new JObject
        {
            ["processId"] = instance.ProcessId,
            ["executablePath"] = instance.ExecutablePath ?? string.Empty,
            ["projectPath"] = instance.ProjectPath ?? string.Empty,
            ["projectVersion"] = instance.ProjectVersion ?? string.Empty,
            ["warnings"] = new JArray(instance.Warnings.Select(w => (JToken)new JValue(w)).ToArray())
        };
    }

    private static JObject CreateLifecycleFailure(
        string commandId,
        string tool,
        string code,
        string summary,
        string projectPath,
        string? projectVersion,
        string? unityExecutablePath,
        int? processId,
        string recommendedAction,
        IEnumerable<string>? warnings = null,
        string? statusPath = null,
        long? heartbeatAgeMs = null)
    {
        var payload = new JObject
        {
            ["schemaVersion"] = AgentCommandEnvelope.SchemaVersion,
            ["commandId"] = commandId,
            ["tool"] = tool,
            ["success"] = false,
            ["status"] = "failed",
            ["code"] = code,
            ["summary"] = summary,
            ["projectPath"] = projectPath,
            ["projectVersion"] = projectVersion is null ? JValue.CreateNull() : new JValue(projectVersion),
            ["unityExecutablePath"] = unityExecutablePath is null ? JValue.CreateNull() : new JValue(unityExecutablePath),
            ["processId"] = processId.HasValue ? new JValue(processId.Value) : JValue.CreateNull(),
            ["bridgeReady"] = false,
            ["recommendedAction"] = recommendedAction,
            ["warnings"] = new JArray((warnings ?? Array.Empty<string>()).Select(w => (JToken)new JValue(w)).ToArray()),
            ["errors"] = new JArray
            {
                new JObject
                {
                    ["code"] = code,
                    ["message"] = summary
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(statusPath))
        {
            payload["statusPath"] = statusPath;
        }

        if (heartbeatAgeMs.HasValue)
        {
            payload["heartbeatAgeMs"] = heartbeatAgeMs.Value;
        }

        return payload;
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
