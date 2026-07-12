using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityMcp.BuiltInPlugins.LuaTools;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class LuaToolsPluginTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "LuaToolsPluginTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets", "Lua"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets", "Settings"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Library"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Temp", "AgentBridge"));
            PrepareLuaRuntimePayload();
        }

        [TearDown]
        public void TearDown()
        {
            LuaToolsProcess.TestRunnerOverride = null;
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_181.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_181")]
        public void LuaTools_Discovery_WhenRuntimeReady_RegistersLintAndCompileCatalogEntries()
        {
            var registry = DiscoverLuaTools();

            Assert.That(registry.TryGetTool("unity.lua.lint", out var lintTool), Is.True);
            Assert.That(registry.TryGetTool("unity.lua.compile", out var compileTool), Is.True);
            Assert.That(lintTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            Assert.That(compileTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            AssertDefaultSettingsIncludeLuaToolsWithoutDefaultRoots();
            AssertLuaToolsAsmdefDoesNotReferenceEditorInternals();
            Assert.That(lintTool.Descriptor.AllowedModes, Is.EqualTo(ToolExecutionModes.Edit | ToolExecutionModes.Play));
            Assert.That(lintTool.Descriptor.SideEffect, Is.EqualTo(ToolSideEffect.ReadsProject));
            Assert.That(lintTool.Descriptor.MayTriggerDomainReload, Is.False);
            Assert.That(compileTool.Descriptor.AllowedModes, Is.EqualTo(ToolExecutionModes.Edit | ToolExecutionModes.Play));
            Assert.That(compileTool.Descriptor.SideEffect, Is.EqualTo(ToolSideEffect.ReadsProject));
            Assert.That(compileTool.Descriptor.MayTriggerDomainReload, Is.False);
            var catalogJson = File.ReadAllText(GetPluginCatalogPath());
            Assert.That(catalogJson, Does.Contain("\"bridgeTool\":\"unity.lua.lint\""));
            Assert.That(catalogJson, Does.Contain("\"mcpName\":\"mcp__unity__lua_lint\""));
            Assert.That(catalogJson, Does.Contain("\"bridgeTool\":\"unity.lua.compile\""));
            Assert.That(catalogJson, Does.Contain("\"mcpName\":\"mcp__unity__lua_compile\""));
            Assert.That(catalogJson, Does.Contain("\"inputSchemaJson\""));
            Assert.That(catalogJson, Does.Contain("\\\"path\\\""));
            Assert.That(catalogJson, Does.Contain("\\\"checks\\\""));
            Assert.That(catalogJson, Does.Contain("\\\"failOn\\\""));
            Assert.That(catalogJson, Does.Contain("\\\"timeoutMs\\\""));
            Assert.That(catalogJson, Does.Contain("\\\"limit\\\""));
            Assert.That(catalogJson, Does.Contain("\\\"offset\\\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_182.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_182")]
        public void LuaTools_Discovery_WhenRuntimeMissing_HidesToolsAndPreservesOtherPluginRegistrations()
        {
            File.Delete(GetPreparedLuaLinterPath());

            var settings = CreateLuaAndProjectInfoSettings();
            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();

            UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));

            Assert.That(registry.TryGetTool("unity.lua.lint", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.lua.compile", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.project.get_info", out var projectInfoTool), Is.True);
            Assert.That(projectInfoTool, Is.TypeOf<UnityMcpPluginToolAdapter>());
            var catalogJson = File.ReadAllText(GetPluginCatalogPath());
            Assert.That(catalogJson, Does.Not.Contain("\"bridgeTool\":\"unity.lua.lint\""));
            Assert.That(catalogJson, Does.Contain("\"bridgeTool\":\"unity.project.get_info\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_183.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_183")]
        public void LuaTools_Lint_ValidatesArgsAndRunsApprovedPayloadWithReports()
        {
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "Lua", "ok.lua"), "local x = 1");
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages", "com.example.lua", "Runtime"));
            File.WriteAllText(Path.Combine(_projectRoot, "Packages", "com.example.lua", "Runtime", "ok.lua"), "local y = 2");
            var tool = GetLuaTool("unity.lua.lint");

            var success = Execute(tool, "unity.lua.lint", "cmd-lua-lint-ok", "{\"path\":\"Assets/Lua/ok.lua\",\"checks\":[\"gc\"],\"failOn\":\"error\",\"limit\":10}");
            var packagesPath = Execute(tool, "unity.lua.lint", "cmd-lua-lint-packages", "{\"path\":\"Packages/com.example.lua/Runtime/ok.lua\",\"checks\":[]}");
            var omittedChecks = Execute(tool, "unity.lua.lint", "cmd-lua-lint-omitted-checks", "{\"path\":\"Assets/Lua/ok.lua\"}");
            var duplicateChecks = Execute(tool, "unity.lua.lint", "cmd-lua-lint-duplicate-checks", "{\"path\":\"Assets/Lua/ok.lua\",\"checks\":[\"gc\",\"gc\"],\"failOn\":\"warning\",\"offset\":0}");
            var missingPath = Execute(tool, "unity.lua.lint", "cmd-lua-lint-missing-path", "{}");
            var emptyPath = Execute(tool, "unity.lua.lint", "cmd-lua-lint-empty-path", "{\"path\":\"\"}");
            var absolutePath = Execute(tool, "unity.lua.lint", "cmd-lua-lint-absolute", "{\"path\":\"" + EscapeJson(Path.Combine(_projectRoot, "Assets", "Lua", "ok.lua")) + "\"}");
            var traversal = Execute(tool, "unity.lua.lint", "cmd-lua-lint-traversal", "{\"path\":\"Assets/../Packages\"}");
            var outsideProject = Execute(tool, "unity.lua.lint", "cmd-lua-lint-outside", "{\"path\":\"ProjectSettings\"}");
            var unsupportedCheck = Execute(tool, "unity.lua.lint", "cmd-lua-lint-check", "{\"path\":\"Assets/Lua/ok.lua\",\"checks\":[\"style\"]}");
            var unsupportedFailOn = Execute(tool, "unity.lua.lint", "cmd-lua-lint-failon", "{\"path\":\"Assets/Lua/ok.lua\",\"failOn\":\"info\"}");
            var invalidLimit = Execute(tool, "unity.lua.lint", "cmd-lua-lint-limit", "{\"path\":\"Assets/Lua/ok.lua\",\"limit\":501}");
            var invalidOffset = Execute(tool, "unity.lua.lint", "cmd-lua-lint-offset", "{\"path\":\"Assets/Lua/ok.lua\",\"offset\":-1}");

            Assert.That(success.success, Is.True);
            Assert.That(success.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(success.reportPath, Does.StartWith("Temp/AgentBridge/reports/lua_lint_"));
            Assert.That(File.Exists(GetAbsolutePath(success.reportPath)), Is.True);
            var reportJson = File.ReadAllText(GetAbsolutePath(success.reportPath));
            Assert.That(reportJson, Does.Contain("\"operation\":\"lint\""));
            Assert.That(packagesPath.success, Is.True);
            Assert.That(omittedChecks.metricsObjectJson, Does.Contain("\"effectiveChecks\":[\"gc\"]"));
            Assert.That(duplicateChecks.metricsObjectJson, Does.Contain("\"effectiveChecks\":[\"gc\"]"));
            Assert.That(duplicateChecks.metricsObjectJson, Does.Contain("\"failOn\":\"warning\""));
            Assert.That(missingPath.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(emptyPath.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(absolutePath.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(traversal.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(outsideProject.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(unsupportedCheck.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(unsupportedFailOn.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(invalidLimit.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(invalidOffset.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
        }

        [Test]
        [Category("AGB_Core")]
        [Category("AGB_183")]
        public void LuaTools_Lint_MapsProcessFailuresAndPreservesDiagnostics()
        {
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "Lua", "bad.lua"), "local =");
            var tool = GetLuaTool("unity.lua.lint");

            var lintFailed = Execute(tool, "unity.lua.lint", "cmd-lua-lint-bad", "{\"path\":\"Assets/Lua/bad.lua\",\"limit\":1}");
            var lintReport = File.ReadAllText(GetAbsolutePath(lintFailed.reportPath));
            Assert.That(lintFailed.success, Is.False);
            Assert.That(lintFailed.status, Is.EqualTo("lint_failed"));
            Assert.That(lintFailed.errors.Any(error => error.code == "R000"), Is.True);
            Assert.That(lintFailed.metricsObjectJson, Does.Contain("\"diagnosticCount\":1"));
            Assert.That(lintFailed.metricsObjectJson, Does.Contain("\"errorCount\":1"));
            Assert.That(lintReport, Does.Contain("\"file\""));
            Assert.That(lintReport, Does.Contain("\"line\""));
            Assert.That(lintReport, Does.Contain("\"column\""));
            Assert.That(lintReport, Does.Contain("\"rule\":\"R000\""));
            Assert.That(lintReport, Does.Contain("\"severity\""));
            Assert.That(lintReport, Does.Contain("\"message\""));
            Assert.That(lintReport, Does.Contain("\"suggestion\""));

            LuaToolsProcess.TestRunnerOverride = (_, _, _, _) => new LuaToolsProcessResult
            {
                ExitCode = 2,
                DurationMs = 7,
                Stdout = "[]",
                Stderr = "synthetic tool failure"
            };
            var toolFailed = Execute(tool, "unity.lua.lint", "cmd-lua-lint-tool-failed", "{\"path\":\"Assets/Lua/bad.lua\"}");
            Assert.That(toolFailed.status, Is.EqualTo("tool_failed"));
            Assert.That(toolFailed.summary, Does.Contain("synthetic tool failure"));

            LuaToolsProcess.TestRunnerOverride = (_, _, _, _) => new LuaToolsProcessResult
            {
                ExitCode = 0,
                DurationMs = 7,
                Stdout = "not-json",
                Stderr = "protocol stderr"
            };
            var invalidJson = Execute(tool, "unity.lua.lint", "cmd-lua-lint-invalid-json", "{\"path\":\"Assets/Lua/bad.lua\"}");
            Assert.That(invalidJson.status, Is.EqualTo("tool_failed"));
            Assert.That(File.ReadAllText(GetAbsolutePath(invalidJson.reportPath)), Does.Contain("protocol stderr"));

            LuaToolsProcess.TestRunnerOverride = (_, _, _, _) => new LuaToolsProcessResult
            {
                TimedOut = true,
                DurationMs = 100,
                Stdout = "[]",
                Stderr = "partial stderr"
            };
            var timeout = Execute(tool, "unity.lua.lint", "cmd-lua-lint-timeout", "{\"path\":\"Assets/Lua/bad.lua\"}");
            Assert.That(timeout.status, Is.EqualTo(ToolResultStatus.Timeout));
            Assert.That(timeout.metricsObjectJson, Does.Contain("\"timeout\":true"));
        }

        [Test]
        [Category("AGB_Core")]
        [Category("AGB_183")]
        public void LuaTools_Lint_PreservesV1EvidenceFieldsAndKeepsInputSchemaStable()
        {
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "Lua", "v1.lua"), "local value = 1");
            var tool = GetLuaTool("unity.lua.lint");

            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"path\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"checks\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"failOn\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"timeoutMs\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"limit\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Contain("\"offset\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Not.Contain("\"command\""));
            Assert.That(LuaToolsSchemas.Lint, Does.Not.Contain("\"format\""));

            LuaToolsProcess.TestRunnerOverride = (_, _, _, _) => new LuaToolsProcessResult
            {
                ExitCode = 0,
                DurationMs = 7,
                Stdout = "[{\"file\":\"Assets/Lua/v1.lua\",\"line\":1,\"column\":1,\"rule\":\"LUA_GC_002\",\"ruleId\":\"LUA_GC_002\",\"legacyRule\":\"R002\",\"severity\":\"warning\",\"function\":\"update\",\"message\":\"closure allocation\",\"evidence\":\"function() end\",\"suggestion\":\"cache callback\",\"confidence\":\"High\"}]",
                Stderr = string.Empty
            };

            var result = Execute(tool, "unity.lua.lint", "cmd-lua-lint-v1-evidence", "{\"path\":\"Assets/Lua/v1.lua\"}");
            var reportJson = File.ReadAllText(GetAbsolutePath(result.reportPath));

            Assert.That(result.success, Is.True);
            Assert.That(result.metricsObjectJson, Does.Contain("\"ruleId\":\"LUA_GC_002\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"legacyRule\":\"R002\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"function\":\"update\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"evidence\":\"function() end\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"confidence\":\"High\""));
            Assert.That(reportJson, Does.Contain("\"ruleId\":\"LUA_GC_002\""));
            Assert.That(reportJson, Does.Contain("\"legacyRule\":\"R002\""));
            Assert.That(reportJson, Does.Contain("\"function\":\"update\""));
            Assert.That(reportJson, Does.Contain("\"evidence\":\"function() end\""));
            Assert.That(reportJson, Does.Contain("\"confidence\":\"High\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_184.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_184")]
        public void LuaTools_Compile_MapsParseDiagnosticsAndSupportsConfiguredRootsWithoutPersistentOutput()
        {
            var okPath = Path.Combine(_projectRoot, "Assets", "Lua", "ok.lua");
            var badPath = Path.Combine(_projectRoot, "Assets", "Lua", "bad.lua");
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets", "Lua", "Sub"));
            File.WriteAllText(okPath, "local x = 1");
            File.WriteAllText(badPath, "local =");
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "Lua", "Sub", "ok.lua"), "local z = 3");
            WriteAgentBridgeSettingsAsset("Assets/Lua");
            var beforeFiles = Directory.GetFiles(Path.Combine(_projectRoot, "Assets", "Lua"), "*", SearchOption.AllDirectories).Length;
            var tool = GetLuaTool("unity.lua.compile");

            var explicitBad = Execute(tool, "unity.lua.compile", "cmd-lua-compile-bad", "{\"path\":\"Assets/Lua/bad.lua\",\"limit\":10}");
            var explicitDirectory = Execute(tool, "unity.lua.compile", "cmd-lua-compile-dir", "{\"path\":\"Assets/Lua\",\"limit\":10}");
            var configuredRoot = Execute(tool, "unity.lua.compile", "cmd-lua-compile-root", "{\"limit\":10}");
            File.Delete(Path.Combine(_projectRoot, "Assets", "Settings", "AgentBridgeSettings.asset"));
            var missingRoots = Execute(tool, "unity.lua.compile", "cmd-lua-compile-missing-roots", "{}");
            File.Delete(badPath);
            var explicitOk = Execute(tool, "unity.lua.compile", "cmd-lua-compile-ok", "{\"path\":\"Assets/Lua/ok.lua\"}");
            var directoryOk = Execute(tool, "unity.lua.compile", "cmd-lua-compile-dir-ok", "{\"path\":\"Assets/Lua\"}");
            var afterFiles = Directory.GetFiles(Path.Combine(_projectRoot, "Assets", "Lua"), "*", SearchOption.AllDirectories).Length;

            Assert.That(explicitBad.success, Is.False);
            Assert.That(explicitBad.status, Is.EqualTo("compile_failed"));
            Assert.That(explicitBad.errors.Any(error => error.code == "R000"), Is.True);
            Assert.That(explicitBad.metricsObjectJson, Does.Contain("\"parserDialect\":\"lua-gc-lint parser\""));
            Assert.That(File.ReadAllText(GetAbsolutePath(explicitBad.reportPath)), Does.Contain("\"rule\":\"R000\""));
            Assert.That(explicitDirectory.status, Is.EqualTo("compile_failed"));
            Assert.That(configuredRoot.status, Is.EqualTo("compile_failed"));
            Assert.That(configuredRoot.metricsObjectJson, Does.Contain("Assets/Lua"));
            Assert.That(missingRoots.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(explicitOk.success, Is.True);
            Assert.That(explicitOk.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(directoryOk.success, Is.True);
            Assert.That(afterFiles, Is.EqualTo(beforeFiles - 1));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_185.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_185")]
        public void LuaTools_RuntimePayloadBoundary_IsPreparedAndDeliveryApproved()
        {
            var preparedPath = GetPreparedLuaLinterPath();
            var packagePath = GetPackageLuaLinterPath();
            var deliveryPath = Path.Combine(
                GetRepoRoot(),
                "Build",
                "AgentBridge-PackageDistribution",
                "package",
                "com.unitymcp.agent-bridge",
                "Tools~",
                "UnityAgentBridge",
                "lua-gc-lint",
                "out",
                "win-x64",
                "lua-gc-lint.exe");

            Assert.That(File.Exists(packagePath), Is.True);
            Assert.That(File.Exists(preparedPath), Is.True);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(packagePath), "*.exe").Select(Path.GetFileName), Is.EquivalentTo(new[] { "lua-gc-lint.exe" }));
            if (File.Exists(deliveryPath))
            {
                Assert.That(new FileInfo(deliveryPath).Length, Is.EqualTo(new FileInfo(packagePath).Length));
            }
        }

        private static void AssertDefaultSettingsIncludeLuaToolsWithoutDefaultRoots()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            Assert.That(settings.pluginRegistrations.Any(registration =>
                registration.enabled &&
                registration.kind == UnityMcpPluginRegistrationKind.AsmdefAssembly &&
                registration.assemblyName == "UnityMcp.BuiltInPlugins.LuaTools" &&
                registration.providerTypeName == "UnityMcp.BuiltInPlugins.LuaTools.LuaToolsProvider"), Is.True);
            Assert.That(settings.luaSourceRoots, Is.Empty);
        }

        private static void AssertLuaToolsAsmdefDoesNotReferenceEditorInternals()
        {
            var asmdefJson = File.ReadAllText(Path.Combine(GetPackageRoot(), "BuiltInPlugins", "LuaTools", "Editor", "UnityMcp.BuiltInPlugins.LuaTools.asmdef"));
            Assert.That(asmdefJson, Does.Contain("UnityMcp.Plugin.Abstractions"));
            Assert.That(asmdefJson, Does.Not.Contain("UnityMcp.AgentBridge.Editor"));
            Assert.That(asmdefJson, Does.Not.Contain("UnityMcp.AgentBridge.SharedProtocolCore"));
        }

        private AgentToolRegistry DiscoverLuaTools()
        {
            var settings = CreateLuaOnlySettings();
            var paths = new AgentBridgePaths(_projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();
            UnityMcpPluginRuntime.DiscoverAndRegister(registry, settings, paths, new FileAgentBridgeLogger(paths.BridgeLogPath));
            return registry;
        }

        private IAgentTool GetLuaTool(string toolName)
        {
            var registry = DiscoverLuaTools();
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

        private AgentBridgeSettings CreateLuaOnlySettings()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pluginRegistrations.Clear();
            settings.pluginRegistrations.Add(CreateLuaToolsRegistration());
            return settings;
        }

        private AgentBridgeSettings CreateLuaAndProjectInfoSettings()
        {
            var settings = CreateLuaOnlySettings();
            settings.pluginRegistrations.Add(new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.ProjectInfo",
                providerTypeName = "UnityMcp.BuiltInPlugins.ProjectInfo.ProjectInfoProvider"
            });
            return settings;
        }

        private static UnityMcpPluginRegistration CreateLuaToolsRegistration()
        {
            return new UnityMcpPluginRegistration
            {
                enabled = true,
                kind = UnityMcpPluginRegistrationKind.AsmdefAssembly,
                assemblyName = "UnityMcp.BuiltInPlugins.LuaTools",
                providerTypeName = "UnityMcp.BuiltInPlugins.LuaTools.LuaToolsProvider"
            };
        }

        private void PrepareLuaRuntimePayload()
        {
            var targetPath = GetPreparedLuaLinterPath();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.Copy(GetPackageLuaLinterPath(), targetPath, true);
        }

        private void WriteAgentBridgeSettingsAsset(string luaSourceRoot)
        {
            File.WriteAllText(
                Path.Combine(_projectRoot, "Assets", "Settings", "AgentBridgeSettings.asset"),
                "luaSourceRoots:\n- " + luaSourceRoot + "\n");
        }

        private string GetPreparedLuaLinterPath()
        {
            return Path.Combine(_projectRoot, ".unitymcp", "runtime", "UnityAgentBridge", "lua-gc-lint", "out", "win-x64", "lua-gc-lint.exe");
        }

        private static string GetPackageLuaLinterPath()
        {
            return Path.Combine(GetPackageRoot(), "Tools~", "UnityAgentBridge", "lua-gc-lint", "out", "win-x64", "lua-gc-lint.exe");
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string GetPluginCatalogPath()
        {
            return Path.Combine(_projectRoot, "Library", "AgentBridge", "plugin-catalog.json");
        }

        private string GetAbsolutePath(string projectRelativePath)
        {
            return Path.Combine(_projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetPackageRoot()
        {
            return Path.GetFullPath(Path.Combine(GetRepoRoot(), "..", "unity-agent-bridge", "com.unitymcp.agent-bridge"));
        }

        private static string GetRepoRoot()
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "UnityMCP")) &&
                    Directory.Exists(Path.Combine(current.FullName, "openspec")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not resolve unity-agent-bridge-workbench repository root.");
        }
    }
}
