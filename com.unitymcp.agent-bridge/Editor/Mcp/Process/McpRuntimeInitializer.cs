using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpRuntimeInitializer
    {
        private readonly IAsyncProcessRunner _processRunner;
        private readonly McpPathResolver _pathResolver;
        private readonly McpServerProcessProbe _serverProcessProbe;

        public McpRuntimeInitializer()
            : this(new AsyncProcessRunner(), new McpPathResolver(), new McpServerProcessProbe())
        {
        }

        internal McpRuntimeInitializer(IAsyncProcessRunner processRunner, McpPathResolver pathResolver)
            : this(processRunner, pathResolver, new McpServerProcessProbe())
        {
        }

        internal McpRuntimeInitializer(IAsyncProcessRunner processRunner, McpPathResolver pathResolver, McpServerProcessProbe serverProcessProbe)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _serverProcessProbe = serverProcessProbe ?? throw new ArgumentNullException(nameof(serverProcessProbe));
        }

        public Task<ManagedBlockApplyResult> InitializeRuntimeAsync(
            McpEditorSettings settings,
            CancellationToken cancellationToken)
        {
            var payloadToolsRoot = ResolvePayloadToolsRoot(settings);
            if (string.IsNullOrWhiteSpace(payloadToolsRoot) || !Directory.Exists(payloadToolsRoot))
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    Reason = "payload_root_missing",
                    TargetPath = string.Empty,
                });
            }

            var runtimeRoot = _pathResolver.ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    Reason = "workspace_runtime_root_missing",
                    TargetPath = string.Empty,
                });
            }

            var activeServerSnapshot = _serverProcessProbe.Inspect(settings);
            if (activeServerSnapshot.SafeStopTargets.Any())
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    Reason = "active_mcp_server_running",
                    TargetPath = runtimeRoot,
                });
            }

            MaterializeRuntimePayload(payloadToolsRoot, runtimeRoot);

            var mcpRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            if (!Directory.Exists(mcpRoot))
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    Reason = "mcp_root_missing",
                    TargetPath = runtimeRoot,
                });
            }

            var cliRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli");
            if (!Directory.Exists(cliRoot))
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = runtimeRoot,
                    Reason = "cli_root_missing",
                });
            }

            var cliExecutablePath = ResolvePreparedCliExecutablePath(runtimeRoot);
            if (string.IsNullOrWhiteSpace(cliExecutablePath))
            {
                return Task.FromResult(new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = cliRoot,
                    Reason = "cli_executable_missing",
                });
            }

            return Task.FromResult(new ManagedBlockApplyResult
            {
                Applied = true,
                TargetPath = runtimeRoot,
                Reason = string.Empty,
            });
        }

        internal static string ResolvePreparedCliExecutablePath(string runtimeRoot)
        {
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                return string.Empty;
            }

            var executableName = GetProductExecutableName();
            var cliRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli");
            var candidates = new[]
            {
                Path.Combine(cliRoot, "out", GetCurrentRid(), executableName),
                Path.Combine(cliRoot, executableName),
            };

            for (var index = 0; index < candidates.Length; index++)
            {
                if (File.Exists(candidates[index]))
                {
                    return candidates[index];
                }
            }

            return string.Empty;
        }

        private string ResolvePayloadToolsRoot(McpEditorSettings settings)
        {
            return _pathResolver.ResolveToolsRoot(settings);
        }

        internal static void MaterializeRuntimePayload(string payloadToolsRoot, string runtimeRoot)
        {
            var canResetRuntimeRoot = true;
            string stagedRuntimeRoot = null;
            if (Directory.Exists(runtimeRoot))
            {
                try
                {
                    stagedRuntimeRoot = StageGeneratedRuntimeOutputs(runtimeRoot);
                    Directory.Delete(runtimeRoot, true);
                }
                catch (UnauthorizedAccessException)
                {
                    canResetRuntimeRoot = false;
                }
                catch (IOException)
                {
                    canResetRuntimeRoot = false;
                }
            }

            if (canResetRuntimeRoot || !Directory.Exists(runtimeRoot))
            {
                Directory.CreateDirectory(runtimeRoot);
            }

            CopyDirectory(payloadToolsRoot, runtimeRoot, canResetRuntimeRoot);
            RestoreGeneratedRuntimeOutputs(stagedRuntimeRoot, runtimeRoot);
        }

        internal static string GetCurrentRid()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    return "win-x64";
                case PlatformID.MacOSX:
                    return "osx-arm64";
                case PlatformID.Unix:
                    return "linux-x64";
                default:
                    return "win-x64";
            }
        }

        internal static string GetProductExecutableName()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "unity-agent-bridge.exe"
                : "unity-agent-bridge";
        }

        private static void CopyDirectory(string sourceRoot, string targetRoot, bool canOverwriteLockedFiles)
        {
            foreach (var directoryPath in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (IsExcludedRuntimePayloadPath(directoryPath))
                {
                    continue;
                }

                var relativePath = directoryPath.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
            }

            foreach (var filePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (IsExcludedRuntimePayloadPath(filePath))
                {
                    continue;
                }

                var relativePath = filePath.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetPath = Path.Combine(targetRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                try
                {
                    File.Copy(filePath, targetPath, true);
                }
                catch (UnauthorizedAccessException) when (!canOverwriteLockedFiles && IsRuntimeExecutablePath(targetPath))
                {
                }
                catch (IOException) when (!canOverwriteLockedFiles && IsRuntimeExecutablePath(targetPath))
                {
                }
            }
        }

        private static bool IsRuntimeExecutablePath(string path)
        {
            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, "unity-agent-bridge.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "unity-agent-bridge", StringComparison.OrdinalIgnoreCase);
        }

        private static string StageGeneratedRuntimeOutputs(string runtimeRoot)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge.RuntimeStage", Guid.NewGuid().ToString("N"));
            var stagedAny = false;
            foreach (var relativePath in GetGeneratedRuntimeRelativePaths())
            {
                var sourcePath = Path.Combine(runtimeRoot, relativePath);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var targetPath = Path.Combine(tempRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourcePath, targetPath, true);
                stagedAny = true;
            }

            if (stagedAny)
            {
                return tempRoot;
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            return null;
        }

        private static void RestoreGeneratedRuntimeOutputs(string stagedRuntimeRoot, string runtimeRoot)
        {
            if (string.IsNullOrWhiteSpace(stagedRuntimeRoot) || !Directory.Exists(stagedRuntimeRoot))
            {
                return;
            }

            try
            {
                foreach (var filePath in Directory.GetFiles(stagedRuntimeRoot, "*", SearchOption.AllDirectories))
                {
                    var relativePath = filePath.Substring(stagedRuntimeRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var targetPath = Path.Combine(runtimeRoot, relativePath);
                    var targetDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    try
                    {
                        File.Copy(filePath, targetPath, true);
                    }
                    catch (UnauthorizedAccessException) when (IsGeneratedRuntimePath(targetPath))
                    {
                    }
                    catch (IOException) when (IsGeneratedRuntimePath(targetPath))
                    {
                    }
                }
            }
            finally
            {
                Directory.Delete(stagedRuntimeRoot, true);
            }
        }

        private static string[] GetGeneratedRuntimeRelativePaths()
        {
            return new[]
            {
                Path.Combine("UnityAgentBridge", "cli", "out", GetCurrentRid(), GetProductExecutableName()),
                Path.Combine("UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe"),
            };
        }

        private static bool IsGeneratedRuntimePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, GetProductExecutableName(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "unity-roslyn-compiler.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcludedRuntimePayloadPath(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in parts)
            {
                if (string.Equals(part, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "tools", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "UnityAgentBridge.Cli.Tests", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, "server.ts", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "probe.ts", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "package-lock.json", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class McpServerProcessProbe
    {
        private readonly IMcpServerProcessProvider _processProvider;
        private readonly Func<McpEditorSettings, string> _projectRootProvider;
        private readonly Func<McpEditorSettings, string> _runtimeRootProvider;

        public McpServerProcessProbe()
            : this(new SystemMcpServerProcessProvider(), new McpPathResolver())
        {
        }

        internal McpServerProcessProbe(IMcpServerProcessProvider processProvider, McpPathResolver pathResolver)
            : this(
                processProvider,
                pathResolver,
                settings => pathResolver.GetProjectRoot(),
                settings => pathResolver.ResolveWorkspaceRuntimeRoot(settings))
        {
        }

        internal McpServerProcessProbe(
            IMcpServerProcessProvider processProvider,
            McpPathResolver pathResolver,
            Func<McpEditorSettings, string> projectRootProvider,
            Func<McpEditorSettings, string> runtimeRootProvider)
        {
            _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
            _projectRootProvider = projectRootProvider ?? throw new ArgumentNullException(nameof(projectRootProvider));
            _runtimeRootProvider = runtimeRootProvider ?? throw new ArgumentNullException(nameof(runtimeRootProvider));
        }

        public McpServerProcessSnapshot Inspect(McpEditorSettings settings)
        {
            var projectRoot = NormalizePath(_projectRootProvider(settings));
            var runtimeRoot = NormalizePath(_runtimeRootProvider(settings));
            var entries = new List<McpServerProcessInfo>();
            var inspectionFailure = string.Empty;

            IReadOnlyList<McpProcessDescriptor> processes;
            try
            {
                processes = _processProvider.GetProcesses() ?? Array.Empty<McpProcessDescriptor>();
            }
            catch (Exception exception)
            {
                return McpServerProcessSnapshot.Unavailable(exception.Message);
            }

            foreach (var process in processes)
            {
                var info = Classify(process, projectRoot, runtimeRoot);
                if (info == null)
                {
                    continue;
                }

                entries.Add(info);
                if (!string.IsNullOrWhiteSpace(info.InspectionError))
                {
                    inspectionFailure = string.IsNullOrWhiteSpace(inspectionFailure)
                        ? info.InspectionError
                        : inspectionFailure + "; " + info.InspectionError;
                }
            }

            return McpServerProcessSnapshot.FromEntries(entries, inspectionFailure);
        }

        public McpServerStopResult StopCurrentProjectServers(McpEditorSettings settings)
        {
            var snapshot = Inspect(settings);
            var targets = snapshot.SafeStopTargets.ToArray();
            if (targets.Length == 0)
            {
                return new McpServerStopResult
                {
                    Attempted = false,
                    Summary = "No current-project MCP server process was found.",
                    SnapshotBeforeStop = snapshot,
                    Results = Array.Empty<McpServerStopProcessResult>(),
                };
            }

            var results = new List<McpServerStopProcessResult>();
            foreach (var target in targets)
            {
                try
                {
                    var stopped = _processProvider.TryTerminate(target.ProcessId, out var error);
                    results.Add(new McpServerStopProcessResult
                    {
                        ProcessId = target.ProcessId,
                        ProcessName = target.ProcessName,
                        Succeeded = stopped,
                        Message = stopped
                            ? "Stopped"
                            : (string.IsNullOrWhiteSpace(error) ? "Could not stop process." : error),
                    });
                }
                catch (Exception exception)
                {
                    results.Add(new McpServerStopProcessResult
                    {
                        ProcessId = target.ProcessId,
                        ProcessName = target.ProcessName,
                        Succeeded = false,
                        Message = exception.Message,
                    });
                }
            }

            var failed = results.Count(item => !item.Succeeded);
            return new McpServerStopResult
            {
                Attempted = true,
                Summary = failed == 0
                    ? "Stopped " + results.Count + " MCP server process" + (results.Count == 1 ? "." : "es.")
                    : "Stopped " + (results.Count - failed) + " of " + results.Count + " MCP server processes.",
                SnapshotBeforeStop = snapshot,
                Results = results,
            };
        }

        internal static McpServerProcessInfo Classify(McpProcessDescriptor process, string projectRoot, string runtimeRoot)
        {
            var executablePath = NormalizePath(process.ExecutablePath);
            var commandLine = process.CommandLine ?? string.Empty;
            var normalizedCommandLine = NormalizeTextPath(commandLine);
            var processName = process.ProcessName ?? string.Empty;
            if (!LooksLikeUnityAgentBridge(processName, executablePath, normalizedCommandLine))
            {
                return null;
            }

            var projectEvidence = ContainsPath(normalizedCommandLine, projectRoot);
            var runtimeEvidence = IsUnderPath(executablePath, runtimeRoot) || ContainsPath(normalizedCommandLine, runtimeRoot);
            var hasContradictoryProject = HasContradictoryProjectBinding(normalizedCommandLine, projectRoot);

            McpServerProcessMatchKind matchKind;
            string matchReason;
            if (projectEvidence && !hasContradictoryProject)
            {
                matchKind = McpServerProcessMatchKind.CurrentProject;
                matchReason = "command line references the current Unity project";
            }
            else if (runtimeEvidence && !hasContradictoryProject)
            {
                matchKind = McpServerProcessMatchKind.PreparedRuntime;
                matchReason = "process executable or command line references the prepared runtime";
            }
            else if (hasContradictoryProject)
            {
                matchKind = McpServerProcessMatchKind.MismatchedProject;
                matchReason = "command line references another Unity project";
            }
            else
            {
                matchKind = McpServerProcessMatchKind.Candidate;
                matchReason = "process looks like Unity Agent Bridge but has no current-project evidence";
            }

            return new McpServerProcessInfo
            {
                ProcessId = process.ProcessId,
                ProcessName = processName,
                ExecutablePath = process.ExecutablePath ?? string.Empty,
                CommandLineSummary = Summarize(commandLine, 260),
                MatchKind = matchKind,
                MatchReason = matchReason,
                InspectionError = process.InspectionError ?? string.Empty,
            };
        }

        private static bool LooksLikeUnityAgentBridge(string processName, string executablePath, string normalizedCommandLine)
        {
            var fileName = Path.GetFileName(executablePath);
            if (string.Equals(fileName, "unity-agent-bridge.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "unity-agent-bridge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "unity-agent-bridge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "unity-agent-bridge.exe", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedCommandLine.IndexOf("mcp-server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       normalizedCommandLine.IndexOf("unity-agent-bridge", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return normalizedCommandLine.IndexOf("unity-agent-bridge", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   normalizedCommandLine.IndexOf("mcp-server", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasContradictoryProjectBinding(string normalizedCommandLine, string projectRoot)
        {
            if (normalizedCommandLine.IndexOf("UNITY_PROJECT_PATH=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return !ContainsPath(normalizedCommandLine, projectRoot);
        }

        private static bool IsUnderPath(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var candidate = NormalizePath(candidatePath);
            var root = EnsureTrailingSeparator(NormalizePath(rootPath));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsPath(string text, string path)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return NormalizeTextPath(text).IndexOf(NormalizePath(path), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeTextPath(string text)
        {
            return (text ?? string.Empty).Replace('\\', '/').Trim();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
            }
            catch
            {
                return path.Replace('\\', '/').TrimEnd('/');
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return string.IsNullOrEmpty(path) || path.EndsWith("/", StringComparison.Ordinal)
                ? path
                : path + "/";
        }

        private static string Summarize(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength - 3) + "...";
        }
    }

    internal interface IMcpServerProcessProvider
    {
        IReadOnlyList<McpProcessDescriptor> GetProcesses();

        bool TryTerminate(int processId, out string error);
    }

    internal sealed class SystemMcpServerProcessProvider : IMcpServerProcessProvider
    {
        public IReadOnlyList<McpProcessDescriptor> GetProcesses()
        {
            var descriptors = new List<McpProcessDescriptor>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var processName = SafeGetName(process);
                    if (!LooksLikeUnityAgentBridgeProcessName(processName))
                    {
                        continue;
                    }

                    var descriptor = new McpProcessDescriptor
                    {
                        ProcessId = SafeGetId(process),
                        ProcessName = processName,
                    };

                    try
                    {
                        descriptor.ExecutablePath = process.MainModule != null ? process.MainModule.FileName : string.Empty;
                    }
                    catch (Exception exception)
                    {
                        descriptor.InspectionError = exception.Message;
                    }

                    descriptors.Add(descriptor);
                }
            }

            return descriptors;
        }

        private static bool LooksLikeUnityAgentBridgeProcessName(string processName)
        {
            return string.Equals(processName, "unity-agent-bridge", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processName, "unity-agent-bridge.exe", StringComparison.OrdinalIgnoreCase);
        }

        public bool TryTerminate(int processId, out string error)
        {
            error = string.Empty;
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    process.Kill();
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static int SafeGetId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeGetName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal sealed class McpProcessDescriptor
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public string InspectionError { get; set; } = string.Empty;
    }

    public sealed class McpServerProcessSnapshot
    {
        public McpServerProcessState State { get; private set; }
        public string Summary { get; private set; } = string.Empty;
        public string Detail { get; private set; } = string.Empty;
        public string InspectionFailure { get; private set; } = string.Empty;
        public IReadOnlyList<McpServerProcessInfo> Processes { get; private set; } = Array.Empty<McpServerProcessInfo>();

        public IEnumerable<McpServerProcessInfo> SafeStopTargets
        {
            get
            {
                return Processes.Where(process =>
                    process.MatchKind == McpServerProcessMatchKind.CurrentProject ||
                    process.MatchKind == McpServerProcessMatchKind.PreparedRuntime);
            }
        }

        public static McpServerProcessSnapshot Unavailable(string reason)
        {
            return new McpServerProcessSnapshot
            {
                State = McpServerProcessState.Unavailable,
                Summary = "Unknown (?/?)",
                Detail = "Long-running MCP server process state could not be determined.",
                InspectionFailure = reason ?? string.Empty,
            };
        }

        internal static McpServerProcessSnapshot FromEntries(IReadOnlyList<McpServerProcessInfo> entries, string inspectionFailure)
        {
            var snapshot = new McpServerProcessSnapshot
            {
                Processes = entries ?? Array.Empty<McpServerProcessInfo>(),
                InspectionFailure = inspectionFailure ?? string.Empty,
            };

            var currentCount = snapshot.SafeStopTargets.Count();
            var totalCount = snapshot.Processes.Count;
            var mismatchedCount = snapshot.Processes.Count(process => process.MatchKind == McpServerProcessMatchKind.MismatchedProject);
            var candidateCount = snapshot.Processes.Count(process => process.MatchKind == McpServerProcessMatchKind.Candidate);
            var ratio = "(" + currentCount + "/" + totalCount + ")";

            if (snapshot.Processes.Count == 0)
            {
                snapshot.State = McpServerProcessState.Stopped;
                snapshot.Summary = "Idle " + ratio;
                snapshot.Detail = "No long-running MCP server process was detected. MCP may still be available through on-demand CLI calls.";
            }
            else if (currentCount == 1)
            {
                snapshot.State = McpServerProcessState.RunningCurrentProject;
                snapshot.Summary = "Running " + ratio;
                snapshot.Detail = "One long-running MCP server process is tied to this project and can be stopped safely.";
            }
            else if (currentCount > 1)
            {
                snapshot.State = McpServerProcessState.MultipleCurrentProject;
                snapshot.Summary = "Duplicate " + ratio;
                snapshot.Detail = currentCount + " long-running MCP server processes are tied to this project.";
            }
            else if (mismatchedCount > 0)
            {
                snapshot.State = McpServerProcessState.MismatchedProject;
                snapshot.Summary = "Foreign " + ratio;
                snapshot.Detail = mismatchedCount + " long-running MCP server process" + (mismatchedCount == 1 ? " is" : "es are") + " tied to another Unity project.";
            }
            else if (candidateCount > 0)
            {
                snapshot.State = McpServerProcessState.Ambiguous;
                snapshot.Summary = "Ambiguous " + ratio;
                snapshot.Detail = candidateCount + " long-running MCP server candidate" + (candidateCount == 1 ? " is" : "s are") + " not tied to a known Unity project.";
            }
            else
            {
                snapshot.State = McpServerProcessState.Stopped;
                snapshot.Summary = "Idle " + ratio;
                snapshot.Detail = "No current-project long-running MCP server process was detected.";
            }

            return snapshot;
        }
    }

    public enum McpServerProcessState
    {
        Stopped,
        RunningCurrentProject,
        MultipleCurrentProject,
        MismatchedProject,
        Ambiguous,
        Unavailable,
    }

    public enum McpServerProcessMatchKind
    {
        CurrentProject,
        PreparedRuntime,
        MismatchedProject,
        Candidate,
    }

    public sealed class McpServerProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string CommandLineSummary { get; set; } = string.Empty;
        public McpServerProcessMatchKind MatchKind { get; set; }
        public string MatchReason { get; set; } = string.Empty;
        public string InspectionError { get; set; } = string.Empty;
    }

    public sealed class McpServerStopResult
    {
        public bool Attempted { get; set; }
        public string Summary { get; set; } = string.Empty;
        public McpServerProcessSnapshot SnapshotBeforeStop { get; set; }
        public IReadOnlyList<McpServerStopProcessResult> Results { get; set; } = Array.Empty<McpServerStopProcessResult>();
    }

    public sealed class McpServerStopProcessResult
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
