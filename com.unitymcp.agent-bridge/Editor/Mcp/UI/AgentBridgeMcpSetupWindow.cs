using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMcp.AgentBridge;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class AgentBridgeMcpSetupWindow : EditorWindow
    {
        internal const string WindowTitle = "MCP Setup & Diagnostics";
        internal const string DisabledActionTooltip = "Not wired in this build yet";
        internal const string BridgeToggleLabel = "Enable Unity Agent Bridge";
        internal const string BridgeConfirmTitle = "Confirm Unity Agent Bridge Setting";

        private Vector2 _scrollPosition;
        private McpStatusSection _statusSection;
        private McpClientConfigSection _clientConfigSection;
        private McpDiagnosticsSection _diagnosticsSection;
        private McpEditorSettings _settings;
        private McpEnvironmentSnapshot _snapshot;
        private IMcpClientConfigWriter _codexWriter;
        private IMcpClientConfigWriter _claudeWriter;
        private IMcpClientConfigWriter _cursorWriter;
        private IMcpClientConfigWriter _copilotWriter;
        private IMcpEditorSettingsStore _settingsStore;
        private McpEnvironmentProbe _environmentProbe;
        private McpPathResolver _pathResolver;
        private IMcpDiagnosticsRunner _diagnosticsRunner;
        private McpReadinessAggregator _readinessAggregator;
        private McpReportFormatter _reportFormatter;
        private McpRuntimeInitializer _runtimeInitializer;
        private System.Collections.Generic.IReadOnlyList<McpDiagnosticCheck> _diagnosticChecks;
        private McpReadiness _readiness;
        private string _diagnosticReport;
        private CancellationTokenSource _diagnosticsCts;
        private Task<DiagnosticsRunResult> _diagnosticsTask;
        private bool _diagnosticsRunning;
        private CancellationTokenSource _runtimeInitCts;
        private Task<ManagedBlockApplyResult> _runtimeInitTask;
        private bool _runtimeInitRunning;
        private string _runtimeInitMessage;
        private bool _initialDiagnosticsQueued;
        private bool _showAdvancedDetails;
        private IUnityToolFacade _toolFacade;

        [MenuItem("Tools/Unity Agent Bridge/MCP Setup & Diagnostics")]
        public static void ShowWindow()
        {
            var window = GetWindow<AgentBridgeMcpSetupWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(720f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            _statusSection = new McpStatusSection();
            _clientConfigSection = new McpClientConfigSection(DisabledActionTooltip);
            _diagnosticsSection = new McpDiagnosticsSection(DisabledActionTooltip);
            _settingsStore = new McpEditorSettingsStore();
            _environmentProbe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), null);
            _pathResolver = new McpPathResolver();
            _diagnosticsRunner = new McpDiagnosticsRunner();
            _readinessAggregator = new McpReadinessAggregator();
            _reportFormatter = new McpReportFormatter();
            _runtimeInitializer = new McpRuntimeInitializer();
            _settings = _settingsStore.Load();
            _snapshot = _environmentProbe.SnapshotAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
            _codexWriter = new CodexProjectConfigWriter();
            _claudeWriter = new ClaudeCodeProjectConfigWriter();
            _cursorWriter = new CursorProjectConfigWriter();
            _copilotWriter = new GitHubCopilotProjectConfigWriter();
            _diagnosticChecks = new McpDiagnosticCheck[0];
            _readiness = McpReadiness.NotChecked;
            _diagnosticReport = string.Empty;
            _runtimeInitMessage = string.Empty;
            _toolFacade = CreateToolFacade();
        }

        private void OnGUI()
        {
            EnsureInitialized();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Unity MCP Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Prepare the project runtime, apply MCP client config, then verify the setup. Details are available under Advanced.", MessageType.Info);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.Space(4f);

            DrawSetupFlowControls();
            EditorGUILayout.Space(12f);

            _clientConfigSection.Draw(_codexWriter, _claudeWriter, _cursorWriter, _copilotWriter, _settings);
            EditorGUILayout.Space(12f);

            _diagnosticsSection.Draw(
                _diagnosticChecks,
                GetEffectiveReadiness(_readiness),
                _diagnosticReport,
                _diagnosticsRunning,
                RunDiagnostics,
                ClearDiagnostics);

            EditorGUILayout.Space(12f);
            DrawCommandListEntryPoint();
            EditorGUILayout.Space(12f);
            _showAdvancedDetails = EditorGUILayout.Foldout(_showAdvancedDetails, "Advanced Details", true);
            if (_showAdvancedDetails)
            {
                DrawLocalControls();
            }

            EditorGUILayout.EndScrollView();
        }

        private void EnsureInitialized()
        {
            if (_statusSection == null || _clientConfigSection == null || _diagnosticsSection == null)
            {
                OnEnable();
            }
        }

        private void QueueInitialDiagnosticsIfNeeded()
        {
            if (_initialDiagnosticsQueued || _diagnosticsRunning)
            {
                return;
            }

            if (_readiness != McpReadiness.NotChecked)
            {
                return;
            }

            if (_diagnosticChecks != null && _diagnosticChecks.Count > 0)
            {
                return;
            }

            _initialDiagnosticsQueued = true;
            RunDiagnostics();
        }

        private void DrawLocalControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Advanced Local Controls", EditorStyles.boldLabel);

                var bridgeEnabled = AgentBridgeLocalPreferences.BridgeEnabled;
                var requestedBridgeEnabled = EditorGUILayout.ToggleLeft(
                    BridgeToggleLabel,
                    bridgeEnabled);

                if (requestedBridgeEnabled != bridgeEnabled)
                {
                    TrySetBridgeEnabled(requestedBridgeEnabled, DisplayBridgeConfirmation);
                }

                EditorGUILayout.Space(8f);
                DrawConfiguredProjectControls();
                EditorGUILayout.Space(8f);
                var currentWorkspaceRoot = _settings != null ? _settings.WorkspaceRoot : string.Empty;
                var defaultWorkspaceRoot = GetDefaultWorkspaceRoot();
                var effectiveWorkspaceRoot = ResolveDisplayedWorkspaceRoot(currentWorkspaceRoot, defaultWorkspaceRoot);
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                var workspaceRootHasIssue = string.IsNullOrWhiteSpace(currentWorkspaceRoot) ||
                                            !Directory.Exists(effectiveWorkspaceRoot) ||
                                            !McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, effectiveWorkspaceRoot);
                var workspaceRootTooltip = workspaceRootHasIssue
                    ? "Workspace Root must be the current Unity project root or one of its first three ancestor directories."
                    : "Resolved workspace used for managed client config targets.";
                var workspaceRootLabel = workspaceRootHasIssue ? "Workspace Root*" : "Workspace Root";
                EditorGUILayout.LabelField(new GUIContent(workspaceRootLabel, workspaceRootTooltip), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var nextWorkspaceRoot = EditorGUILayout.TextField(new GUIContent(workspaceRootLabel, workspaceRootTooltip), effectiveWorkspaceRoot);
                    if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("Select Workspace Root", effectiveWorkspaceRoot, string.Empty);
                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            nextWorkspaceRoot = selected;
                        }
                    }

                    if (!string.Equals(nextWorkspaceRoot, effectiveWorkspaceRoot, StringComparison.Ordinal))
                    {
                        _settings.WorkspaceRoot = McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, nextWorkspaceRoot)
                            ? (nextWorkspaceRoot ?? string.Empty)
                            : effectiveWorkspaceRoot;
                        _settingsStore.Save(_settings);
                        _snapshot = _environmentProbe.SnapshotAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
                EditorGUILayout.Space(8f);
                var currentToolsRoot = _settings != null ? _settings.ToolsRoot : string.Empty;
                var defaultToolsRoot = GetDefaultToolsRoot();
                var effectiveToolsRoot = ResolveDisplayedToolsRoot(currentToolsRoot, defaultToolsRoot);
                var toolsRootHasIssue = string.IsNullOrWhiteSpace(currentToolsRoot) || !Directory.Exists(effectiveToolsRoot);
                var toolsRootTooltip = toolsRootHasIssue
                    ? "Tools Root is unresolved or does not exist. Configure a valid Tools directory or install the delivery root."
                    : "Resolved Tools Root used for launcher, server, and CLI discovery.";
                var toolsRootLabel = toolsRootHasIssue ? "Tools Root*" : "Tools Root";
                EditorGUILayout.LabelField(new GUIContent(toolsRootLabel, toolsRootTooltip), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var nextToolsRoot = EditorGUILayout.TextField(new GUIContent(toolsRootLabel, toolsRootTooltip), effectiveToolsRoot);
                    if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("Select Tools Root", effectiveToolsRoot, string.Empty);
                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            nextToolsRoot = selected;
                        }
                    }

                    if (!string.Equals(nextToolsRoot, effectiveToolsRoot, StringComparison.Ordinal))
                    {
                        _settings.ToolsRoot = nextToolsRoot ?? string.Empty;
                        _settingsStore.Save(_settings);
                        _snapshot = _environmentProbe.SnapshotAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
            }
        }

        private void DrawSetupFlowControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Step 1: Prepare Project Runtime", EditorStyles.boldLabel);
                DrawRuntimeInitializationControls();
                EditorGUILayout.Space(8f);
                DrawWorkspaceConfigTargetSummary();
                EditorGUILayout.Space(12f);
                _statusSection.Draw(CreateStatusViewModel(_snapshot, _settings, _pathResolver, _readiness));
            }
        }

        private void DrawCommandListEntryPoint()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Step 4: Command List", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Available Unity MCP commands",
                    _toolFacade == null ? "Unavailable" : _toolFacade.ListTools().Count.ToString());
                EditorGUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(_toolFacade == null))
                {
                    if (GUILayout.Button("Open Command List", GUILayout.Width(160f)))
                    {
                        McpCommandCatalogWindow.ShowWindow(_toolFacade != null ? _toolFacade.ListTools() : null);
                    }
                }
            }
        }

        private void DrawWorkspaceConfigTargetSummary()
        {
            var effectiveSettings = _settings ?? McpEditorSettingsDefaults.Create();
            var resolver = _pathResolver ?? new McpPathResolver();
            var workspaceRoot = resolver.GetWorkspaceRoot(effectiveSettings);
            var tooltip = "Managed Codex, Claude Code, Cursor, and GitHub Copilot config files are written under this workspace root. Edit it in Advanced Details when the detected root is wrong.";

            EditorGUILayout.LabelField(new GUIContent("Workspace Config Target", tooltip), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrWhiteSpace(workspaceRoot) ? "Not configured" : workspaceRoot,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (!string.IsNullOrWhiteSpace(workspaceRoot))
            {
                EditorGUILayout.LabelField(".codex/config.toml", Path.Combine(workspaceRoot, ".codex", "config.toml"));
                EditorGUILayout.LabelField(".mcp.json", Path.Combine(workspaceRoot, ".mcp.json"));
                EditorGUILayout.LabelField(".cursor/mcp.json", Path.Combine(workspaceRoot, ".cursor", "mcp.json"));
                EditorGUILayout.LabelField(".vscode/mcp.json", Path.Combine(workspaceRoot, ".vscode", "mcp.json"));
            }
        }

        private void DrawConfiguredProjectControls()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var resolvedToolsRoot = (_pathResolver ?? new McpPathResolver()).ResolveToolsRoot(_settings ?? McpEditorSettingsDefaults.Create());
            var configuredProjectPath = ReadConfiguredUnityProjectPath(projectRoot, resolvedToolsRoot);
            var configPath = ResolveLauncherConfigPath(projectRoot, resolvedToolsRoot);
            var hasIssue = HasConfiguredProjectIssue(projectRoot, configuredProjectPath) ||
                           string.Equals(configuredProjectPath, "Invalid Config", StringComparison.Ordinal);
            var tooltip = hasIssue
                ? "Configured Unity Project is invalid or mismatched. Sync writes the currently opened Unity project path into Start-Codex-With-UnityMcp.json."
                : "Shared launcher binding used by the direct MCP launcher.";
            var label = hasIssue ? "Configured Project*" : "Configured Project";

            EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(configuredProjectPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(configPath)))
                {
                    if (GUILayout.Button(new GUIContent("Sync", tooltip), GUILayout.Width(90f)))
                    {
                        SyncConfiguredProject(configPath, projectRoot);
                    }
                }
            }
        }

        private void DrawRuntimeInitializationControls()
        {
            var effectiveSettings = _settings ?? McpEditorSettingsDefaults.Create();
            var runtimeRoot = (_pathResolver ?? new McpPathResolver()).ResolveWorkspaceRuntimeRoot(effectiveSettings);
            var tooltip = "Prepare a project-local MCP runtime by copying the packaged launcher, MCP payload, and CLI executable into the Unity project .unitymcp/runtime directory.";
            EditorGUILayout.LabelField(new GUIContent("MCP Runtime", tooltip), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(runtimeRoot) ? "Not configured" : runtimeRoot, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(runtimeRoot) || _runtimeInitRunning))
                {
                    if (GUILayout.Button(new GUIContent(_runtimeInitRunning ? "Preparing..." : "Prepare", tooltip), GUILayout.Width(90f)))
                    {
                        InitializeMcpRuntime();
                    }
                }
            }

            if (_runtimeInitRunning)
            {
                EditorGUILayout.HelpBox("Preparing a project-local MCP runtime from the packaged payload. The window remains responsive while the background task completes.", MessageType.Info);
            }
            else if (!string.IsNullOrWhiteSpace(_runtimeInitMessage))
            {
                EditorGUILayout.HelpBox(_runtimeInitMessage, MessageType.None);
            }
        }

        internal bool TrySetBridgeEnabled(bool enabled, System.Func<bool, bool> confirm)
        {
            var confirmCallback = confirm ?? DisplayBridgeConfirmation;
            if (!confirmCallback(enabled))
            {
                return false;
            }

            if (AgentBridgeLocalPreferences.BridgeEnabled == enabled)
            {
                return false;
            }

            AgentBridgeLocalPreferences.BridgeEnabled = enabled;
            AgentBridgeBootstrap.Reconfigure();
            Repaint();
            return true;
        }

        internal static bool DisplayBridgeConfirmation(bool enabled)
        {
            var message = enabled
                ? "Unity Agent Bridge command polling and tool execution will be enabled for this project. Continue?"
                : "Unity Agent Bridge command polling and tool execution will be disabled for this project. Continue?";

            return EditorUtility.DisplayDialog(
                BridgeConfirmTitle,
                message,
                enabled ? "Enable Bridge" : "Disable Bridge",
                "Cancel");
        }

        private static McpStatusViewModel CreateStatusViewModel(
            McpEnvironmentSnapshot snapshot,
            McpEditorSettings settings,
            McpPathResolver pathResolver,
            McpReadiness readiness)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var effectiveSettings = settings ?? McpEditorSettingsDefaults.Create();
            var effectiveResolver = pathResolver ?? new McpPathResolver();
            var resolvedWorkspaceRoot = effectiveResolver.GetWorkspaceRoot(effectiveSettings);
            var resolvedToolsRoot = effectiveResolver.ResolveToolsRoot(effectiveSettings);
            var configuredUnityProjectPath = ReadConfiguredUnityProjectPath(projectRoot, resolvedToolsRoot);
            var configuredUnityProjectHasIssue = HasConfiguredProjectIssue(projectRoot, configuredUnityProjectPath);
            var effectiveReadiness = AdjustReadinessForConfiguredProject(readiness, configuredUnityProjectHasIssue);

            return new McpStatusViewModel
            {
                ConfiguredUnityProjectHasIssue = configuredUnityProjectHasIssue,
                ConfiguredUnityProjectIssueTooltip = GetConfiguredProjectIssueTooltip(projectRoot, configuredUnityProjectPath),
                ConfiguredUnityProjectConfigPath = ResolveLauncherConfigPath(projectRoot, resolvedToolsRoot),
                WorkspaceRoot = string.IsNullOrEmpty(resolvedWorkspaceRoot) ? "Not configured" : resolvedWorkspaceRoot,
                ToolsRootHasIssue = HasToolsRootIssue(resolvedToolsRoot),
                ToolsRootIssueTooltip = GetToolsRootIssueTooltip(resolvedToolsRoot),
                McpReadinessHasIssue = HasReadinessIssue(effectiveReadiness),
                McpReadinessIssueTooltip = GetReadinessIssueTooltip(effectiveReadiness),
                UnityBridgeStatus = AgentBridgeLocalPreferences.BridgeEnabled ? "OK" : "Disabled",
                UnityProjectPath = projectRoot,
                ConfiguredUnityProjectPath = configuredUnityProjectPath,
                ToolsRoot = string.IsNullOrEmpty(resolvedToolsRoot) ? "Not configured" : resolvedToolsRoot,
                LauncherPath = string.IsNullOrEmpty(effectiveResolver.ResolveLauncherPath(effectiveSettings)) ? "Not configured" : effectiveResolver.ResolveLauncherPath(effectiveSettings),
                McpServerRoot = string.IsNullOrEmpty(effectiveResolver.ResolveMcpServerRoot(effectiveSettings)) ? "Not configured" : effectiveResolver.ResolveMcpServerRoot(effectiveSettings),
                CliRoot = string.IsNullOrEmpty(effectiveResolver.ResolveCliRoot(effectiveSettings)) ? "Not configured" : effectiveResolver.ResolveCliRoot(effectiveSettings),
                CliStatus = FormatCliStatus(effectiveSettings, effectiveResolver),
                DotnetVersion = FormatToolStatus(snapshot.Dotnet),
                McpReadiness = FormatReadiness(effectiveReadiness),
                PriorityMessage = BuildPriorityMessage(projectRoot, configuredUnityProjectPath, resolvedToolsRoot, effectiveReadiness),
                PriorityMessageType = BuildPriorityMessageType(projectRoot, configuredUnityProjectPath, resolvedToolsRoot, effectiveReadiness),
            };
        }

        private static string FormatCliStatus(McpEditorSettings settings, McpPathResolver pathResolver)
        {
            if (settings == null)
            {
                return "Missing";
            }

            if (!string.IsNullOrWhiteSpace(settings.CliExecutablePath))
            {
                return File.Exists(settings.CliExecutablePath) ? "OK" : "Missing";
            }

            if (settings.PreferPublishedCli)
            {
                var cliRoot = pathResolver != null ? pathResolver.ResolveCliRoot(settings) : string.Empty;
                if (!string.IsNullOrEmpty(cliRoot))
                {
                    var candidate = Path.Combine(cliRoot, "out", "win-x64", "unity-agent-bridge.exe");
                    if (File.Exists(candidate))
                    {
                        return "OK";
                    }
                }
            }

            var dotnetPath = pathResolver != null ? pathResolver.ResolveExecutablePath(settings.DotnetPath, "dotnet") : string.Empty;
            return string.IsNullOrEmpty(dotnetPath) ? "Missing" : "OK";
        }

        private static string FormatToolStatus(ToolProbeResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.ResolvedPath))
            {
                return "Missing";
            }

            return string.IsNullOrEmpty(result.VersionText) ? "OK" : result.VersionText;
        }

        internal static string ReadConfiguredUnityProjectPath(string projectRoot, string toolsRoot)
        {
            if (string.IsNullOrEmpty(projectRoot) && string.IsNullOrEmpty(toolsRoot))
            {
                return "Not Configured";
            }

            var configPath = ResolveLauncherConfigPath(projectRoot, toolsRoot);
            if (string.IsNullOrEmpty(configPath))
            {
                return "Not Configured";
            }

            if (!File.Exists(configPath))
            {
                return "Not Configured";
            }

            try
            {
                var content = File.ReadAllText(configPath);
                var marker = "\"unityProjectPath\"";
                var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    return "Invalid Config";
                }

                var colonIndex = content.IndexOf(':', markerIndex + marker.Length);
                if (colonIndex < 0)
                {
                    return "Invalid Config";
                }

                var firstQuote = content.IndexOf('"', colonIndex + 1);
                if (firstQuote < 0)
                {
                    return "Invalid Config";
                }

                var secondQuote = content.IndexOf('"', firstQuote + 1);
                if (secondQuote < 0)
                {
                    return "Invalid Config";
                }

                var configuredPath = content.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                return string.IsNullOrWhiteSpace(configuredPath) ? "Not Configured" : configuredPath;
            }
            catch
            {
                return "Invalid Config";
            }
        }

        private static string ResolveLauncherConfigPath(string projectRoot, string toolsRoot)
        {
            if (!string.IsNullOrWhiteSpace(toolsRoot))
            {
                var toolsConfigPath = Path.Combine(toolsRoot, "AgentBridge", "Start-Codex-With-UnityMcp.json");
                if (File.Exists(toolsConfigPath))
                {
                    return toolsConfigPath;
                }
            }

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            return Path.Combine(projectRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json");
        }

        private static string GetDefaultToolsRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            return Path.Combine(projectRoot, "Tools");
        }

        private static string GetDefaultWorkspaceRoot()
        {
            return new McpPathResolver().GetWorkspaceRoot(McpEditorSettingsDefaults.Create());
        }

        private static string ResolveDisplayedWorkspaceRoot(string configuredWorkspaceRoot, string defaultWorkspaceRoot)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configuredWorkspaceRoot) &&
                Directory.Exists(configuredWorkspaceRoot) &&
                McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, configuredWorkspaceRoot))
            {
                return configuredWorkspaceRoot;
            }

            return defaultWorkspaceRoot ?? string.Empty;
        }

        private static string ResolveDisplayedToolsRoot(string configuredToolsRoot, string defaultToolsRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredToolsRoot) && Directory.Exists(configuredToolsRoot))
            {
                return configuredToolsRoot;
            }

            return defaultToolsRoot ?? string.Empty;
        }

        private static string FormatReadiness(McpReadiness readiness)
        {
            switch (readiness)
            {
                case McpReadiness.NotChecked:
                    return "Not checked yet";
                case McpReadiness.Degraded:
                    return "Needs attention";
                case McpReadiness.Unavailable:
                    return "Unavailable";
                case McpReadiness.Ready:
                    return "OK";
                default:
                    return readiness.ToString();
            }
        }

        private static McpReadiness AdjustReadinessForConfiguredProject(McpReadiness readiness, bool configuredProjectHasIssue)
        {
            if (!configuredProjectHasIssue)
            {
                return readiness;
            }

            if (readiness == McpReadiness.Unavailable)
            {
                return readiness;
            }

            return McpReadiness.Degraded;
        }

        private static string BuildPriorityMessage(string currentProject, string configuredProject, string toolsRoot, McpReadiness readiness)
        {
            if (HasConfiguredProjectIssue(currentProject, configuredProject))
            {
                return "Configured Unity Project does not match the currently opened Unity project.";
            }

            if (HasToolsRootIssue(toolsRoot))
            {
                return "Tools Root is not resolved yet. Configure it or use an installed delivery root.";
            }

            if (HasReadinessIssue(readiness))
            {
                return "Resolve the diagnostics issue below before relying on MCP readiness.";
            }

            return string.Empty;
        }

        private static MessageType BuildPriorityMessageType(string currentProject, string configuredProject, string toolsRoot, McpReadiness readiness)
        {
            if (HasConfiguredProjectIssue(currentProject, configuredProject))
            {
                return MessageType.Warning;
            }

            if (HasToolsRootIssue(toolsRoot))
            {
                return MessageType.Warning;
            }

            if (readiness == McpReadiness.Unavailable)
            {
                return MessageType.Error;
            }

            if (readiness == McpReadiness.Degraded)
            {
                return MessageType.Warning;
            }

            return MessageType.None;
        }

        private static bool HasConfiguredProjectIssue(string currentProject, string configuredProject)
        {
            if (string.IsNullOrEmpty(currentProject) || string.IsNullOrEmpty(configuredProject))
            {
                return false;
            }

            if (configuredProject == "Invalid Config")
            {
                return true;
            }

            if (configuredProject == "Not Configured")
            {
                return false;
            }

            return !string.Equals(Path.GetFullPath(currentProject), Path.GetFullPath(configuredProject), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetConfiguredProjectIssueTooltip(string currentProject, string configuredProject)
        {
            if (string.Equals(configuredProject, "Invalid Config", StringComparison.Ordinal))
            {
                return "Configured Unity Project binding file is invalid. Sync the current project or repair Start-Codex-With-UnityMcp.json.";
            }

            return HasConfiguredProjectIssue(currentProject, configuredProject)
                ? "Configured Unity Project does not match the currently opened Unity project. Direct MCP launcher may bind to a different project."
                : string.Empty;
        }

        private static bool HasToolsRootIssue(string toolsRoot)
        {
            return string.IsNullOrEmpty(toolsRoot) || string.Equals(toolsRoot, "Not configured", StringComparison.Ordinal);
        }

        private static string GetToolsRootIssueTooltip(string toolsRoot)
        {
            return HasToolsRootIssue(toolsRoot)
                ? "Tools Root is unresolved. Configure a valid Tools directory or install the delivery root."
                : string.Empty;
        }

        private static bool HasReadinessIssue(McpReadiness readiness)
        {
            return readiness == McpReadiness.Unavailable || readiness == McpReadiness.Degraded;
        }

        private static string GetReadinessIssueTooltip(McpReadiness readiness)
        {
            if (readiness == McpReadiness.Unavailable)
            {
                return "Diagnostics found blocking issues. Review the highest-priority diagnostic result before using MCP.";
            }

            if (readiness == McpReadiness.Degraded)
            {
                return "Diagnostics found non-blocking issues. Review the highest-priority diagnostic result before relying on MCP readiness.";
            }

            return string.Empty;
        }

        private void RunDiagnostics()
        {
            EnsureInitialized();
            if (_diagnosticsRunning)
            {
                return;
            }

            _diagnosticsCts?.Cancel();
            _diagnosticsCts?.Dispose();
            _diagnosticsCts = new CancellationTokenSource();
            var token = _diagnosticsCts.Token;
            _diagnosticsRunning = true;
            _diagnosticsTask = RunDiagnosticsAsync(token);
            EditorApplication.update -= PollDiagnosticsTask;
            EditorApplication.update += PollDiagnosticsTask;
            Repaint();
        }

        private async Task<DiagnosticsRunResult> RunDiagnosticsAsync(CancellationToken token)
        {
            try
            {
                var settings = _settingsStore.Load();
                var snapshot = await _environmentProbe.SnapshotAsync(settings, token);
                var checks = await _diagnosticsRunner.RunAsync(settings, token);
                var readiness = _readinessAggregator.Aggregate(checks, settings);
                var report = _reportFormatter.Format(checks, readiness, settings);

                return new DiagnosticsRunResult
                {
                    Settings = settings,
                    Snapshot = snapshot,
                    Checks = checks,
                    Readiness = readiness,
                    Report = report,
                };
            }
            catch (OperationCanceledException)
            {
                return new DiagnosticsRunResult
                {
                    Cancelled = true,
                };
            }
            catch (System.Exception exception)
            {
                var checks = new[]
                {
                    new McpDiagnosticCheck
                    {
                        Code = "MCP000",
                        Severity = McpDiagnosticSeverity.Error,
                        Summary = "Diagnostics Failed",
                        Details = exception.Message,
                        Remediation = "Check Library/AgentBridge/logs and rerun Quick Diagnostics.",
                    }
                };

                return new DiagnosticsRunResult
                {
                    Checks = checks,
                    Readiness = McpReadiness.Unavailable,
                    Report = _reportFormatter.Format(checks, McpReadiness.Unavailable, _settings),
                    Error = exception,
                };
            }
        }

        private void PollDiagnosticsTask()
        {
            if (_diagnosticsTask == null || !_diagnosticsTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollDiagnosticsTask;
            var completedTask = _diagnosticsTask;
            _diagnosticsTask = null;
            _diagnosticsRunning = false;

            if (completedTask.IsFaulted)
            {
                var exception = completedTask.Exception != null ? completedTask.Exception.GetBaseException() : null;
                _diagnosticChecks = new[]
                {
                    new McpDiagnosticCheck
                    {
                        Code = "MCP000",
                        Severity = McpDiagnosticSeverity.Error,
                        Summary = "Diagnostics Failed",
                        Details = exception != null ? exception.Message : "Unknown diagnostics failure.",
                        Remediation = "Check Library/AgentBridge/logs and rerun Quick Diagnostics.",
                    }
                };
                _readiness = McpReadiness.Unavailable;
                _diagnosticReport = _reportFormatter.Format(_diagnosticChecks, GetEffectiveReadiness(_readiness), _settings);
                Repaint();
                return;
            }

            var result = completedTask.Result;
            if (result.Cancelled)
            {
                _readiness = McpReadiness.NotChecked;
                Repaint();
                return;
            }

            if (result.Settings != null)
            {
                _settings = result.Settings;
            }

            if (result.Snapshot != null)
            {
                _snapshot = result.Snapshot;
            }

            _diagnosticChecks = result.Checks ?? new McpDiagnosticCheck[0];
            _readiness = result.Readiness;
            _diagnosticReport = _reportFormatter.Format(_diagnosticChecks, GetEffectiveReadiness(_readiness), _settings);
            Repaint();
        }

        private void ClearDiagnostics()
        {
            if (_diagnosticsRunning)
            {
                return;
            }

            _diagnosticChecks = new McpDiagnosticCheck[0];
            _readiness = McpReadiness.NotChecked;
            _diagnosticReport = string.Empty;
            _initialDiagnosticsQueued = false;
            Repaint();
        }

        internal static IUnityToolFacade CreateToolFacade()
        {
            var settingsLoad = AgentBridgeSettingsLoader.Load();
            var settings = settingsLoad?.Settings ?? ScriptableObject.CreateInstance<AgentBridgeSettings>();
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var paths = new AgentBridgePaths(projectRoot, settings);
            paths.EnsureDirectories();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var registry = new AgentToolRegistry();
            registry.Discover();
            UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger, new UnityMcpPluginHostServices
            {
                Settings = settings,
                Queue = new AgentCommandQueue(projectRoot, settings.tempRoot),
                Registry = registry,
                Logger = logger
            });
            var facade = new UnityToolFacade(registry, settings, logger);
            return facade;
        }

        internal static void SyncConfiguredProjectFile(string configPath, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalizedProjectPath = Path.GetFullPath(projectRoot).Replace("\\", "\\\\");
            var content = "{\n  \"unityProjectPath\": \"" + normalizedProjectPath + "\"\n}\n";
            File.WriteAllText(configPath, content);
        }

        private void SyncConfiguredProject(string configPath, string projectRoot)
        {
            SyncConfiguredProjectFile(configPath, projectRoot);
            _snapshot = _environmentProbe.SnapshotAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
            _diagnosticReport = _reportFormatter.Format(_diagnosticChecks, GetEffectiveReadiness(_readiness), _settings);
            Repaint();
        }

        internal McpReadiness GetEffectiveReadiness(McpReadiness readiness)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var resolvedToolsRoot = (_pathResolver ?? new McpPathResolver()).ResolveToolsRoot(_settings ?? McpEditorSettingsDefaults.Create());
            var configuredProjectPath = ReadConfiguredUnityProjectPath(projectRoot, resolvedToolsRoot);
            return GetEffectiveReadinessForConfiguredProject(readiness, projectRoot, configuredProjectPath);
        }

        internal static McpReadiness GetEffectiveReadinessForConfiguredProject(
            McpReadiness readiness,
            string currentProject,
            string configuredProject)
        {
            return AdjustReadinessForConfiguredProject(readiness, HasConfiguredProjectIssue(currentProject, configuredProject));
        }

        private void InitializeMcpRuntime()
        {
            if (_runtimeInitRunning)
            {
                return;
            }

            _runtimeInitCts?.Cancel();
            _runtimeInitCts?.Dispose();
            _runtimeInitCts = new CancellationTokenSource();
            _runtimeInitRunning = true;
            _runtimeInitMessage = string.Empty;
            _runtimeInitTask = _runtimeInitializer.InitializeRuntimeAsync(_settings, _runtimeInitCts.Token);
            EditorApplication.update -= PollRuntimeInitializationTask;
            EditorApplication.update += PollRuntimeInitializationTask;
            Repaint();
        }

        private void PollRuntimeInitializationTask()
        {
            if (_runtimeInitTask == null || !_runtimeInitTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollRuntimeInitializationTask;
            var completedTask = _runtimeInitTask;
            _runtimeInitTask = null;
            _runtimeInitRunning = false;

            if (completedTask.IsFaulted)
            {
                var exception = completedTask.Exception != null ? completedTask.Exception.GetBaseException() : null;
                _runtimeInitMessage = exception != null ? exception.Message : "MCP runtime initialization failed.";
                EditorUtility.DisplayDialog("MCP Runtime Initialization Failed", _runtimeInitMessage, "OK");
                Repaint();
                return;
            }

            var result = completedTask.Result;
            if (!result.Applied)
            {
                _runtimeInitMessage = string.IsNullOrWhiteSpace(result.Reason) ? "MCP runtime initialization failed." : result.Reason;
                EditorUtility.DisplayDialog("MCP Runtime Initialization Failed", _runtimeInitMessage, "OK");
                Repaint();
                return;
            }

            _runtimeInitMessage = "Project-local MCP runtime prepared successfully.";
            AgentBridgeBootstrap.Reconfigure();
            _snapshot = _environmentProbe.SnapshotAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
            Repaint();
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollDiagnosticsTask;
            EditorApplication.update -= PollRuntimeInitializationTask;
            _diagnosticsCts?.Cancel();
            _diagnosticsCts?.Dispose();
            _diagnosticsCts = null;
            _diagnosticsTask = null;
            _diagnosticsRunning = false;
            _runtimeInitCts?.Cancel();
            _runtimeInitCts?.Dispose();
            _runtimeInitCts = null;
            _runtimeInitTask = null;
            _runtimeInitRunning = false;
            _initialDiagnosticsQueued = false;
        }

        private sealed class DiagnosticsRunResult
        {
            public McpEditorSettings Settings { get; set; }
            public McpEnvironmentSnapshot Snapshot { get; set; }
            public System.Collections.Generic.IReadOnlyList<McpDiagnosticCheck> Checks { get; set; }
            public McpReadiness Readiness { get; set; } = McpReadiness.NotChecked;
            public string Report { get; set; } = string.Empty;
            public bool Cancelled { get; set; }
            public System.Exception Error { get; set; }
        }
    }
}
