using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PluginDiscoveryTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "PluginDiscoveryTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Library"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_156.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_156")]
        public void SettingsValidation_RejectsEnabledAsmdefRegistrationWithoutAssemblyName()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pluginRegistrations.Clear();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = string.Empty
            });

            var validation = InvokeSettingsValidation(settings);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Message, Does.Contain("assemblyName"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_186.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_186")]
        public void SettingsMigration_AddsAllMissingDefaultPluginRegistrations()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pluginRegistrations.RemoveAll(registration =>
                registration != null &&
                registration.assemblyName != "UnityMcp.BuiltInPlugins.MonoBehaviourSemantics" &&
                registration.assemblyName != "UnityMcp.BuiltInPlugins.LuaTools");

            InvokeDefaultPluginMigration(settings);

            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.ProjectInfo"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.EditorBasics"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.UnityQueries"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.TestRunner"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.RoslynExecution"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.MonoBehaviourSemantics"), Is.True);
            Assert.That(settings.pluginRegistrations.Any(registration => registration.assemblyName == "UnityMcp.BuiltInPlugins.LuaTools"), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_157.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_157")]
        public void Discovery_RegistersConfiguredAsmdefPluginAndWritesCatalog()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(TestProjectProfileProvider).Assembly.GetName().Name,
                providerTypeName = typeof(TestProjectProfileProvider).FullName
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            Assert.That(registry.TryGetTool("unity.test.project_profile", out var tool), Is.True);
            Assert.That(tool, Is.Not.Null);
            Assert.That(File.Exists(paths.PluginCatalogPath), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.test.project_profile"), Is.True);
            var catalogJson = File.ReadAllText(paths.PluginCatalogPath);
            Assert.That(catalogJson, Does.Contain("\"mcpName\":\"mcp__unity__test_project_profile\""));
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_DisabledRegistrationRefresh_RemovesToolAndWritesEmptyCatalog()
        {
            var settings = CreatePluginOnlySettings();
            var registration = new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(TestProjectProfileProvider).Assembly.GetName().Name,
                providerTypeName = typeof(TestProjectProfileProvider).FullName
            };
            settings.pluginRegistrations.Add(registration);

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var firstRegistry = new AgentToolRegistry();
            firstRegistry.Discover();

            UnityMcpPluginRuntime.DiscoverAndRegister(firstRegistry, settings, paths, logger);

            Assert.That(firstRegistry.TryGetTool("unity.test.project_profile", out _), Is.True);
            Assert.That(File.ReadAllText(paths.PluginCatalogPath), Does.Contain("mcp__unity__test_project_profile"));

            registration.enabled = false;
            var refreshedRegistry = new AgentToolRegistry();
            refreshedRegistry.Discover();

            var refreshed = UnityMcpPluginRuntime.DiscoverAndRegister(refreshedRegistry, settings, paths, logger);

            Assert.That(refreshedRegistry.TryGetTool("unity.test.project_profile", out _), Is.False);
            Assert.That(refreshed.Catalog.tools, Is.Empty);
            var refreshedCatalogJson = File.ReadAllText(paths.PluginCatalogPath);
            Assert.That(refreshedCatalogJson, Does.Contain("\"tools\":[]"));
            Assert.That(refreshedCatalogJson, Does.Not.Contain("mcp__unity__test_project_profile"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_158.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_158")]
        public void Discovery_BuiltInConflict_KeepsBuiltInToolAndRejectsPluginTool()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(BuiltInConflictProvider).Assembly.GetName().Name,
                providerTypeName = typeof(BuiltInConflictProvider).FullName
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            Assert.That(registry.TryGetTool("unity.compile", out var builtInTool), Is.True);
            Assert.That(builtInTool.GetType().Name, Is.Not.EqualTo(nameof(UnityMcpPluginToolAdapter)));
            Assert.That(File.ReadAllText(paths.BridgeLogPath), Does.Contain("plugin_tool_conflict_builtin"));
            var catalogJson = File.ReadAllText(paths.PluginCatalogPath);
            Assert.That(catalogJson, Does.Not.Contain("mcp__unity__compile"));
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_ProjectInfoPlugin_OwnsPluginProjectInfoToolName()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.ProjectInfo",
                providerTypeName = "UnityMcp.BuiltInPlugins.ProjectInfo.ProjectInfoProvider"
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            Assert.That(registry.TryGetTool("unity.project.get_info", out var projectInfoTool), Is.True);
            Assert.That(projectInfoTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(registry.TryGetTool("unity.project_info", out _), Is.False);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.project.get_info" && item.mcpName == "mcp__unity__project_get_info"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.project_info"), Is.False);
            Assert.That(File.ReadAllText(paths.PluginCatalogPath), Does.Not.Contain("mcp__unity__project_info"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_164.md
        [Test]
        [Category("AGB_164")]
        [Category("AGB_Core")]
        public void Discovery_DefaultEditorBasicsPlugin_RegistersMigratedToolsAndWritesCatalog()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.EditorBasics",
                providerTypeName = "UnityMcp.BuiltInPlugins.EditorBasics.EditorBasicsProvider"
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            Assert.That(registry.TryGetTool("unity.ping", out var pingTool), Is.True);
            Assert.That(registry.TryGetTool("unity.get_console", out var consoleTool), Is.True);
            Assert.That(registry.TryGetTool("unity.get_editor_state", out var editorStateTool), Is.True);
            Assert.That(pingTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(consoleTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(editorStateTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.ping" && item.mcpName == "mcp__unity__ping"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.get_console" && item.mcpName == "mcp__unity__get_console"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.get_editor_state" && item.mcpName == "mcp__unity__get_editor_state"), Is.True);
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_UnityQueriesPlugin_RegistersQueryToolsAndExcludesOpenScene()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.UnityQueries",
                providerTypeName = "UnityMcp.BuiltInPlugins.UnityQueries.UnityQueriesProvider"
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            AssertPluginTool(registry, "unity.assetdatabase_search");
            AssertPluginTool(registry, "unity.get_hierarchy");
            AssertPluginTool(registry, "unity.get_gameobject_component_info");
            AssertPluginTool(registry, "unity.get_selection_info");
            AssertPluginTool(registry, "unity.read_report");
            Assert.That(registry.TryGetTool("unity.open_scene", out var openSceneTool), Is.True);
            Assert.That(openSceneTool, Is.Not.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.open_scene"), Is.False);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.assetdatabase_search" && item.mcpName == "mcp__unity__assetdatabase_search"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.get_hierarchy" && item.mcpName == "mcp__unity__get_hierarchy"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.get_gameobject_component_info" && item.mcpName == "mcp__unity__get_gameobject_component_info"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.get_selection_info" && item.mcpName == "mcp__unity__get_selection_info"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.read_report" && item.mcpName == "mcp__unity__read_report"), Is.True);
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_TestRunnerPlugin_RegistersTestToolsAndWritesCatalog()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.TestRunner",
                providerTypeName = "UnityMcp.BuiltInPlugins.TestRunner.TestRunnerProvider"
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger, new UnityMcpPluginHostServices
            {
                Settings = settings,
                Queue = new AgentCommandQueue(_projectRoot, settings.tempRoot),
                Registry = registry,
                Logger = logger
            });

            AssertPluginTool(registry, "unity.run_editmode_tests");
            AssertPluginTool(registry, "unity.run_playmode_tests");
            AssertPluginTool(registry, "unity.agent_bridge_self_test");
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.run_editmode_tests" && item.mcpName == "mcp__unity__run_editmode_tests"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.run_playmode_tests" && item.mcpName == "mcp__unity__run_playmode_tests"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.agent_bridge_self_test" && item.mcpName == "mcp__unity__agent_bridge_self_test"), Is.True);
        }

        [Test]
        [Category("AGB_Core")]
        [Category("AGB_169")]
        public void Discovery_MonoBehaviourSemanticsPlugin_RegistersGuidUsageToolAndWritesCatalog()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.MonoBehaviourSemantics",
                providerTypeName = "UnityMcp.BuiltInPlugins.MonoBehaviourSemantics.MonoBehaviourSemanticsProvider"
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            AssertPluginTool(registry, "unity.mono.find_script_guid_usages");
            Assert.That(result.Catalog.tools.Any(item =>
                item.bridgeTool == "unity.mono.find_script_guid_usages" &&
                item.mcpName == "mcp__unity__mono_find_script_guid_usages"), Is.True);
            Assert.That(result.Catalog.tools.Any(item =>
                item.bridgeTool == "unity.mono.find_script_guid_usages" &&
                item.description.Contains("MonoBehaviour script GUID")), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_159.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_159")]
        public void Discovery_DuplicatePluginToolName_IsRejectedDeterministically()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(FirstDuplicateProvider).Assembly.GetName().Name,
                providerTypeName = typeof(FirstDuplicateProvider).FullName
            });
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(SecondDuplicateProvider).Assembly.GetName().Name,
                providerTypeName = typeof(SecondDuplicateProvider).FullName
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, logger);

            Assert.That(registry.TryGetTool("unity.test.duplicate", out var tool), Is.True);
            Assert.That(((UnityMcpPluginToolAdapter)tool).Descriptor.Description, Is.EqualTo("First duplicate provider tool."));
            Assert.That(result.Catalog.tools.Count(item => item.bridgeTool == "unity.test.duplicate"), Is.EqualTo(1));
            Assert.That(File.ReadAllText(paths.BridgeLogPath), Does.Contain("plugin_tool_conflict_plugin"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_160.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_160")]
        public void Discovery_ResolvesAssetAndPackageSchemasIntoCatalog()
        {
            File.WriteAllText(
                Path.Combine(_projectRoot, "Assets", "plugin-asset.schema.json"),
                "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"additionalProperties\":false}");
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages", "com.vendor.example"));
            File.WriteAllText(
                Path.Combine(_projectRoot, "Packages", "com.vendor.example", "plugin-package.schema.json"),
                "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\"}},\"additionalProperties\":false}");

            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = typeof(PathSchemaProvider).Assembly.GetName().Name,
                providerTypeName = typeof(PathSchemaProvider).FullName
            });

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();

            UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));

            var catalogJson = File.ReadAllText(paths.PluginCatalogPath);
            Assert.That(catalogJson, Does.Contain("\"bridgeTool\":\"unity.test.asset_schema\""));
            Assert.That(catalogJson, Does.Contain("\"bridgeTool\":\"unity.test.package_schema\""));
            Assert.That(catalogJson, Does.Contain("\\\"name\\\":{\\\"type\\\":\\\"string\\\"}"));
            Assert.That(catalogJson, Does.Contain("\\\"count\\\":{\\\"type\\\":\\\"integer\\\"}"));
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_RoslynExecution_Disabled_DoesNotRegisterTool()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(CreateRoslynExecutionRegistration());
            WriteRoslynSettingsAsset(false);
            CreatePreparedRoslynCompilerPayload();

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));

            Assert.That(registry.TryGetTool("unity.execute_csharp", out _), Is.False);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.execute_csharp"), Is.False);
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_RoslynExecution_EnabledWithoutRuntime_DoesNotRegisterTool()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(CreateRoslynExecutionRegistration());
            WriteRoslynSettingsAsset(true);

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));

            Assert.That(registry.TryGetTool("unity.execute_csharp", out _), Is.False);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.execute_csharp"), Is.False);
        }

        [Test]
        [Category("AGB_Core")]
        public void Discovery_RoslynExecution_EnabledAndRuntimeReady_RegistersToolAndRejectsFullFileInput()
        {
            var settings = CreatePluginOnlySettings();
            settings.pluginRegistrations.Add(CreateRoslynExecutionRegistration());
            WriteRoslynSettingsAsset(true);
            CreatePreparedRoslynCompilerPayload();

            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            var result = UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));

            Assert.That(registry.TryGetTool("unity.execute_csharp", out var tool), Is.True);
            Assert.That(tool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.execute_csharp" && item.mcpName == "mcp__unity__execute_csharp"), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.execute_csharp" && item.allowedRuntimeModes == "Edit"), Is.True);

            var execution = tool.Execute(new AgentToolContext
            {
                Command = new AgentCommand
                {
                    commandId = "cmd-roslyn-validation",
                    tool = "unity.execute_csharp",
                    timeoutMs = 1000
                },
                RawArgsJson = "{\"code\":\"public static class Entry {}\",\"timeoutMs\":500}"
            }, NoOpAgentCancellation.Instance);

            Assert.That(execution.success, Is.False);
            Assert.That(execution.status, Is.EqualTo("validation_failed"));
            Assert.That(execution.summary, Does.Contain("__Run() method body"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_161.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_161")]
        public void PluginToolAdapter_NullPluginResult_ReturnsFailedToolResult()
        {
            var tool = new UnityMcpPluginToolAdapter(new NullResultTool(), _projectRoot, "Temp/AgentBridge");

            var result = tool.Execute(new AgentToolContext
            {
                Command = new AgentCommand
                {
                    commandId = "cmd-null",
                    tool = "unity.test.null_result",
                    timeoutMs = 1000
                },
                RawArgsJson = "{}"
            }, NoOpAgentCancellation.Instance);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(result.errors.Any(error => error.code == "UNITYMCP_PLUGIN_RESULT_NULL"), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_162.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_162")]
        public void PluginToolAdapter_NormalizesStatusSummaryAndInvalidMetrics()
        {
            var tool = new UnityMcpPluginToolAdapter(new InvalidMetricsTool(), _projectRoot, "Temp/AgentBridge");

            var result = tool.Execute(new AgentToolContext
            {
                Command = new AgentCommand
                {
                    commandId = "cmd-metrics",
                    tool = "unity.test.invalid_metrics",
                    timeoutMs = 1000
                },
                RawArgsJson = "{}"
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.success, Is.True);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.summary, Is.EqualTo("Plugin tool completed."));
            Assert.That(result.metricsObjectJson, Is.EqualTo("{}"));
            Assert.That(result.warnings.Any(warning => warning.code == "UNITYMCP_PLUGIN_METRICS_INVALID"), Is.True);
        }

        private static (bool IsValid, string Message) InvokeSettingsValidation(AgentBridgeSettings settings)
        {
            var method = typeof(AgentBridgeSettingsLoader).GetMethod("TryValidate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var parameters = new object[] { settings, null };
            var isValid = (bool)method.Invoke(null, parameters);
            return (isValid, parameters[1] as string);
        }

        private static void InvokeDefaultPluginMigration(AgentBridgeSettings settings)
        {
            var method = typeof(AgentBridgeSettingsLoader).GetMethod("EnsureDefaultPluginRegistrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { settings });
        }

        private static void AssertPluginTool(AgentToolRegistry registry, string toolName)
        {
            Assert.That(registry.TryGetTool(toolName, out var tool), Is.True, toolName);
            Assert.That(tool, Is.TypeOf<UnityMcpPluginToolAdapter>(), toolName);
        }

        private AgentBridgeSettings CreatePluginOnlySettings()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pluginRegistrations.Clear();
            return settings;
        }

        private UnityMcpPluginRegistration CreateRoslynExecutionRegistration()
        {
            return new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.RoslynExecution",
                providerTypeName = "UnityMcp.BuiltInPlugins.RoslynExecution.RoslynExecutionProvider"
            };
        }

        private void WriteRoslynSettingsAsset(bool enabled)
        {
            var settingsDirectory = Path.Combine(_projectRoot, "Assets", "Settings");
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(
                Path.Combine(settingsDirectory, "AgentBridgeSettings.asset"),
                "%YAML 1.1\nroslynExecutionEnabled: " + (enabled ? "1" : "0") + "\n");
        }

        private void CreatePreparedRoslynCompilerPayload()
        {
            var compilerPath = Path.Combine(
                _projectRoot,
                ".unitymcp",
                "runtime",
                "UnityAgentBridge",
                "roslyn-execution",
                "out",
                "win-x64",
                "unity-roslyn-compiler.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(compilerPath) ?? _projectRoot);
            File.WriteAllText(compilerPath, "stub");
        }

        [UnityMcpPlugin("Test.ProjectProfile", "1.0.0")]
        private sealed class TestProjectProfileProvider : IUnityMcpToolProvider
        {
            public System.Collections.Generic.IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
            {
                return new IUnityMcpTool[]
                {
                    new InlineSchemaTool("unity.test.project_profile", "Project profile test tool.")
                };
            }
        }

        [UnityMcpPlugin("Test.BuiltInConflict", "1.0.0")]
        private sealed class BuiltInConflictProvider : IUnityMcpToolProvider
        {
            public System.Collections.Generic.IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
            {
                return new IUnityMcpTool[]
                {
                    new InlineSchemaTool("unity.compile", "Plugin should not override built-in.")
                };
            }
        }

        [UnityMcpPlugin("Test.Duplicate.First", "1.0.0")]
        private sealed class FirstDuplicateProvider : IUnityMcpToolProvider
        {
            public System.Collections.Generic.IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
            {
                return new IUnityMcpTool[]
                {
                    new InlineSchemaTool("unity.test.duplicate", "First duplicate provider tool.")
                };
            }
        }

        [UnityMcpPlugin("Test.Duplicate.Second", "1.0.0")]
        private sealed class SecondDuplicateProvider : IUnityMcpToolProvider
        {
            public System.Collections.Generic.IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
            {
                return new IUnityMcpTool[]
                {
                    new InlineSchemaTool("unity.test.duplicate", "Second duplicate provider tool.")
                };
            }
        }

        [UnityMcpPlugin("Test.PathSchemas", "1.0.0")]
        private sealed class PathSchemaProvider : IUnityMcpToolProvider
        {
            public System.Collections.Generic.IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
            {
                return new IUnityMcpTool[]
                {
                    new SchemaPathTool(
                        "unity.test.asset_schema",
                        "Asset schema tool.",
                        new UnityMcpSchemaDeclaration
                        {
                            Kind = UnityMcpSchemaKind.AssetPath,
                            Value = "Assets/plugin-asset.schema.json"
                        }),
                    new SchemaPathTool(
                        "unity.test.package_schema",
                        "Package schema tool.",
                        new UnityMcpSchemaDeclaration
                        {
                            Kind = UnityMcpSchemaKind.PackagePath,
                            Value = "Packages/com.vendor.example/plugin-package.schema.json"
                        })
                };
            }
        }

        private sealed class InlineSchemaTool : IUnityMcpTool
        {
            public InlineSchemaTool(string name, string description)
            {
                Descriptor = new UnityMcpToolDescriptor
                {
                    Name = name,
                    Title = name,
                    Description = description,
                    DefaultTimeoutMs = 1000,
                    AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
                    SideEffect = UnityMcpToolSideEffect.ReadsProject,
                    MayTriggerDomainReload = false
                };
            }

            public UnityMcpToolDescriptor Descriptor { get; }

            public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.InlineJson,
                Value = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            };

            public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
            {
                return new UnityMcpToolResult
                {
                    Success = true,
                    Status = UnityMcpToolStatus.Success,
                    Summary = "ok"
                };
            }
        }

        private sealed class SchemaPathTool : IUnityMcpTool
        {
            public SchemaPathTool(string name, string description, UnityMcpSchemaDeclaration schema)
            {
                Descriptor = new UnityMcpToolDescriptor
                {
                    Name = name,
                    Title = name,
                    Description = description,
                    DefaultTimeoutMs = 1000,
                    AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
                    SideEffect = UnityMcpToolSideEffect.ReadsProject,
                    MayTriggerDomainReload = false
                };
                InputSchema = schema;
            }

            public UnityMcpToolDescriptor Descriptor { get; }

            public UnityMcpSchemaDeclaration InputSchema { get; }

            public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
            {
                return new UnityMcpToolResult
                {
                    Success = true,
                    Status = UnityMcpToolStatus.Success,
                    Summary = "ok"
                };
            }
        }

        private sealed class NullResultTool : IUnityMcpTool
        {
            public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
            {
                Name = "unity.test.null_result",
                Title = "Null Result",
                Description = "Returns null to verify adapter normalization.",
                DefaultTimeoutMs = 1000,
                AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
                SideEffect = UnityMcpToolSideEffect.ReadsProject,
                MayTriggerDomainReload = false
            };

            public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.InlineJson,
                Value = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            };

            public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
            {
                return null;
            }
        }

        private sealed class InvalidMetricsTool : IUnityMcpTool
        {
            public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
            {
                Name = "unity.test.invalid_metrics",
                Title = "Invalid Metrics",
                Description = "Returns invalid metrics to verify adapter normalization.",
                DefaultTimeoutMs = 1000,
                AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
                SideEffect = UnityMcpToolSideEffect.ReadsProject,
                MayTriggerDomainReload = false
            };

            public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.InlineJson,
                Value = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            };

            public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
            {
                return new UnityMcpToolResult
                {
                    Success = true,
                    MetricsObjectJson = "[]"
                };
            }
        }
    }
}
