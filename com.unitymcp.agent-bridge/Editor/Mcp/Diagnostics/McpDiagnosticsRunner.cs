using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcp.AgentBridge;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpDiagnosticsRunner : IMcpDiagnosticsRunner
    {
        private static readonly string[] FrozenToolNames =
        {
            "mcp_echo",
            "unity_bridge_health",
            "unity_bridge_submit_only",
            "unity_bridge_wait_result",
            "mcp__unity__ping",
            "mcp__unity__project_get_info",
            "mcp__unity__compile",
            "mcp__unity__get_console",
            "mcp__unity__assetdatabase_search",
            "mcp__unity__get_selection_info",
            "mcp__unity__get_gameobject_component_info",
            "mcp__unity__run_static_method",
            "mcp__unity__run_diagnostic",
            "mcp__unity__run_editmode_tests",
            "mcp__unity__run_playmode_tests",
            "mcp__unity__agent_bridge_self_test",
        };
        private readonly McpEnvironmentProbe _environmentProbe;
        private readonly McpPathResolver _pathResolver;
        private readonly IAsyncProcessRunner _processRunner;

        public McpDiagnosticsRunner()
            : this(new AsyncProcessRunner())
        {
        }

        internal McpDiagnosticsRunner(
            McpEnvironmentProbe environmentProbe,
            McpPathResolver pathResolver,
            IAsyncProcessRunner processRunner)
        {
            _environmentProbe = environmentProbe ?? throw new ArgumentNullException(nameof(environmentProbe));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _processRunner = processRunner;
        }

        private McpDiagnosticsRunner(IAsyncProcessRunner processRunner)
            : this(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), processRunner),
                new McpPathResolver(),
                processRunner)
        {
        }

        public async Task<IReadOnlyList<McpDiagnosticCheck>> RunAsync(
            McpEditorSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var results = new List<McpDiagnosticCheck>();
            DotnetSdkProbeResult dotnetSdkProbe = null;
            try
            {
                await _environmentProbe.SnapshotAsync(settings, cancellationToken);
            }
            catch (Exception exception)
            {
                results.Add(CreateExceptionCheck("MCP005", ".NET 8 SDK", "Install .NET 8 SDK, then run Build Local Runtime.", exception));
            }

            if (!ContainsCode(results, "MCP005"))
            {
                try
                {
                    var runtimeBuilder = new McpRuntimeBuilder(_processRunner ?? new AsyncProcessRunner(), _pathResolver, TimeSpan.FromSeconds(30));
                    dotnetSdkProbe = await runtimeBuilder.ProbeDotnetSdkAsync(settings.DotnetPath, cancellationToken);
                }
                catch (Exception exception)
                {
                    results.Add(CreateExceptionCheck("MCP005", ".NET 8 SDK", "Install .NET 8 SDK, then run Build Local Runtime.", exception));
                }
            }

            results.Add(TryCreateCheck("MCP001", "Bridge Settings", "Open Project Settings to enable Unity Agent Bridge.", IsBridgeEnabled));
            results.Add(TryCreateCheck("MCP002", "Queue Writable", "Check Temp/AgentBridge permissions and path settings.", QueueWritable));
            if (!ContainsCode(results, "MCP003"))
            {
                results.Add(TryCreateCheck("MCP003", "Executable Runtime", "Run Build Local Runtime, then Prepare Runtime.", () => CliPresent(settings)));
            }

            if (!ContainsCode(results, "MCP004"))
            {
                results.Add(TryCreateCheck("MCP004", "Runtime Binding", "Prepare the project-local MCP runtime and apply managed client config.", () => RuntimeBindingPresent(settings)));
            }

            if (!ContainsCode(results, "MCP005"))
            {
                results.Add(CreateDotnetSdkCheck(dotnetSdkProbe));
            }

            results.Add(TryCreateCheck("MCP006", "Server Files", "Repair the prepared MCP runtime payload.", () => ServerFilesPresent(settings)));
            results.Add(TryCreateCheck("MCP007", "Dependencies", "Run Build Local Runtime, then Prepare Runtime.", () => DependenciesPresent(settings)));
            results.Add(TryCreateCheck("MCP008", "CLI", "Run Build Local Runtime, then Prepare Runtime.", () => CliPresent(settings)));
            results.Add(CreateRoslynPayloadSourceCheck(settings));
            results.Add(CreateRoslynPreparedRuntimeCheck(settings));

            try
            {
                var probeResult = await RunProbeAsync(settings, cancellationToken);
                results.Add(CreateProbeListCheck(probeResult));
                results.Add(CreateProbePingCheck(probeResult));
            }
            catch (Exception exception)
            {
                results.Add(CreateExceptionCheck("MCP009", "MCP Tool List", "Check the C# MCP executable, prepared runtime payload, and Unity bridge availability.", exception));
                results.Add(CreateExceptionCheck("MCP010", "MCP Ping", "Check the C# MCP executable, prepared runtime payload, and Unity bridge availability.", exception));
            }

            return results;
        }

        private static bool ContainsCode(IReadOnlyList<McpDiagnosticCheck> checks, string code)
        {
            for (var index = 0; index < checks.Count; index++)
            {
                if (string.Equals(checks[index].Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static McpDiagnosticCheck TryCreateCheck(string code, string summary, string remediation, Func<bool> evaluator)
        {
            try
            {
                return CreateCheck(code, evaluator(), summary, remediation);
            }
            catch (Exception exception)
            {
                return CreateExceptionCheck(code, summary, remediation, exception);
            }
        }

        private static McpDiagnosticCheck CreateCheck(string code, bool ok, string summary, string remediation)
        {
            return new McpDiagnosticCheck
            {
                Code = code,
                Severity = ok ? McpDiagnosticSeverity.Info : McpDiagnosticSeverity.Error,
                Summary = summary,
                Details = ok ? "OK" : "Missing or invalid",
                Remediation = remediation,
                Duration = TimeSpan.Zero,
            };
        }

        private static McpDiagnosticCheck CreateToolCheck(string code, ToolProbeResult probe, string summary, string remediation)
        {
            var ok = probe != null && probe.IsAvailable;
            return new McpDiagnosticCheck
            {
                Code = code,
                Severity = ok ? McpDiagnosticSeverity.Info : McpDiagnosticSeverity.Error,
                Summary = summary,
                Details = ok ? probe.VersionText : "Missing",
                Remediation = remediation,
                Duration = TimeSpan.Zero,
            };
        }

        private static McpDiagnosticCheck CreateDotnetSdkCheck(DotnetSdkProbeResult probe)
        {
            var versionText = probe != null ? probe.Net8SdkVersion : string.Empty;
            var details = probe != null && !string.IsNullOrWhiteSpace(probe.Stdout)
                ? probe.Stdout.Replace('\r', ' ').Replace('\n', ' ').Trim()
                : string.Empty;
            var ok = probe != null && probe.HasNet8Sdk;
            return new McpDiagnosticCheck
            {
                Code = "MCP005",
                Severity = ok ? McpDiagnosticSeverity.Info : McpDiagnosticSeverity.Error,
                Summary = ".NET 8 SDK",
                Details = ok ? versionText : "Missing .NET 8 SDK" + (string.IsNullOrWhiteSpace(details) ? string.Empty : " (installed SDKs: " + details + ")"),
                Remediation = "Install .NET 8 SDK, then run Build Local Runtime.",
                Duration = TimeSpan.Zero,
            };
        }

        private static McpDiagnosticCheck CreateProbeListCheck(ProbeRunResult probeResult)
        {
            if (probeResult == null)
            {
                return CreateProbeFailureCheck("MCP009", "MCP Tool List", "Probe result missing.");
            }

            if (!probeResult.Success)
            {
                return CreateProbeFailureCheck("MCP009", "MCP Tool List", probeResult.ErrorDetails, probeResult.Duration);
            }

            for (var index = 0; index < FrozenToolNames.Length; index++)
            {
                if (!probeResult.ToolNames.Contains(FrozenToolNames[index]))
                {
                    return CreateProbeFailureCheck("MCP009", "MCP Tool List", "Missing frozen MCP tool names.", probeResult.Duration);
                }
            }

            return new McpDiagnosticCheck
            {
                Code = "MCP009",
                Severity = McpDiagnosticSeverity.Info,
                Summary = "MCP Tool List",
                Details = probeResult.ToolNames.Count + " frozen MCP tools listed",
                Remediation = "Repair C# MCP server startup if tool enumeration fails.",
                Duration = probeResult.Duration,
            };
        }

        private static McpDiagnosticCheck CreateProbePingCheck(ProbeRunResult probeResult)
        {
            if (probeResult == null)
            {
                return CreateProbeFailureCheck("MCP010", "MCP Ping", "Probe result missing.");
            }

            if (!probeResult.Success)
            {
                return CreateProbeFailureCheck("MCP010", "MCP Ping", probeResult.ErrorDetails, probeResult.Duration);
            }

            if (!string.Equals(probeResult.PingStatus, "success", StringComparison.OrdinalIgnoreCase) || probeResult.PingIsError)
            {
                return CreateProbeFailureCheck("MCP010", "MCP Ping", BuildPingFailureDetails(probeResult), probeResult.Duration);
            }

            return new McpDiagnosticCheck
            {
                Code = "MCP010",
                Severity = McpDiagnosticSeverity.Info,
                Summary = "MCP Ping",
                Details = "status=success",
                Remediation = "Ensure Unity Editor and Bridge are running if ping fails.",
                Duration = probeResult.Duration,
            };
        }

        private static McpDiagnosticCheck CreateProbeFailureCheck(string code, string summary, string details, TimeSpan? duration = null)
        {
            return new McpDiagnosticCheck
            {
                Code = code,
                Severity = McpDiagnosticSeverity.Error,
                Summary = summary,
                Details = string.IsNullOrEmpty(details) ? "Probe failed" : details,
                Remediation = "Check the C# MCP executable, prepared runtime payload, and Unity bridge availability.",
                Duration = duration ?? TimeSpan.Zero,
            };
        }

        private static string BuildPingFailureDetails(ProbeRunResult probeResult)
        {
            var details = "Ping did not return status=success.";
            if (probeResult == null || string.IsNullOrWhiteSpace(probeResult.HealthLifecycleState))
            {
                return details;
            }

            return details +
                   " lifecycleState=" + probeResult.HealthLifecycleState +
                   " healthReason=" + probeResult.HealthReason +
                   " recommendedActionCode=" + probeResult.RecommendedActionCode +
                   " toolExecution=" + probeResult.ToolExecution +
                   " recommendedAction=" + probeResult.RecommendedAction;
        }

        private static McpDiagnosticCheck CreateExceptionCheck(string code, string summary, string remediation, Exception exception)
        {
            return new McpDiagnosticCheck
            {
                Code = code,
                Severity = McpDiagnosticSeverity.Error,
                Summary = summary,
                Details = exception != null ? exception.Message : "Unexpected diagnostic exception.",
                Remediation = remediation,
                Duration = TimeSpan.Zero,
            };
        }

        private McpDiagnosticCheck CreateRoslynPayloadSourceCheck(McpEditorSettings settings)
        {
            try
            {
                var payloadSourcePath = ResolveRoslynPayloadSourcePath(settings);
                var exists = !string.IsNullOrWhiteSpace(payloadSourcePath) && File.Exists(payloadSourcePath);
                return new McpDiagnosticCheck
                {
                    Code = "MCP011",
                    Severity = exists ? McpDiagnosticSeverity.Info : McpDiagnosticSeverity.Error,
                    Summary = "Roslyn Build Input",
                    Details = exists
                        ? payloadSourcePath
                        : "Missing package build input: " + (string.IsNullOrWhiteSpace(payloadSourcePath) ? "<unresolved>" : payloadSourcePath),
                    Remediation = "Restore package-contained Roslyn compiler source, then run Build Local Runtime.",
                    Duration = TimeSpan.Zero,
                };
            }
            catch (Exception exception)
            {
                return CreateExceptionCheck("MCP011", "Roslyn Build Input", "Restore package-contained Roslyn compiler source.", exception);
            }
        }

        private McpDiagnosticCheck CreateRoslynPreparedRuntimeCheck(McpEditorSettings settings)
        {
            try
            {
                var runtimePayloadPath = ResolveRoslynPreparedRuntimePath(settings);
                var exists = !string.IsNullOrWhiteSpace(runtimePayloadPath) && File.Exists(runtimePayloadPath);
                return new McpDiagnosticCheck
                {
                    Code = "MCP012",
                    Severity = exists ? McpDiagnosticSeverity.Info : McpDiagnosticSeverity.Error,
                    Summary = "Roslyn Prepared Runtime",
                    Details = exists
                        ? runtimePayloadPath
                        : "Missing prepared runtime payload: " + (string.IsNullOrWhiteSpace(runtimePayloadPath) ? "<unresolved>" : runtimePayloadPath),
                    Remediation = "Run Build Local Runtime, then Prepare Runtime.",
                    Duration = TimeSpan.Zero,
                };
            }
            catch (Exception exception)
            {
                return CreateExceptionCheck("MCP012", "Roslyn Prepared Runtime", "Run Build Local Runtime, then Prepare Runtime.", exception);
            }
        }

        private static bool IsBridgeEnabled()
        {
            return AgentBridgeLocalPreferences.BridgeEnabled;
        }

        private static bool QueueWritable()
        {
            var loadResult = AgentBridgeSettingsLoader.Load();
            var settings = loadResult.Settings;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            }

            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                return false;
            }

            try
            {
                var paths = new AgentBridgePaths(projectRoot.FullName, settings);
                Directory.CreateDirectory(paths.QueueRoot);
                var probePath = Path.Combine(paths.QueueRoot, ".mcp.write.test");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool RuntimeBindingPresent(McpEditorSettings settings)
        {
            var launcherPath = _pathResolver.ResolveLauncherPath(settings);
            return File.Exists(launcherPath)
                   && ServerFilesPresent(settings)
                   && CliPresent(settings);
        }

        private bool ServerFilesPresent(McpEditorSettings settings)
        {
            var root = _pathResolver.ResolveMcpServerRoot(settings);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return false;
            }

            var cliRoot = Path.Combine(root, "cli");
            if (!Directory.Exists(cliRoot))
            {
                return false;
            }

            var executableName = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "unity-agent-bridge.exe"
                : "unity-agent-bridge";
            var ridExecutablePath = Path.Combine(cliRoot, "out", McpRuntimeInitializer.GetCurrentRid(), executableName);
            var rootExecutablePath = Path.Combine(cliRoot, executableName);

            if (File.Exists(ridExecutablePath) || File.Exists(rootExecutablePath))
            {
                return true;
            }

            return false;
        }

        private bool DependenciesPresent(McpEditorSettings settings)
        {
            return CliPresent(settings);
        }

        private bool CliPresent(McpEditorSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.CliExecutablePath))
            {
                return File.Exists(settings.CliExecutablePath);
            }

            var root = _pathResolver.ResolveCliRoot(settings);
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var executableName = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "unity-agent-bridge.exe"
                : "unity-agent-bridge";
            return File.Exists(Path.Combine(root, "out", "win-x64", executableName))
                   || File.Exists(Path.Combine(root, executableName))
                   || File.Exists(Path.Combine(root, "unity-agent-bridge.exe"));
        }

        private async Task<ProbeRunResult> RunProbeAsync(McpEditorSettings settings, CancellationToken cancellationToken)
        {
            var cliExecutablePath = ResolveCliExecutablePath(settings);
            if (string.IsNullOrEmpty(cliExecutablePath))
            {
                return ProbeRunResult.Fail("C# MCP executable could not be resolved.");
            }

            var runner = _processRunner ?? new AsyncProcessRunner();
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var request = new ProcessExecutionRequest
            {
                FilePath = cliExecutablePath,
                Arguments = new[] { "mcp-probe" },
                WorkingDirectory = Path.GetDirectoryName(cliExecutablePath) ?? _pathResolver.GetProjectRoot(),
                Environment = new Dictionary<string, string>
                {
                    ["UNITY_AGENT_BRIDGE_PROJECT_PATH"] = projectRoot,
                },
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, settings != null ? settings.DiagnosticTimeoutMs : 30000)),
                CancellationMode = ProcessCancellationMode.TerminateOnCancel,
            };

            try
            {
                var result = await runner.RunAsync(request, cancellationToken);
                if (result == null || result.Outcome != ProcessOutcome.Completed || string.IsNullOrWhiteSpace(result.Stdout))
                {
                    return ProbeRunResult.Fail(result != null ? result.Stderr : "Probe returned no output.", result != null ? result.Duration : TimeSpan.Zero);
                }

                return ProbeRunResult.Parse(result.Stdout, result.Stderr, result.Duration);
            }
            catch (Exception exception)
            {
                return ProbeRunResult.Fail(exception.Message);
            }
        }

        private string ResolveCliExecutablePath(McpEditorSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.CliExecutablePath) && File.Exists(settings.CliExecutablePath))
            {
                return Path.GetFullPath(settings.CliExecutablePath);
            }

            var cliRoot = _pathResolver.ResolveCliRoot(settings);
            if (string.IsNullOrWhiteSpace(cliRoot))
            {
                return string.Empty;
            }

            var executableName = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "unity-agent-bridge.exe"
                : "unity-agent-bridge";

            var candidates = new[]
            {
                Path.Combine(cliRoot, "out", "win-x64", executableName),
                Path.Combine(cliRoot, executableName),
                Path.Combine(cliRoot, "unity-agent-bridge.exe"),
            };

            for (var index = 0; index < candidates.Length; index++)
            {
                if (File.Exists(candidates[index]))
                {
                    return Path.GetFullPath(candidates[index]);
                }
            }

            return string.Empty;
        }

        private string ResolveRoslynPayloadSourcePath(McpEditorSettings settings)
        {
            var toolsRoot = _pathResolver.ResolveToolsRoot(settings);
            if (string.IsNullOrWhiteSpace(toolsRoot))
            {
                return string.Empty;
            }

            return Path.Combine(
                toolsRoot,
                "UnityAgentBridge",
                "src",
                "UnityAgentBridge.RoslynCompiler",
                "UnityAgentBridge.RoslynCompiler.csproj");
        }

        private string ResolveRoslynPreparedRuntimePath(McpEditorSettings settings)
        {
            var runtimeRoot = _pathResolver.ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                return string.Empty;
            }

            return Path.Combine(
                runtimeRoot,
                "UnityAgentBridge",
                "roslyn-execution",
                "out",
                "win-x64",
                "unity-roslyn-compiler.exe");
        }

        private sealed class ProbeRunResult
        {
            public bool Success { get; private set; }
            public string ErrorDetails { get; private set; } = string.Empty;
            public HashSet<string> ToolNames { get; } = new HashSet<string>(StringComparer.Ordinal);
            public string PingStatus { get; private set; } = string.Empty;
            public bool PingIsError { get; private set; }
            public string HealthLifecycleState { get; private set; } = string.Empty;
            public string HealthReason { get; private set; } = string.Empty;
            public string RecommendedActionCode { get; private set; } = string.Empty;
            public string RecommendedAction { get; private set; } = string.Empty;
            public string ToolExecution { get; private set; } = string.Empty;
            public TimeSpan Duration { get; private set; }

            public static ProbeRunResult Fail(string errorDetails, TimeSpan? duration = null)
            {
                return new ProbeRunResult
                {
                    Success = false,
                    ErrorDetails = string.IsNullOrEmpty(errorDetails) ? "Probe failed." : errorDetails,
                    Duration = duration ?? TimeSpan.Zero,
                };
            }

            public static ProbeRunResult Parse(string stdout, string stderr, TimeSpan duration)
            {
                if (string.IsNullOrWhiteSpace(stdout))
                {
                    return Fail(string.IsNullOrWhiteSpace(stderr) ? "Probe returned empty stdout." : stderr, duration);
                }

                try
                {
                    var document = JObject.Parse(stdout);
                    var toolNamesToken = document["toolNames"] as JArray;
                    var pingResultToken = document["pingResult"] as JObject;
                    var structuredContentToken = pingResultToken?["structuredContent"] as JObject;
                    var pingStatus = structuredContentToken?["status"]?.Value<string>() ?? string.Empty;
                    var pingIsError = pingResultToken?["isError"]?.Value<bool>() ?? false;
                    var healthStructuredContentToken = (document["healthResult"] as JObject)?["structuredContent"] as JObject;

                    if (toolNamesToken == null || toolNamesToken.Count == 0 || string.IsNullOrEmpty(pingStatus))
                    {
                        return Fail(BuildMalformedProbeDetails(stdout, stderr, "Probe output missing required fields."), duration);
                    }

                    var parsed = new ProbeRunResult
                    {
                        Success = true,
                        PingStatus = pingStatus,
                        PingIsError = pingIsError,
                        HealthLifecycleState = healthStructuredContentToken?["lifecycleState"]?.Value<string>() ?? string.Empty,
                        HealthReason = healthStructuredContentToken?["healthReason"]?.Value<string>() ?? string.Empty,
                        RecommendedActionCode = healthStructuredContentToken?["recommendedActionCode"]?.Value<string>() ?? string.Empty,
                        RecommendedAction = healthStructuredContentToken?["recommendedAction"]?.Value<string>() ?? string.Empty,
                        ToolExecution = healthStructuredContentToken?["toolExecution"]?.Value<string>() ?? string.Empty,
                        Duration = duration,
                    };

                    for (var index = 0; index < toolNamesToken.Count; index++)
                    {
                        var toolName = toolNamesToken[index]?.Value<string>();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            parsed.ToolNames.Add(toolName);
                        }
                    }

                    return parsed;
                }
                catch (Exception exception)
                {
                    return Fail(BuildMalformedProbeDetails(stdout, stderr, exception.Message), duration);
                }
            }

            private static string BuildMalformedProbeDetails(string stdout, string stderr, string summary)
            {
                var stdoutSummary = SummarizeForDiagnostics(stdout);
                var stderrSummary = SummarizeForDiagnostics(stderr);
                return string.Format(
                    "{0} stdout={1} stderr={2}",
                    summary,
                    stdoutSummary,
                    stderrSummary);
            }

            private static string SummarizeForDiagnostics(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return "<empty>";
                }

                var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (normalized.Length > 400)
                {
                    normalized = normalized.Substring(0, 400) + "...";
                }

                return normalized;
            }
        }
    }
}
