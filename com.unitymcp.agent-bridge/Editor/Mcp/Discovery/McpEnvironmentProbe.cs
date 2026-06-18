using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpEnvironmentProbe
    {
        private readonly McpPathResolver _pathResolver;
        private readonly ToolVersionParser _versionParser;
        private readonly IAsyncProcessRunner _processRunner;

        public McpEnvironmentProbe()
            : this(new McpPathResolver(), new ToolVersionParser(), null)
        {
        }

        public McpEnvironmentProbe(
            McpPathResolver pathResolver,
            ToolVersionParser versionParser,
            IAsyncProcessRunner processRunner)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _versionParser = versionParser ?? throw new ArgumentNullException(nameof(versionParser));
            _processRunner = processRunner;
        }

        public Task<McpEnvironmentSnapshot> SnapshotAsync(
            McpEditorSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return SnapshotInternalAsync(settings, cancellationToken);
        }

        private async Task<McpEnvironmentSnapshot> SnapshotInternalAsync(
            McpEditorSettings settings,
            CancellationToken cancellationToken)
        {
            var snapshot = new McpEnvironmentSnapshot
            {
                Dotnet = await ProbeAsync(settings.DotnetPath, "dotnet", cancellationToken),
            };

            return snapshot;
        }

        private async Task<ToolProbeResult> ProbeAsync(
            string configuredPath,
            string executableName,
            CancellationToken cancellationToken)
        {
            var resolvedPath = _pathResolver.ResolveExecutablePath(configuredPath, executableName);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return new ToolProbeResult();
            }

            if (_processRunner == null)
            {
                return new ToolProbeResult
                {
                    ResolvedPath = resolvedPath,
                    VersionText = string.Empty,
                    IsAvailable = false,
                };
            }

            var request = new ProcessExecutionRequest
            {
                FilePath = resolvedPath,
                Arguments = new[] { "--version" },
                Timeout = TimeSpan.FromSeconds(5),
                CancellationMode = ProcessCancellationMode.TerminateOnCancel,
            };

            try
            {
                var result = await _processRunner.RunAsync(request, cancellationToken);
                var stdout = (result?.Stdout ?? string.Empty).Trim();
                var stderr = (result?.Stderr ?? string.Empty).Trim();
                var versionText = string.IsNullOrEmpty(stdout) ? stderr : stdout;
                var parsedVersion = _versionParser.Parse(versionText);

                return new ToolProbeResult
                {
                    ResolvedPath = resolvedPath,
                    VersionText = versionText,
                    IsAvailable = result != null && result.Outcome == ProcessOutcome.Completed && parsedVersion != null,
                };
            }
            catch (Exception exception)
            {
                return new ToolProbeResult
                {
                    ResolvedPath = resolvedPath,
                    VersionText = exception.Message,
                    IsAvailable = false,
                };
            }
        }
    }

    public sealed class McpEnvironmentSnapshot
    {
        public ToolProbeResult Dotnet { get; set; } = new ToolProbeResult();
    }

    public sealed class ToolProbeResult
    {
        public string ResolvedPath { get; set; } = string.Empty;
        public string VersionText { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
    }
}
