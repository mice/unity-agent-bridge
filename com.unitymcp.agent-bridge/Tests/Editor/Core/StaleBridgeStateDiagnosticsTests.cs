using System;
using System.IO;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class StaleBridgeStateDiagnosticsTests
    {
        private string _workspaceRoot;

        [SetUp]
        public void SetUp()
        {
            _workspaceRoot = Path.Combine(Path.GetTempPath(), "StaleBridgeStateDiagnosticsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspaceRoot);
            Directory.CreateDirectory(Path.Combine(_workspaceRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(_workspaceRoot, "ProjectSettings"));
            File.WriteAllText(Path.Combine(_workspaceRoot, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_workspaceRoot))
            {
                Directory.Delete(_workspaceRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_145.md
        [Test]
        [Category("AGB_145")]
        public void InvalidCreatedAt_ClassifiesAsStaleQueue()
        {
            var queue = new AgentCommandQueue(_workspaceRoot, "Temp/AgentBridge");
            Directory.CreateDirectory(queue.InboxDirectory);
            var commandPath = Path.Combine(queue.InboxDirectory, "cmd-145-a.json");
            File.WriteAllText(commandPath, "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-145-a\",\"tool\":\"unity.compile\",\"timeoutMs\":1000,\"createdAt\":42,\"args\":{}}");

            var failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", "Field 'createdAt' must be a JSON string.");
            var diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(_workspaceRoot, "Temp/AgentBridge", commandPath, "createdAt", failure);

            Assert.That(diagnostics.primaryClassification, Is.EqualTo("stale_queue"));
            Assert.That(diagnostics.evidencePriorityPath, Does.Contain("queue_snapshot"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_145.md
        [Test]
        [Category("AGB_145")]
        public void StalePollerWinsOverRuntimeMismatch()
        {
            var paths = new AgentBridgePaths(_workspaceRoot, AgentBridgeSettingsLoader.CreateDefaultSettings());
            paths.EnsureDirectories();
            Directory.CreateDirectory(Path.Combine(_workspaceRoot, "Tools", "AgentBridge"));
            File.WriteAllText(Path.Combine(_workspaceRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json"),
                "{\n  \"unityProjectPath\": \"" + _workspaceRoot.Replace("\\", "/") + "\"\n}\n");
            var status = new UnityBridgeStatusSnapshot
            {
                heartbeatUtc = DateTime.UtcNow.AddMinutes(-2).ToString("O"),
                currentStage = "unity.poller.pickup",
                currentCommandId = "cmd-145-b",
                projectPath = Path.Combine(_workspaceRoot, "OtherProject").Replace('\\', '/')
            };
            File.WriteAllText(paths.StatusFilePath, UnityEngine.JsonUtility.ToJson(status, false));

            var diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(_workspaceRoot, "Temp/AgentBridge", string.Empty, "createdAt", ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", "Field 'createdAt' must be a JSON string."));

            Assert.That(diagnostics.primaryClassification, Is.EqualTo("stale_poller_session"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_145.md
        [Test]
        [Category("AGB_145")]
        public void FreshPollerAndProjectMismatch_ClassifiesAsStaleRuntime()
        {
            var paths = new AgentBridgePaths(_workspaceRoot, AgentBridgeSettingsLoader.CreateDefaultSettings());
            paths.EnsureDirectories();
            Directory.CreateDirectory(Path.Combine(_workspaceRoot, "Tools", "AgentBridge"));
            File.WriteAllText(Path.Combine(_workspaceRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json"),
                "{\n  \"unityProjectPath\": \"" + _workspaceRoot.Replace("\\", "/") + "\"\n}\n");
            var status = new UnityBridgeStatusSnapshot
            {
                heartbeatUtc = DateTime.UtcNow.ToString("O"),
                currentStage = "unity.poller.idle",
                currentCommandId = string.Empty,
                projectPath = Path.Combine(_workspaceRoot, "OtherProject").Replace('\\', '/')
            };
            File.WriteAllText(paths.StatusFilePath, UnityEngine.JsonUtility.ToJson(status, false));

            var diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(_workspaceRoot, "Temp/AgentBridge", string.Empty, "createdAt", ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", "Field 'createdAt' must be a JSON string."));

            Assert.That(diagnostics.primaryClassification, Is.EqualTo("stale_runtime"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_145.md
        [Test]
        [Category("AGB_145")]
        public void InconclusiveRecordsMissingEvidence()
        {
            var paths = new AgentBridgePaths(_workspaceRoot, AgentBridgeSettingsLoader.CreateDefaultSettings());
            paths.EnsureDirectories();
            var status = new UnityBridgeStatusSnapshot
            {
                heartbeatUtc = DateTime.UtcNow.ToString("O"),
                currentStage = "unity.poller.idle",
                currentCommandId = string.Empty,
                projectPath = _workspaceRoot.Replace('\\', '/')
            };
            File.WriteAllText(paths.StatusFilePath, UnityEngine.JsonUtility.ToJson(status, false));

            var diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(_workspaceRoot, "Temp/AgentBridge", string.Empty, "createdAt", ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", "Field 'createdAt' must be a JSON string."));

            Assert.That(diagnostics.primaryClassification, Is.EqualTo("inconclusive"));
            Assert.That(diagnostics.missingEvidence.Count, Is.GreaterThan(0));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void AttachToResultMetrics_EmbedsDiagnosticsObject()
        {
            var result = new ToolResult
            {
                metricsObjectJson = "{}"
            };
            var diagnostics = new StaleBridgeStateDiagnostics
            {
                primaryClassification = "source_or_compiler",
                evidencePriorityPath = "source_or_compiler"
            };

            StaleBridgeStateDiagnosticsCollector.AttachToResultMetrics(result, diagnostics);
            Assert.That(result.metricsObjectJson, Does.Contain("\"staleStateDiagnostics\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"primaryClassification\":\"source_or_compiler\""));
        }
    }
}
