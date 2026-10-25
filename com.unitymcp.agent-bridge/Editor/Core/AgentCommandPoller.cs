using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentCommandPoller
    {
        private readonly AgentCommandQueue _queue;
        private readonly UnityToolFacade _facade;
        private readonly AgentBridgeSettings _settings;
        private readonly FileAgentBridgeLogger _logger;
        private readonly AgentBridgePaths _paths;
        private readonly string _capabilityRevision;
        private readonly bool _useSharedScheduler;
        private readonly Func<string, string> _projectIdProvider;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private DateTime _nextTickUtc;
        private bool _isStarted;
        private string _lastError = string.Empty;

        public AgentCommandPoller(AgentCommandQueue queue, UnityToolFacade facade, AgentBridgeSettings settings, AgentBridgePaths paths, FileAgentBridgeLogger logger = null, string capabilityRevision = null)
            : this(queue, facade, settings, paths, true, logger, capabilityRevision)
        {
        }

        internal AgentCommandPoller(AgentCommandQueue queue, UnityToolFacade facade, AgentBridgeSettings settings, AgentBridgePaths paths, bool useSharedScheduler, FileAgentBridgeLogger logger = null, string capabilityRevision = null, Func<string, string> projectIdProvider = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _facade = facade ?? throw new ArgumentNullException(nameof(facade));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _logger = logger;
            _capabilityRevision = capabilityRevision ?? "missing";
            _useSharedScheduler = useSharedScheduler;
            if (useSharedScheduler && projectIdProvider != null) throw new ArgumentException("Custom identities require an isolated poller.", nameof(projectIdProvider));
            _projectIdProvider = projectIdProvider ?? (path => ProjectPathIdentity.GetProjectId(path));
            if (_useSharedScheduler) EditorTaskScheduling.Configure(_facade, settings);
        }

        public bool IsStarted => _isStarted;

        public void Start()
        {
            if (_isStarted || !_settings.enabled)
            {
                return;
            }

            _nextTickUtc = DateTime.UtcNow;
            EditorApplication.update += OnEditorUpdate;
            _isStarted = true;
        }

        public void Stop()
        {
            if (!_isStarted)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _isStarted = false;
            PublishStatus(string.Empty, "unity.poller.stopped");
        }

        private void OnEditorUpdate()
        {
            try
            {
                if (!_settings.enabled)
                {
                    Stop();
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                if (nowUtc < _nextTickUtc)
                {
                    return;
                }

                _nextTickUtc = nowUtc.AddMilliseconds(EditorApplication.isCompiling ? _settings.compileBackoffMs : _settings.pollIntervalMs);
                PublishStatus(string.Empty, "unity.poller.idle");

                // Intake and lifecycle controls continue while a separate scheduler owns execution.
                for (var index = 0; index < (_useSharedScheduler ? Math.Max(32, _settings.maxConcurrent) : _settings.maxConcurrent); index++)
                {
                    if (!_queue.TryDequeue(out var queuedCommand))
                    {
                        break;
                    }

                    PublishStatus(queuedCommand.Command.commandId, "unity.poller.pickup");
                    _logger?.Stage("unity.poller.pickup", queuedCommand.Command.commandId, queuedCommand.Command.tool, "running", "command dequeued");
                    ToolResult result = null;
                    try
                    {
                        PublishStatus(queuedCommand.Command.commandId, "unity.tool.start");
                        _logger?.Stage("unity.tool.start", queuedCommand.Command.commandId, queuedCommand.Command.tool, "running", "tool execution started");
                        result = _useSharedScheduler ? _facade.Execute(queuedCommand.Command, NoOpAgentCancellation.Instance)
                            : _facade.ExecuteImmediate(queuedCommand.Command, NoOpAgentCancellation.Instance);
                        _queue.UpdateState(queuedCommand.Command.commandId, state => state.status = result?.status ?? ToolResultStatus.Exception);
                        _logger?.Stage("unity.tool.finish", queuedCommand.Command.commandId, queuedCommand.Command.tool, result?.status ?? ToolResultStatus.Exception, result?.summary ?? string.Empty);

                        if (ShouldLeaveInProcessing(result))
                        {
                            PublishStatus(queuedCommand.Command.commandId, "unity.tool.finish");
                            continue;
                        }
                    }
                    catch (Exception exception)
                    {
                        result = new ToolResult
                        {
                            success = false,
                            status = ToolResultStatus.Exception,
                            summary = exception.Message,
                            errors = new System.Collections.Generic.List<ToolError>
                            {
                                new ToolError
                                {
                                    code = "AGENTBRIDGE_POLLER_EXCEPTION",
                                    message = exception.ToString()
                                }
                            }
                        };
                        UpdateLastError(exception.Message);
                        _logger?.Exception("unity.tool.finish", exception);
                    }

                    _queue.Complete(queuedCommand.Command.commandId, result);
                    if (ShouldPersistAsLastError(result))
                    {
                        UpdateLastError(result?.summary ?? string.Empty);
                    }

                    PublishStatus(queuedCommand.Command.commandId, "unity.write_result");
                    _logger?.Stage("unity.write_result", queuedCommand.Command.commandId, queuedCommand.Command.tool, result?.status ?? string.Empty, "terminal result written");
                }
            }
            catch (Exception exception)
            {
                UpdateLastError(exception.Message);
                _logger?.Exception("unity.poller.update_failed", exception);
            }
        }

        private static bool ShouldLeaveInProcessing(ToolResult result)
        {
            if (result == null)
            {
                return false;
            }

            return string.Equals(result.status, ToolResultStatus.Pending, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Running, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Resuming, StringComparison.Ordinal);
        }

        private static bool ShouldPersistAsLastError(ToolResult result)
        {
            if (result == null)
            {
                return false;
            }

            return string.Equals(result.status, ToolResultStatus.Failed, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Timeout, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Exception, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.InvalidArgs, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Blocked, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Unsupported, StringComparison.Ordinal) ||
                   string.Equals(result.status, ToolResultStatus.Cancelled, StringComparison.Ordinal);
        }

        private void UpdateLastError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastError = message;
            }
        }

        private EditorSessionInfo CreateEditorSession()
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                var sessionId = process.Id + ":" + process.StartTime.ToUniversalTime().Ticks;
                var revisionKey = "UnityAgentBridge.EditorSessionRevision." + sessionId;
                long.TryParse(SessionState.GetString(revisionKey, "0"), out var lastRevision);
                var revision = Math.Max(DateTime.UtcNow.Ticks, lastRevision + 1);
                SessionState.SetString(revisionKey, revision.ToString(CultureInfo.InvariantCulture));
                return new EditorSessionInfo
                {
                    projectId = _projectIdProvider(_paths.ProjectRoot),
                    editorSessionId = sessionId,
                    processId = process.Id,
                    processStartTime = process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    heartbeatUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    revision = revision,
                    bridgeEnabled = _settings.enabled && _isStarted,
                    bridgeVersion = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AgentCommandPoller).Assembly)?.version ?? typeof(AgentCommandPoller).Assembly.GetName().Version.ToString(),
                    capabilityRevision = _capabilityRevision
                };
            }
        }

        private void PublishStatus(string currentCommandId, string currentStage)
        {

            var snapshot = new UnityBridgeStatusSnapshot
            {
                editorSession = CreateEditorSession(),
                timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                heartbeatUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                currentCommandId = currentCommandId ?? string.Empty,
                currentStage = currentStage ?? string.Empty,
                schedulingJson = _useSharedScheduler ? EditorTaskScheduling.Observation() : null,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlayingOrWillChangePlaymode,
                lastError = _lastError ?? string.Empty
            };

            var lifecycle = new CompileLifecycleStore(_paths.ProjectRoot, _settings.tempRoot).Read();
            if (lifecycle != null)
            {
                snapshot.projectPath = lifecycle.projectPath ?? _paths.ProjectRoot.Replace('\\', '/');
                snapshot.currentCompileEpoch = lifecycle.compileEpoch;
                snapshot.activeTargetEpochs = lifecycle.activeTargetEpochs?.ToArray() ?? Array.Empty<int>();
                snapshot.activeCompileCommandIds = lifecycle.activeCommandIds?.ToArray() ?? Array.Empty<string>();
                snapshot.compileLifecycleStage = lifecycle.currentStage ?? string.Empty;
                snapshot.compileLastTransition = lifecycle.lastTransition ?? string.Empty;
                snapshot.compileLastTransitionAtUtc = lifecycle.lastTransitionAtUtc ?? string.Empty;
                snapshot.compileTimeoutReason = lifecycle.timeoutReason ?? string.Empty;
            }
            else
            {
                snapshot.projectPath = _paths.ProjectRoot.Replace('\\', '/');
                snapshot.activeTargetEpochs = Array.Empty<int>();
                snapshot.activeCompileCommandIds = Array.Empty<string>();
                snapshot.compileLifecycleStage = string.Empty;
                snapshot.compileLastTransition = string.Empty;
                snapshot.compileLastTransitionAtUtc = string.Empty;
                snapshot.compileTimeoutReason = string.Empty;
            }

            var staleDiagnostics = StaleBridgeStateDiagnosticsCollector.CaptureSnapshot(_paths.ProjectRoot, _settings.tempRoot);
            snapshot.stalePrimaryClassification = staleDiagnostics.primaryClassification ?? string.Empty;
            snapshot.staleEvidencePriorityPath = staleDiagnostics.evidencePriorityPath ?? string.Empty;
            snapshot.staleHeartbeatAgeMs = staleDiagnostics.heartbeatAgeMs;
            snapshot.staleConfiguredProjectPath = staleDiagnostics.configuredProjectPath ?? string.Empty;
            snapshot.staleDetectedProjectPath = staleDiagnostics.detectedProjectPath ?? string.Empty;
            snapshot.staleProjectBindingKind = staleDiagnostics.projectBindingKind ?? string.Empty;
            snapshot.staleRuntimeIdentity = staleDiagnostics.runtimeIdentity ?? string.Empty;

            WriteAtomic(_paths.StatusFilePath, UnityEngine.JsonUtility.ToJson(snapshot, false));
        }

        private static void WriteAtomic(string targetPath, string content)
        {
            var tempPath = targetPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(content);
            }

            try
            {
                var replaced = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? MoveFileEx(tempPath, targetPath, 1 | 8)
                    : RenameFile(tempPath, targetPath) == 0;
                if (!replaced) throw new IOException("Atomic heartbeat rename failed with native error " + Marshal.GetLastWin32Error() + ".");
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                // A reader may temporarily hold a Windows handle without delete sharing.
                // Keep the prior valid heartbeat and retry on the next scheduled publish.
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string source, string destination, int flags);

        [DllImport("libc", EntryPoint = "rename", SetLastError = true)]
        private static extern int RenameFile(string source, string destination);
    }
}
