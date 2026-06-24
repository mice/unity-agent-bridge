using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpRuntimeInitializer
    {
        private readonly IAsyncProcessRunner _processRunner;
        private readonly McpPathResolver _pathResolver;

        public McpRuntimeInitializer()
            : this(new AsyncProcessRunner(), new McpPathResolver())
        {
        }

        internal McpRuntimeInitializer(IAsyncProcessRunner processRunner, McpPathResolver pathResolver)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
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
}
