using NUnit.Framework;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class AgentBridgeMcpSetupWindowTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<AgentBridgeMcpSetupWindow>())
            {
                window.Close();
            }

            foreach (var window in Resources.FindObjectsOfTypeAll<McpCommandCatalogWindow>())
            {
                window.Close();
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_161.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_161")]
        public void ShowWindow_OpensWindow()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();

            var window = FindOpenWindow();

            Assert.That(window, Is.Not.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_162.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_162")]
        public void ShowWindow_SetsExpectedTitle()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();

            var window = FindOpenWindow();

            Assert.That(window.titleContent.text, Is.EqualTo(AgentBridgeMcpSetupWindow.WindowTitle));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_163.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_163")]
        public void ShowWindow_SetsMinimumSize()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();

            var window = FindOpenWindow();

            Assert.That(window.minSize.x, Is.GreaterThanOrEqualTo(720f));
            Assert.That(window.minSize.y, Is.GreaterThanOrEqualTo(560f));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_164.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_164")]
        public void Window_CanBeClosed()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();
            var window = FindOpenWindow();

            window.Close();

            Assert.That(FindOpenWindow(), Is.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_165.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_165")]
        public void Window_CanBeReopenedAfterClose()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();
            var first = FindOpenWindow();
            first.Close();

            AgentBridgeMcpSetupWindow.ShowWindow();
            var second = FindOpenWindow();

            Assert.That(second, Is.Not.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_166.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_166")]
        public void StatusSection_Draw_DoesNotThrow()
        {
            var section = new McpStatusSection();
            var viewModel = new McpStatusViewModel
            {
                UnityBridgeStatus = "OK",
                UnityProjectPath = "Project",
                ConfiguredUnityProjectPath = "ConfiguredProject",
                ToolsRoot = "D:/Repo/com.unitymcp.agent-bridge/Tools~",
                LauncherPath = "D:/Repo/com.unitymcp.agent-bridge/Tools~/AgentBridge/Start-UnityAgentBridge-Mcp.cmd",
                McpServerRoot = "Not configured",
                CliRoot = "D:/Repo/com.unitymcp.agent-bridge/Tools~/UnityAgentBridge/cli",
                CliStatus = "Missing",
                DotnetVersion = "Missing",
                McpReadiness = "Not checked yet",
                PriorityMessage = "Configured Unity Project does not match the currently opened Unity project.",
                PriorityMessageType = MessageType.Warning,
                ConfiguredUnityProjectHasIssue = true,
                ConfiguredUnityProjectIssueTooltip = "Configured Unity Project does not match the currently opened Unity project. Direct MCP launcher may bind to a different project.",
                ToolsRootHasIssue = true,
                ToolsRootIssueTooltip = "Tools Root is unresolved. Configure a valid Tools directory or install the delivery root.",
                McpReadinessHasIssue = true,
                McpReadinessIssueTooltip = "Diagnostics found non-blocking issues. Review the highest-priority diagnostic result before relying on MCP readiness.",
            };

            Assert.DoesNotThrow(() => section.Draw(viewModel));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_167.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_167")]
        public void ClientConfigSection_Draw_DoesNotThrow()
        {
            var section = new McpClientConfigSection(AgentBridgeMcpSetupWindow.DisabledActionTooltip);

            Assert.DoesNotThrow(() => section.Draw(new StubWriter(), new StubWriter(), new StubWriter(), new StubWriter(), new McpEditorSettings()));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_168.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_168")]
        public void DiagnosticsSection_Draw_DoesNotThrow()
        {
            var section = new McpDiagnosticsSection(AgentBridgeMcpSetupWindow.DisabledActionTooltip);

            Assert.DoesNotThrow(() => section.Draw(
                new McpDiagnosticCheck[0],
                McpReadiness.NotChecked,
                string.Empty,
                false,
                () => { },
                () => { }));
        }

        [Test]
        [Category("AGBM_UI")]
        public void DiagnosticsSection_Draw_WithChecks_DoesNotThrow()
        {
            var section = new McpDiagnosticsSection(AgentBridgeMcpSetupWindow.DisabledActionTooltip);
            var checks = new[]
            {
                new McpDiagnosticCheck
                {
                    Code = "MCP009",
                    Severity = McpDiagnosticSeverity.Error,
                    Summary = "MCP Tool List",
                    Details = "Probe output missing required fields.",
                    Remediation = "Check the C# MCP executable, prepared runtime payload, and Unity bridge availability.",
                    Duration = System.TimeSpan.FromMilliseconds(10),
                }
            };

            Assert.DoesNotThrow(() => section.Draw(
                checks,
                McpReadiness.Unavailable,
                "report",
                false,
                () => { },
                () => { }));
        }

        [Test]
        [Category("AGBM_UI")]
        public void CommandCatalogSection_Draw_WithDescriptors_DoesNotThrow()
        {
            var window = ScriptableObject.CreateInstance<McpCommandCatalogWindow>();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var descriptors = registry.ListTools();

            Assert.DoesNotThrow(() => window.SetDescriptors(descriptors));
            ScriptableObject.DestroyImmediate(window);
        }

        [Test]
        [Category("AGBM_UI")]
        public void ClientConfigSection_Source_KeepsStep2PrimaryActionsFocused()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/McpClientConfigSection.cs"));

            Assert.That(content, Does.Contain("GUILayout.Button(\"Apply\""));
            Assert.That(content, Does.Contain("GUILayout.Button(\"Remove\""));
            Assert.That(content, Does.Contain("\"Cursor\""));
            Assert.That(content, Does.Contain("\"GitHub Copilot\""));
            Assert.That(content, Does.Contain(".cursor/mcp.json"));
            Assert.That(content, Does.Contain(".vscode/mcp.json"));
            Assert.That(content, Does.Not.Contain("GUILayout.Button(\"Copy\""));
            Assert.That(content, Does.Not.Contain("GUILayout.Button(\"Reveal\""));
        }

        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_179")]
        public void SetupWindow_Source_WiresCursorAndCopilotConfigWriters()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));

            Assert.That(content, Does.Contain("new CursorProjectConfigWriter()"));
            Assert.That(content, Does.Contain("new GitHubCopilotProjectConfigWriter()"));
            Assert.That(content, Does.Contain(".cursor/mcp.json"));
            Assert.That(content, Does.Contain(".vscode/mcp.json"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_183.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_183")]
        public void SetupWindow_Source_ExposesAiQuickConnectPromptAndBatchRefresh()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));

            Assert.That(content, Does.Contain("AI Quick Connect"));
            Assert.That(content, Does.Contain("unity_agent_bridge"));
            Assert.That(content, Does.Contain("unity_bridge_health"));
            Assert.That(content, Does.Contain("mcp__unity__get_editor_state"));
            Assert.That(content, Does.Contain("mcp__unity__project_get_info"));
            Assert.That(content, Does.Contain("Do not launch another AI client from Unity."));
            Assert.That(content, Does.Contain("Refresh All Client Configs"));
            Assert.That(content, Does.Contain("Copy AI Prompt"));
            Assert.That(content, Does.Contain("RefreshAllClientConfigs();"));
            Assert.That(content, Does.Contain("_codexWriter"));
            Assert.That(content, Does.Contain("_claudeWriter"));
            Assert.That(content, Does.Contain("_cursorWriter"));
            Assert.That(content, Does.Contain("_copilotWriter"));
        }

        [Test]
        [Category("AGBM_UI")]
        public void SetupWindow_Source_UsesDedicatedCommandListWindowAndRemovesDeprecatedUi()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));

            Assert.That(content, Does.Contain("Open Command List"));
            Assert.That(content, Does.Contain("McpCommandCatalogWindow.ShowWindow"));
            Assert.That(content, Does.Not.Contain("Advanced Config Actions"));
            Assert.That(content, Does.Not.Contain("AutoLaunchToggleLabel"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_184.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_184")]
        public void SetupWindow_Source_ExposesFindReference2ProviderToggleAndReadiness()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));
            var settingsContent = File.ReadAllText(GetPackageRelativePath("Editor/Core/AgentBridgeSettings.cs"));
            var projectSettingsContent = File.ReadAllText(GetPackageRelativePath("Editor/UI/AgentBridgeSettingsProvider.cs"));

            Assert.That(settingsContent, Does.Contain("monoBehaviourFindReference2ProviderEnabled"));
            Assert.That(projectSettingsContent, Does.Contain("Enable FindReference2 Provider"));
            Assert.That(content, Does.Contain("MonoBehaviour Semantics"));
            Assert.That(content, Does.Contain("Enable FindReference2 provider"));
            Assert.That(content, Does.Contain("GetFindReference2ReadinessLabel"));
            Assert.That(content, Does.Contain("\"not installed\""));
            Assert.That(content, Does.Contain("\"installed but disabled\""));
            Assert.That(content, Does.Contain("\"enabled\""));
            Assert.That(content, Does.Contain("\"enabled but incompatible\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_184.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_184")]
        public void SetupWindow_Source_UsesDllEntryForEnabledFindReference2Readiness()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));
            var enabledBranch = content.IndexOf("return IsFindReference2EntryTypeAvailable() ? \"enabled\" : \"enabled but incompatible\";");
            var entryType = content.IndexOf("assembly.GetType(\"vietlabs.fr2.FR2\", false)");

            Assert.That(enabledBranch, Is.GreaterThanOrEqualTo(0));
            Assert.That(entryType, Is.GreaterThanOrEqualTo(0));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_184.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_184")]
        public void SetupWindow_FindReference2Readiness_DisabledDoesNotRequireReflection()
        {
            var readiness = AgentBridgeMcpSetupWindow.GetFindReference2ReadinessLabel(false);

            Assert.That(readiness, Is.EqualTo("not installed").Or.EqualTo("installed but disabled"));
        }

        [Test]
        [Category("AGBM_UI")]
        public void SetupWindow_Source_ReconfiguresBridgeAfterRuntimePreparation()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));
            var successIndex = content.IndexOf("_runtimeInitMessage = \"Project-local MCP runtime prepared successfully.\"");
            var reconfigureIndex = content.IndexOf("AgentBridgeBootstrap.Reconfigure();", successIndex >= 0 ? successIndex : 0);
            var snapshotIndex = content.IndexOf("_snapshot = _environmentProbe.SnapshotAsync", successIndex >= 0 ? successIndex : 0);

            Assert.That(successIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(reconfigureIndex, Is.GreaterThan(successIndex));
            Assert.That(snapshotIndex, Is.GreaterThan(reconfigureIndex));
        }

        [Test]
        [Category("AGBM_UI")]
        public void SetupWindow_Source_WiresBuildLocalRuntimePrimaryAction()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/UI/AgentBridgeMcpSetupWindow.cs"));

            Assert.That(content, Does.Contain("new McpRuntimeBuilder()"));
            Assert.That(content, Does.Contain("BuildLocalRuntime();"));
            Assert.That(content, Does.Contain("\"Building...\""));
            Assert.That(content, Does.Contain("Build Local Runtime"));
            Assert.That(content, Does.Contain("PollRuntimeBuildTask"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_169.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_169")]
        public void DisabledTooltip_ExplainsDeferredActions()
        {
            Assert.That(AgentBridgeMcpSetupWindow.DisabledActionTooltip, Is.EqualTo("Not wired in this build yet"));
        }

        [Test]
        [Category("AGBM_UI")]
        public void TrySetBridgeEnabled_RequiresConfirmation()
        {
            var window = ScriptableObject.CreateInstance<AgentBridgeMcpSetupWindow>();
            var original = AgentBridgeLocalPreferences.BridgeEnabled;
            AgentBridgeLocalPreferences.BridgeEnabled = false;

            try
            {
                var changed = window.TrySetBridgeEnabled(true, _ => false);

                Assert.That(changed, Is.False);
                Assert.That(AgentBridgeLocalPreferences.BridgeEnabled, Is.False);
            }
            finally
            {
                AgentBridgeLocalPreferences.BridgeEnabled = original;
                ScriptableObject.DestroyImmediate(window);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_170.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_170")]
        public void Window_SingletonGetWindow_ReturnsSameInstance()
        {
            AgentBridgeMcpSetupWindow.ShowWindow();
            var first = FindOpenWindow();
            var second = EditorWindow.GetWindow<AgentBridgeMcpSetupWindow>();

            Assert.That(ReferenceEquals(first, second), Is.True);
        }

        [Test]
        [Category("AGBM_UI")]
        public void CommandCatalogWindow_ShowWindow_OpensDedicatedWindow()
        {
            var registry = new AgentToolRegistry();
            registry.Discover();

            var window = McpCommandCatalogWindow.ShowWindow(registry.ListTools());

            Assert.That(window, Is.Not.Null);
            Assert.That(window.titleContent.text, Is.EqualTo(McpCommandCatalogWindow.WindowTitle));
        }

        [Test]
        [Category("AGBM_UI")]
        public void SetupWindow_CreateToolFacade_IncludesRegisteredPluginCommands()
        {
            AgentBridgeBootstrap.Reconfigure();
            var facade = AgentBridgeMcpSetupWindow.CreateToolFacade();
            var descriptors = facade.ListTools();

            Assert.That(descriptors, Has.None.Matches<ToolDescriptor>(descriptor => descriptor.Name == "unity.project_info"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor => descriptor.Name == "unity.project.get_info"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor => descriptor.Name == "unity.get_editor_state"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor => descriptor.Name == "unity.ping"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.mono.find_script_guid_usages" &&
                descriptor.Description.Contains("MonoBehaviour script GUID")));
            Assert.That(descriptors.Count, Is.GreaterThan(3));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_171.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_171")]
        public void StatusSection_Draw_DoesNotThrowWhenConfiguredProjectBlocksOkSummary()
        {
            var readiness = AgentBridgeMcpSetupWindow.GetEffectiveReadinessForConfiguredProject(
                McpReadiness.Ready,
                "D:/Repo/Project",
                "D:/Repo/OtherProject");

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));

            var section = new McpStatusSection();
            var viewModel = new McpStatusViewModel
            {
                UnityBridgeStatus = "OK",
                UnityProjectPath = "Project",
                ConfiguredUnityProjectPath = "OtherProject",
                ConfiguredUnityProjectHasIssue = true,
                ConfiguredUnityProjectIssueTooltip = "Mismatch",
                ToolsRoot = "D:/Repo/Tools",
                McpReadiness = "Needs attention",
                McpReadinessHasIssue = true,
                McpReadinessIssueTooltip = "Configured project mismatch blocks OK summary.",
                PriorityMessage = "Configured Unity Project does not match the currently opened Unity project.",
                PriorityMessageType = MessageType.Warning,
            };

            Assert.DoesNotThrow(() => section.Draw(viewModel));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_172.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_172")]
        public void SyncConfiguredProjectFile_WritesCurrentProjectPath()
        {
            var root = Path.Combine(Path.GetTempPath(), "AGBM_172");
            var configPath = Path.Combine(root, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json");
            var projectPath = Path.Combine(root, "SampleProject");

            Directory.CreateDirectory(projectPath);

            AgentBridgeMcpSetupWindow.SyncConfiguredProjectFile(configPath, projectPath);

            Assert.That(File.Exists(configPath), Is.True);
            var json = File.ReadAllText(configPath);
            Assert.That(json, Does.Contain("\"unityProjectPath\""));
            Assert.That(json, Does.Contain(projectPath.Replace("\\", "\\\\")));
            var configuredProjectPath = AgentBridgeMcpSetupWindow.ReadConfiguredUnityProjectPath(projectPath, root);
            var readiness = AgentBridgeMcpSetupWindow.GetEffectiveReadinessForConfiguredProject(
                McpReadiness.Ready,
                projectPath,
                configuredProjectPath);
            Assert.That(readiness, Is.EqualTo(McpReadiness.Ready));

            Directory.Delete(root, true);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_173.md
        [Test]
        [Category("AGBM_UI")]
        [Category("AGBM_173")]
        public void SyncConfiguredProjectFile_CreatesMissingDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "AGBM_173");
            var configPath = Path.Combine(root, "Missing", "AgentBridge", "Start-Codex-With-UnityMcp.json");
            var projectPath = Path.Combine(root, "SampleProject");

            Directory.CreateDirectory(projectPath);

            AgentBridgeMcpSetupWindow.SyncConfiguredProjectFile(configPath, projectPath);

            Assert.That(File.Exists(configPath), Is.True);

            Directory.Delete(root, true);
        }

        [Test]
        [Category("AGBM_UI")]
        public void ResolveLauncherConfigPath_UsesWorkspaceToolsNotPackageTools()
        {
            var root = Path.Combine(Path.GetTempPath(), "AGBM_BINDING_PATH");
            var workspaceRoot = Path.Combine(root, "Workspace");
            var packageToolsRoot = Path.Combine(root, "Package", "Tools~");
            Directory.CreateDirectory(Path.Combine(packageToolsRoot, "AgentBridge"));

            var resolved = AgentBridgeMcpSetupWindow.ResolveLauncherConfigPath(workspaceRoot);

            Assert.That(resolved, Is.EqualTo(Path.Combine(workspaceRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json")));
            Assert.That(resolved, Does.Not.StartWith(packageToolsRoot));

            Directory.Delete(root, true);
        }

        [Test]
        [Category("AGBM_UI")]
        public void SyncConfiguredProjectFile_PreservesMaxRunningUnityEditors()
        {
            var root = Path.Combine(Path.GetTempPath(), "AGBM_LIFECYCLE_CFG");
            var configPath = Path.Combine(root, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json");
            var projectPath = Path.Combine(root, "SampleProject");

            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            Directory.CreateDirectory(projectPath);
            File.WriteAllText(configPath, "{\n  \"unityProjectPath\": \"D:/OldProject\",\n  \"maxRunningUnityEditors\": 5\n}\n");

            AgentBridgeMcpSetupWindow.SyncConfiguredProjectFile(configPath, projectPath);

            var json = File.ReadAllText(configPath);
            Assert.That(json, Does.Contain("\"unityProjectPath\""));
            Assert.That(json, Does.Contain("\"maxRunningUnityEditors\": 5"));
            Assert.That(json, Does.Contain(projectPath.Replace("\\", "\\\\")));

            Directory.Delete(root, true);
        }

        private static AgentBridgeMcpSetupWindow FindOpenWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<AgentBridgeMcpSetupWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        private static string GetPackageRelativePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class StubWriter : IMcpClientConfigWriter
        {
            public ManagedBlockApplyResult Apply(McpEditorSettings settings)
            {
                return new ManagedBlockApplyResult();
            }

            public ManagedBlockApplyResult Remove()
            {
                return new ManagedBlockApplyResult();
            }

            public string Preview(McpEditorSettings settings)
            {
                return "preview placeholder";
            }
        }
    }
}
