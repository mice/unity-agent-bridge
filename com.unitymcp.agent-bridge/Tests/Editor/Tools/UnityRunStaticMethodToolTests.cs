using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityRunStaticMethodToolTests
    {
        private readonly List<string> _reportPaths = new List<string>();

        [SetUp]
        public void SetUp()
        {
            AgentBridgeStaticMethodSelfTests.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var reportPath in _reportPaths)
            {
                if (string.IsNullOrWhiteSpace(reportPath))
                {
                    continue;
                }

                var absolutePath = GetReportAbsolutePath(reportPath);
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }

            _reportPaths.Clear();
            AgentBridgeStaticMethodSelfTests.Reset();
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_049.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_049")]
        public void UnityRunStaticMethodTool_WhitelistMiss_ReturnsInvalidArgs()
        {
            var tool = CreateTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.static.049",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"NotWhitelisted\"}",
                    CreateSettings()),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_STATIC_METHOD_NOT_ALLOWED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_050.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_050")]
        public void UnityRunStaticMethodTool_NoArgWhitelistHit_ReturnsSuccessAndTimeoutWarning()
        {
            var assetOps = new FakeAssetDatabaseOps();
            var settings = CreateSettings();
            settings.maxToolDurationMs = 3000;
            settings.allowedStaticMethods[0].maxDurationMs = 1200;
            var tool = CreateTool(assetOps);

            var result = tool.Execute(
                CreateContext(
                    "agb.static.050",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestOk\"}",
                    settings,
                    timeoutMs: 2000),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.warnings, Has.Some.Matches<ToolWarning>(warning => warning.code == "AGENTBRIDGE_TIMEOUT_TRUNCATED"));
            Assert.That(assetOps.RefreshCalls, Is.Zero);
            Assert.That(assetOps.SaveAssetsCalls, Is.Zero);
            Assert.That(result.metricsObjectJson, Does.Contain("\"whitelistId\":\"agentbridge.selftest_ok\""));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.reportPath);
            Assert.That(File.Exists(GetReportAbsolutePath(result.reportPath)), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_051.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_051")]
        public void UnityRunStaticMethodTool_ParameterizedWhitelistHit_WithInvalidParameters_ReturnsInvalidArgs()
        {
            var tool = CreateTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.static.051",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestEcho\",\"parameters\":{\"message\":\"\"}}",
                    CreateSettings()),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ARGS_SCHEMA_VALIDATION_FAILED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_052.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_052")]
        public void UnityRunStaticMethodTool_TargetThrow_UnwrapsInnerException()
        {
            var tool = CreateTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.static.052",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestThrow\"}",
                    CreateSettings()),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Exception));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_STATIC_METHOD_EXCEPTION"));
            Assert.That(result.errors[0].message, Does.Contain("selftest boom"));
            Assert.That(result.errors[0].message, Does.Not.Contain(nameof(TargetInvocationException)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_053.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_053")]
        public void UnityRunStaticMethodTool_DisallowsRefreshAndSave_WhenWhitelistFlagsAreFalse()
        {
            var assetOps = new FakeAssetDatabaseOps();
            var settings = CreateSettings();
            var echoEntry = settings.allowedStaticMethods.Find(entry => entry.id == "agentbridge.selftest_echo");
            Assert.That(echoEntry, Is.Not.Null);
            var tool = CreateTool(assetOps);

            var result = tool.Execute(
                CreateContext(
                    "agb.static.053",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestEcho\",\"parameters\":{\"message\":\"hello\"}}",
                    settings),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(assetOps.RefreshCalls, Is.Zero);
            Assert.That(assetOps.SaveAssetsCalls, Is.Zero);
            Assert.That(AgentBridgeStaticMethodSelfTests.LastEchoMessage, Is.EqualTo("hello"));
            TrackReport(result.reportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_054.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_054")]
        public void UnityRunStaticMethodTool_MissingDoneMark_ReturnsSuccessWithWarning()
        {
            var tool = CreateTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.static.054",
                    "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestMissingDone\"}",
                    CreateSettings()),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.warnings, Has.Some.Matches<ToolWarning>(warning => warning.code == "AGENTBRIDGE_DONE_MARK_MISSING"));
            TrackReport(result.reportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_055.md
        [Test]
        [Category("AGB_Static")]
        [Category("AGB_055")]
        public void RunStaticMethodSchemasAndRegistry_AreAvailable()
        {
            var registry = new AgentToolRegistry();
            registry.Discover();

            Assert.That(registry.TryGetTool("unity.run_static_method", out _), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.run_static_method.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/agentbridge.selftest_echo.args.schema.json")), Is.True);
        }

        private UnityRunStaticMethodTool CreateTool(FakeAssetDatabaseOps assetOps = null)
        {
            return new UnityRunStaticMethodTool(assetOps ?? new FakeAssetDatabaseOps(), () => DateTime.UtcNow);
        }

        private AgentToolContext CreateContext(string commandId, string rawArgsJson, AgentBridgeSettings settings, int timeoutMs = 5000)
        {
            return new AgentToolContext
            {
                Command = new AgentCommand
                {
                    schemaVersion = "1.0",
                    commandId = commandId,
                    tool = "unity.run_static_method",
                    timeoutMs = timeoutMs,
                    createdAt = "2026-06-05T10:00:00Z",
                    rawArgsJson = rawArgsJson
                },
                RawArgsJson = rawArgsJson,
                Settings = settings
            };
        }

        private AgentBridgeSettings CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            settings.allowedStaticMethods = new List<AllowedStaticMethodEntry>
            {
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_ok",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestOk),
                    maxDurationMs = 60000,
                    sideEffects = "read",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestOk().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_echo",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestEcho),
                    parameterDtoTypeName = typeof(AgentBridgeStaticMethodEchoArgs).FullName,
                    argsSchemaPath = "Documentation~/schemas/agentbridge.selftest_echo.args.schema.json",
                    maxDurationMs = 60000,
                    sideEffects = "read",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestEcho().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_throw",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestThrow),
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestThrow().Done"
                },
                new AllowedStaticMethodEntry
                {
                    id = "agentbridge.selftest_missing_done",
                    typeName = typeof(AgentBridgeStaticMethodSelfTests).FullName,
                    methodName = nameof(AgentBridgeStaticMethodSelfTests.SelfTestMissingDone),
                    maxDurationMs = 60000,
                    sideEffects = "validate",
                    allowAssetDatabaseRefresh = false,
                    allowAssetDatabaseSaveAssets = false,
                    doneLogPattern = "AgentBridgeStaticMethodSelfTests.SelfTestMissingDone().Done"
                }
            };
            return settings;
        }

        private void TrackReport(string reportPath)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                _reportPaths.Add(reportPath);
            }
        }

        private static string GetReportAbsolutePath(string reportPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetPackageRelativePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class FakeAssetDatabaseOps : IAssetDatabaseOps
        {
            public int RefreshCalls { get; private set; }

            public int SaveAssetsCalls { get; private set; }

            public void Refresh()
            {
                RefreshCalls++;
            }

            public void SaveAssets()
            {
                SaveAssetsCalls++;
            }
        }
    }
}
