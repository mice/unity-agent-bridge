using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class StandaloneLuaPluginIntegrationTests
    {
        private const string PluginId = "com.unitymcp.lua-gc-lint";
        private string _projectRoot;
        private AgentBridgeSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), nameof(StandaloneLuaPluginIntegrationTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets", "Lua"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Library"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "ProjectSettings"));

            _settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            _settings.pluginRegistrations.Clear();
            _settings.externalPluginStates.Clear();
            RequireInstalledPackage();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
            }

            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_181.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_181")]
        public void StandaloneLuaPlugin_InstalledPackage_DefaultsToDisabledReady()
        {
            var plugin = FindPlugin();

            Assert.That(plugin.Installed, Is.True);
            Assert.That(plugin.Enabled, Is.False);
            Assert.That(plugin.Ready, Is.True);
            Assert.That(plugin.Exposed, Is.False);
            Assert.That(plugin.PackageRoot, Does.Contain("com.unitymcp.lua-gc-lint"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_182.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_182")]
        public void StandaloneLuaPlugin_ExplicitEnable_ExposesOnlyExternalCatalogTools()
        {
            EnablePlugin();
            var result = DiscoverAndRegister(out var registry);
            var plugin = result.InstalledPlugins.Single(item => item.PluginId == PluginId);
            var luaTools = result.Catalog.tools.Where(item => item.pluginId == PluginId).ToArray();

            Assert.That(plugin.Enabled && plugin.Ready && plugin.Exposed, Is.True);
            Assert.That(luaTools.Select(item => item.bridgeTool), Is.EquivalentTo(new[] { "unity.lua.lint", "unity.lua.compile" }));
            Assert.That(luaTools.Select(item => item.mcpName), Is.EquivalentTo(new[] { "unity_lua_lint", "unity_lua_compile" }));
            Assert.That(luaTools.All(item => item.assemblyName == "UnityMcp.LuaGcLint.Plugin"), Is.True);
            Assert.That(registry.TryGetTool("unity.lua.lint", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.lua.compile", out _), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_183.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_183")]
        public void StandaloneLuaPlugin_Lint_ExecutesBinaryPackagePayload()
        {
            var luaPath = WriteLua("valid.lua", "local value = 42\nreturn value\n");
            var tool = GetTool("unity.lua.lint");

            var result = Execute(tool, "unity.lua.lint", "agb-183", "{\"path\":\"" + luaPath + "\",\"checks\":[\"gc\"],\"failOn\":\"error\"}");

            Assert.That(result.success, Is.True, result.summary);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            AssertReportUsesPackagePayload(result);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_184.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_184")]
        public void StandaloneLuaPlugin_Compile_UsesPluginOwnedSourceRoots()
        {
            WriteLua("compile.lua", "local function answer()\n    return 42\nend\nreturn answer()\n");
            File.WriteAllText(
                Path.Combine(_projectRoot, "ProjectSettings", "UnityMcpLuaGcLintSettings.json"),
                "{\"luaSourceRoots\":[\"Assets/Lua\"]}");
            var tool = GetTool("unity.lua.compile");

            var result = Execute(tool, "unity.lua.compile", "agb-184", "{}");

            Assert.That(result.success, Is.True, result.summary);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("Assets/Lua"));
            AssertReportUsesPackagePayload(result);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_185.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_185")]
        public void StandaloneLuaPlugin_Invocation_DoesNotCopyExecutableIntoProject()
        {
            var luaPath = WriteLua("no-copy.lua", "return 42\n");
            var tool = GetTool("unity.lua.compile");

            var result = Execute(tool, "unity.lua.compile", "agb-185", "{\"path\":\"" + luaPath + "\"}");
            var projectCopies = Directory.GetFiles(_projectRoot, "lua-gc-lint.exe", SearchOption.AllDirectories);

            Assert.That(result.success, Is.True, result.summary);
            Assert.That(projectCopies, Is.Empty);
            AssertReportUsesPackagePayload(result);
        }

        private void RequireInstalledPackage()
        {
            if (UnityMcpPluginRuntime.DiscoverInstalledPlugins(_settings).All(item => item.PluginId != PluginId))
            {
                Assert.Ignore("com.unitymcp.lua-gc-lint is not installed in this Unity validation project.");
            }
        }

        private UnityMcpInstalledPlugin FindPlugin()
        {
            return UnityMcpPluginRuntime.DiscoverInstalledPlugins(_settings).Single(item => item.PluginId == PluginId);
        }

        private void EnablePlugin()
        {
            UnityMcpExternalPluginLifecycle.SetEnabled(_settings, PluginId, true);
        }

        private UnityMcpPluginDiscoveryResult DiscoverAndRegister(out AgentToolRegistry registry)
        {
            var paths = new AgentBridgePaths(_projectRoot, _settings);
            paths.EnsureDirectories();
            registry = new AgentToolRegistry();
            registry.Discover();
            return UnityMcpPluginRuntime.DiscoverAndRegister(registry, _settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));
        }

        private IAgentTool GetTool(string toolName)
        {
            EnablePlugin();
            DiscoverAndRegister(out var registry);
            Assert.That(registry.TryGetTool(toolName, out var tool), Is.True, toolName);
            return tool;
        }

        private static ToolResult Execute(IAgentTool tool, string toolName, string commandId, string rawArgsJson)
        {
            return tool.Execute(new AgentToolContext
            {
                Command = new AgentCommand
                {
                    commandId = commandId,
                    tool = toolName,
                    timeoutMs = 30000
                },
                RawArgsJson = rawArgsJson
            }, NoOpAgentCancellation.Instance);
        }

        private string WriteLua(string fileName, string contents)
        {
            var absolutePath = Path.Combine(_projectRoot, "Assets", "Lua", fileName);
            File.WriteAllText(absolutePath, contents);
            return "Assets/Lua/" + fileName;
        }

        private void AssertReportUsesPackagePayload(ToolResult result)
        {
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            var report = File.ReadAllText(Path.Combine(_projectRoot, result.reportPath.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedPackageRoot = FindPlugin().PackageRoot.Replace('\\', '/');
            Assert.That(report, Does.Contain("lua-gc-lint.exe"));
            Assert.That(report.Replace("\\\\", "/").Replace('\\', '/'), Does.Contain(normalizedPackageRoot));
        }
    }
}
