using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    public static class AgentBridgeBootstrap
    {
        private static AgentCommandPoller _poller;
        private static FileAgentBridgeLogger _logger;
        private static string _projectRoot;
        private static bool _suppressStartForTests;
        private static bool _startupCheckRegistered;
        private static string _lastStartupState = string.Empty;

        static AgentBridgeBootstrap()
        {
            if (!AgentBridgeEditorStartupGuards.IsUnityTestRunnerContextActive())
            {
                ArmStartupCheck();
            }

            Debug.Log(typeof(AgentBridgeBootstrap).FullName);
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            // Re-arm bootstrap after script reload so the poller resumes on the new domain.
            if (_suppressStartForTests || AgentBridgeEditorStartupGuards.IsUnityTestRunnerContextActive())
            {
                return;
            }

            ArmStartupCheck();
        }

        public static void Reconfigure()
        {
            ArmStartupCheck();
            TryStop();
            TryStart();
            Debug.Log($"{nameof(AgentBridgeBootstrap)}.{nameof(Reconfigure)}(DONE)");
        }

        private static void ArmStartupCheck()
        {
            if (_startupCheckRegistered)
            {
                return;
            }

            EditorApplication.update += OnStartupEditorUpdate;
            _startupCheckRegistered = true;
        }

        private static void DisarmStartupCheck()
        {
            if (!_startupCheckRegistered)
            {
                return;
            }

            EditorApplication.update -= OnStartupEditorUpdate;
            _startupCheckRegistered = false;
        }

        private static void OnStartupEditorUpdate()
        {
            // Retry bridge startup on later editor ticks until the post-compile domain is stable.
            if (_suppressStartForTests || AgentBridgeEditorStartupGuards.IsUnityTestRunnerContextActive())
            {
                LogStartupState("suppressed");
                DisarmStartupCheck();
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                LogStartupState($"waiting:compiling={EditorApplication.isCompiling},updating={EditorApplication.isUpdating}");
                return;
            }

            LogStartupState("ready");
            TryStart();
        }

        private static void TryStart()
        {
            try
            {
                EnsureBootstrapLogger();
                _logger?.Info("bootstrap_start_attempt", "Agent Bridge startup attempt began.");

                if (_suppressStartForTests || AgentBridgeEditorStartupGuards.IsUnityTestRunnerContextActive())
                {
                    DisarmStartupCheck();
                    TryStop();
                    return;
                }

                _projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(_projectRoot))
                {
                    return;
                }

                var bootstrapSettings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
                var bootstrapPaths = new AgentBridgePaths(_projectRoot, bootstrapSettings);
                bootstrapPaths.EnsureDirectories();
                _logger = new FileAgentBridgeLogger(bootstrapPaths.BridgeLogPath);

                var loadResult = AgentBridgeSettingsLoader.Load();
                if (!string.IsNullOrWhiteSpace(loadResult.WarningCode))
                {
                    _logger.Warning(loadResult.WarningCode, loadResult.WarningMessage);
                }

                if (!AgentBridgeLocalPreferences.BridgeEnabled)
                {
                    DisarmStartupCheck();
                    _logger.Info("bootstrap_skipped", "Agent Bridge start skipped because local bridge preference is disabled.");
                    return;
                }

                if (!loadResult.ShouldStart)
                {
                    DisarmStartupCheck();
                    _logger.Info("bootstrap_skipped", $"Agent Bridge start skipped because settings load did not allow startup. warningCode={loadResult.WarningCode ?? string.Empty}");
                    return;
                }

                var settings = loadResult.Settings;
                var paths = new AgentBridgePaths(_projectRoot, settings);
                paths.EnsureDirectories();

                var queue = new AgentCommandQueue(_projectRoot, settings.tempRoot);
                var registry = new AgentToolRegistry(_logger);
                registry.Discover();
                UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, _logger);
                var facade = new UnityToolFacade(registry, settings, _logger);
                var selfTestRunner = new AgentBridgeSelfTestRunner(facade, queue, settings, _logger);
                registry.Register(new UnitySelfTestTool(selfTestRunner));
                _poller = new AgentCommandPoller(queue, facade, settings, paths, _logger);

                var recoveryRecords = queue.Recover();
                foreach (var record in recoveryRecords)
                {
                    if (record.Action != QueueRecoveryAction.Resuming || record.Command == null)
                    {
                        continue;
                    }

                    var result = facade.Execute(record.Command.Command, NoOpAgentCancellation.Instance);
                    if (ShouldLeaveInProcessing(result))
                    {
                        _logger?.Stage("unity.poller.pickup", record.Command.Command.commandId, record.Command.Command.tool, result.status, "recovered stale processing command continues");
                        continue;
                    }

                    queue.Complete(record.Command.Command.commandId, result);
                    _logger?.Stage("unity.write_result", record.Command.Command.commandId, record.Command.Command.tool, result.status, "recovered stale processing command completed");
                }

                _poller.Start();
                DisarmStartupCheck();
                _logger.Info("bootstrap_completed", "Agent Bridge started.");
            }
            catch (Exception exception)
            {
                try
                {
                    _logger?.Exception("bootstrap_failed", exception);
                }
                catch
                {
                }
            }
        }

        private static void LogStartupState(string state)
        {
            if (string.Equals(_lastStartupState, state, StringComparison.Ordinal))
            {
                return;
            }

            _lastStartupState = state ?? string.Empty;
            EnsureBootstrapLogger();
            _logger?.Info("bootstrap_startup_state", _lastStartupState);
        }

        private static void EnsureBootstrapLogger()
        {
            // Use a best-effort logger so startup diagnostics survive before settings load.
            if (_logger != null)
            {
                return;
            }

            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    return;
                }

                var bootstrapSettings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
                var bootstrapPaths = new AgentBridgePaths(projectRoot, bootstrapSettings);
                bootstrapPaths.EnsureDirectories();
                _logger = new FileAgentBridgeLogger(bootstrapPaths.BridgeLogPath);
            }
            catch
            {
            }
        }

        private static void TryStop()
        {
            try
            {
                _poller?.Stop();
            }
            catch (Exception exception)
            {
                _logger?.Exception("bootstrap_stop_failed", exception);
            }
            finally
            {
                _poller = null;
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

        internal static void SetSuppressStartForTests(bool suppressStart)
        {
            _suppressStartForTests = suppressStart;
            if (suppressStart)
            {
                DisarmStartupCheck();
                TryStop();
                return;
            }

            ArmStartupCheck();
            TryStart();
        }
    }

    public static class AgentBridgeEditorStartupGuards
    {
        public static bool IsUnityTestRunnerContextActive()
        {
            return HasCommandLineSwitch("-runTests") || Application.isBatchMode;
        }

        private static bool HasCommandLineSwitch(string switchName)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], switchName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
