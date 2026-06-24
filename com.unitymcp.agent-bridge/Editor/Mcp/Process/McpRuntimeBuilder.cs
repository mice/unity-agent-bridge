using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpRuntimeBuilder
    {
        private readonly IAsyncProcessRunner _processRunner;
        private readonly McpPathResolver _pathResolver;
        private readonly TimeSpan _timeout;

        public McpRuntimeBuilder()
            : this(new AsyncProcessRunner(), new McpPathResolver(), TimeSpan.FromMinutes(5))
        {
        }

        internal McpRuntimeBuilder(IAsyncProcessRunner processRunner, McpPathResolver pathResolver, TimeSpan timeout)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _timeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMinutes(5);
        }

        public async Task<McpRuntimeBuildResult> BuildAsync(McpEditorSettings settings, CancellationToken cancellationToken)
        {
            var dotnetPath = ResolveDotnetPath(settings);
            var sdkProbe = await ProbeDotnetSdkAsync(dotnetPath, cancellationToken).ConfigureAwait(false);
            if (!sdkProbe.HasNet8Sdk)
            {
                return McpRuntimeBuildResult.Fail(
                    "dotnet_sdk_missing",
                    "Install .NET 8 SDK before building the local MCP runtime.",
                    sdkProbe.Stdout,
                    sdkProbe.Stderr);
            }

            var toolsRoot = ResolveToolsRoot(settings);
            if (string.IsNullOrWhiteSpace(toolsRoot) || !Directory.Exists(toolsRoot))
            {
                return McpRuntimeBuildResult.Fail(
                    "payload_root_missing",
                    "Package Tools~ root could not be resolved.",
                    sdkProbe.Stdout,
                    sdkProbe.Stderr);
            }

            var buildScriptPath = Path.Combine(toolsRoot, "UnityAgentBridge", "runtime-build", "Build-LocalRuntime.ps1");
            if (!File.Exists(buildScriptPath))
            {
                return McpRuntimeBuildResult.Fail(
                    "runtime_build_script_missing",
                    "Package runtime build script is missing: " + buildScriptPath,
                    sdkProbe.Stdout,
                    sdkProbe.Stderr);
            }

            var cliProjectPath = Path.Combine(toolsRoot, "UnityAgentBridge", "src", "UnityAgentBridge.Cli", "UnityAgentBridge.Cli.csproj");
            var roslynProjectPath = Path.Combine(toolsRoot, "UnityAgentBridge", "src", "UnityAgentBridge.RoslynCompiler", "UnityAgentBridge.RoslynCompiler.csproj");
            if (!File.Exists(cliProjectPath))
            {
                return McpRuntimeBuildResult.Fail("cli_build_input_missing", "CLI build input is missing: " + cliProjectPath, sdkProbe.Stdout, sdkProbe.Stderr);
            }

            if (!File.Exists(roslynProjectPath))
            {
                return McpRuntimeBuildResult.Fail("roslyn_build_input_missing", "Roslyn compiler build input is missing: " + roslynProjectPath, sdkProbe.Stdout, sdkProbe.Stderr);
            }

            var runtimeRoot = _pathResolver.ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                return McpRuntimeBuildResult.Fail("workspace_runtime_root_missing", "Project-local runtime root could not be resolved.", sdkProbe.Stdout, sdkProbe.Stderr);
            }

            var projectRoot = _pathResolver.GetProjectRoot();
            var logPath = ResolveLogPath(projectRoot);
            var request = new ProcessExecutionRequest
            {
                FilePath = "pwsh",
                Arguments = new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    buildScriptPath,
                    "-OutputRoot",
                    runtimeRoot,
                    "-UnityProjectPath",
                    projectRoot,
                    "-Rid",
                    "win-x64",
                    "-DotnetPath",
                    dotnetPath,
                },
                WorkingDirectory = Path.GetDirectoryName(buildScriptPath) ?? toolsRoot,
                Timeout = _timeout,
                CancellationMode = ProcessCancellationMode.TerminateOnCancel,
            };

            var result = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            WriteBuildLog(logPath, request, result);
            if (result.Outcome != ProcessOutcome.Completed || result.ExitCode.GetValueOrDefault(-1) != 0)
            {
                return McpRuntimeBuildResult.Fail(
                    "runtime_build_failed",
                    "Local runtime build failed.",
                    result.Stdout,
                    result.Stderr,
                    result.ExitCode,
                    logPath);
            }

            var cliExecutablePath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe");
            if (!File.Exists(cliExecutablePath))
            {
                return McpRuntimeBuildResult.Fail("cli_executable_missing", "Generated CLI executable is missing: " + cliExecutablePath, result.Stdout, result.Stderr, result.ExitCode, logPath);
            }

            var roslynExecutablePath = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe");
            if (!File.Exists(roslynExecutablePath))
            {
                return McpRuntimeBuildResult.Fail("roslyn_executable_missing", "Generated Roslyn compiler executable is missing: " + roslynExecutablePath, result.Stdout, result.Stderr, result.ExitCode, logPath);
            }

            return new McpRuntimeBuildResult
            {
                Succeeded = true,
                Reason = string.Empty,
                Summary = "Local runtime built.",
                RuntimeRoot = runtimeRoot,
                CliExecutablePath = cliExecutablePath,
                RoslynExecutablePath = roslynExecutablePath,
                DotnetSdkVersion = sdkProbe.Net8SdkVersion,
                ExitCode = result.ExitCode,
                LogPath = logPath,
                Stdout = result.Stdout,
                Stderr = result.Stderr,
            };
        }

        internal async Task<DotnetSdkProbeResult> ProbeDotnetSdkAsync(CancellationToken cancellationToken)
        {
            return await ProbeDotnetSdkAsync("dotnet", cancellationToken).ConfigureAwait(false);
        }

        internal async Task<DotnetSdkProbeResult> ProbeDotnetSdkAsync(string dotnetPath, CancellationToken cancellationToken)
        {
            var request = new ProcessExecutionRequest
            {
                FilePath = string.IsNullOrWhiteSpace(dotnetPath) ? "dotnet" : dotnetPath,
                Arguments = new[] { "--list-sdks" },
                Timeout = TimeSpan.FromSeconds(15),
                CancellationMode = ProcessCancellationMode.TerminateOnCancel,
            };

            try
            {
                var result = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Outcome != ProcessOutcome.Completed || result.ExitCode.GetValueOrDefault(-1) != 0)
                {
                    return DotnetSdkProbeResult.Missing(result.Stdout, result.Stderr);
                }

                return DotnetSdkProbeResult.Parse(result.Stdout, result.Stderr);
            }
            catch (Exception exception)
            {
                return DotnetSdkProbeResult.Missing(string.Empty, exception.Message);
            }
        }

        private string ResolveDotnetPath(McpEditorSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.DotnetPath))
            {
                return settings.DotnetPath.Trim();
            }

            return "dotnet";
        }

        private string ResolveToolsRoot(McpEditorSettings settings)
        {
            return _pathResolver.ResolveToolsRoot(settings);
        }

        private static string ResolveLogPath(string projectRoot)
        {
            var root = string.IsNullOrWhiteSpace(projectRoot)
                ? Environment.CurrentDirectory
                : projectRoot;
            var logsRoot = Path.Combine(root, "Library", "AgentBridge", "logs");
            Directory.CreateDirectory(logsRoot);
            return Path.Combine(logsRoot, "mcp-runtime-build.log");
        }

        private static void WriteBuildLog(string logPath, ProcessExecutionRequest request, ProcessExecutionResult result)
        {
            var lines = new List<string>
            {
                "[" + DateTime.UtcNow.ToString("o") + "] Build Local Runtime",
                "command=" + request.FilePath + " " + string.Join(" ", request.Arguments ?? new string[0]),
                "outcome=" + result.Outcome,
                "exitCode=" + (result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : string.Empty),
                "stdout=" + Summarize(result.Stdout),
                "stderr=" + Summarize(result.Stderr),
                string.Empty,
            };
            File.AppendAllLines(logPath, lines);
        }

        private static string Summarize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 2000 ? normalized : normalized.Substring(0, 2000);
        }
    }

    public sealed class McpRuntimeBuildResult
    {
        public bool Succeeded { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string RuntimeRoot { get; set; } = string.Empty;
        public string CliExecutablePath { get; set; } = string.Empty;
        public string RoslynExecutablePath { get; set; } = string.Empty;
        public string DotnetSdkVersion { get; set; } = string.Empty;
        public int? ExitCode { get; set; }
        public string LogPath { get; set; } = string.Empty;
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;

        public static McpRuntimeBuildResult Fail(
            string reason,
            string summary,
            string stdout,
            string stderr,
            int? exitCode = null,
            string logPath = "")
        {
            return new McpRuntimeBuildResult
            {
                Succeeded = false,
                Reason = reason ?? string.Empty,
                Summary = summary ?? string.Empty,
                ExitCode = exitCode,
                LogPath = logPath ?? string.Empty,
                Stdout = stdout ?? string.Empty,
                Stderr = stderr ?? string.Empty,
            };
        }
    }

    public sealed class DotnetSdkProbeResult
    {
        public bool HasNet8Sdk { get; private set; }
        public string Net8SdkVersion { get; private set; } = string.Empty;
        public string Stdout { get; private set; } = string.Empty;
        public string Stderr { get; private set; } = string.Empty;

        public static DotnetSdkProbeResult Parse(string stdout, string stderr)
        {
            var result = new DotnetSdkProbeResult
            {
                Stdout = stdout ?? string.Empty,
                Stderr = stderr ?? string.Empty,
            };

            foreach (var line in (stdout ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var version = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                if (version.StartsWith("8.", StringComparison.Ordinal))
                {
                    result.HasNet8Sdk = true;
                    result.Net8SdkVersion = version;
                    break;
                }
            }

            return result;
        }

        public static DotnetSdkProbeResult Missing(string stdout, string stderr)
        {
            return new DotnetSdkProbeResult
            {
                HasNet8Sdk = false,
                Stdout = stdout ?? string.Empty,
                Stderr = stderr ?? string.Empty,
            };
        }
    }
}
