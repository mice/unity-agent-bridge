using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityDiagnosticToolTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_056.md
        [Test]
        [Category("AGB_Diagnostic")]
        [Category("AGB_056")]
        public void UnityDiagnosticTool_SceneDiagnostic_ReturnsStructuredMetrics()
        {
            var tool = new UnityDiagnosticTool();

            var result = tool.Execute(
                CreateContext("agb.diagnostic.056", "{\"diagnosticType\":\"scene\",\"targetPath\":\"Assets/Scenes/AppMain.unity\"}"),
                NoOpAgentCancellation.Instance);

            var metrics = JsonUtility.FromJson<UnityDiagnosticMetrics>(result.metricsObjectJson);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(metrics.diagnosticType, Is.EqualTo("scene"));
            Assert.That(metrics.targetPath, Is.EqualTo("Assets/Scenes/AppMain.unity"));
            Assert.That(metrics.supported, Is.True);
            Assert.That(metrics.exists, Is.True);
            Assert.That(metrics.assetType, Is.EqualTo("SceneAsset"));
            Assert.That(metrics.dependencyCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(GetAbsolutePath(result.reportPath)), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_057.md
        [Test]
        [Category("AGB_Diagnostic")]
        [Category("AGB_057")]
        public void UnityDiagnosticTool_UnsupportedKnownDiagnostic_ReturnsUnsupported()
        {
            var tool = new UnityDiagnosticTool();

            var result = tool.Execute(
                CreateContext("agb.diagnostic.057", "{\"diagnosticType\":\"fx_prefab\",\"targetPath\":\"Assets/Scenes/AppMain.unity\"}"),
                NoOpAgentCancellation.Instance);

            var metrics = JsonUtility.FromJson<UnityDiagnosticMetrics>(result.metricsObjectJson);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Unsupported));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_DIAGNOSTIC_NOT_INTEGRATED"));
            Assert.That(metrics.supported, Is.False);
            Assert.That(metrics.integrationPoint, Does.Contain("diagnostic_not_integrated"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_058.md
        [Test]
        [Category("AGB_Diagnostic")]
        [Category("AGB_058")]
        public void UnityDiagnosticTool_UnsafePath_ReturnsInvalidArgs()
        {
            var tool = new UnityDiagnosticTool();

            var result = tool.Execute(
                CreateContext("agb.diagnostic.058", "{\"diagnosticType\":\"scene\",\"targetPath\":\"../Secrets/Outside.unity\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_PATH_UNSAFE"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_059.md
        [Test]
        [Category("AGB_Diagnostic")]
        [Category("AGB_059")]
        public void UnityDiagnosticTool_UnknownDiagnosticType_ReturnsInvalidArgs()
        {
            var tool = new UnityDiagnosticTool();

            var result = tool.Execute(
                CreateContext("agb.diagnostic.059", "{\"diagnosticType\":\"unknown_kind\",\"targetPath\":\"Assets/Scenes/AppMain.unity\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_DIAGNOSTIC_TYPE_INVALID"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_060.md
        [Test]
        [Category("AGB_Diagnostic")]
        [Category("AGB_060")]
        public void UnityDiagnosticTool_SchemaAndRegistry_AreAvailable()
        {
            var registry = new AgentToolRegistry();
            registry.Discover();

            Assert.That(registry.TryGetTool("unity.run_diagnostic", out _), Is.True);
            Assert.That(File.Exists(GetAbsolutePath("Packages/com.unitymcp.agent-bridge/Documentation~/schemas/unity.run_diagnostic.args.schema.json")), Is.True);
        }

        private static AgentToolContext CreateContext(string commandId, string rawArgsJson)
        {
            return new AgentToolContext
            {
                Command = new AgentCommand
                {
                    schemaVersion = "1.0",
                    commandId = commandId,
                    tool = "unity.run_diagnostic",
                    timeoutMs = 5000,
                    createdAt = "2026-06-05T10:00:00Z",
                    rawArgsJson = rawArgsJson
                },
                RawArgsJson = rawArgsJson,
                Settings = ScriptableObject.CreateInstance<AgentBridgeSettings>()
            };
        }

        private static string GetAbsolutePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
