using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using TestRunnerPlugin = UnityMcp.BuiltInPlugins.TestRunner;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityTestToolsSmokeTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_063.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_063")]
        public void AgentToolRegistry_Discover_DoesNotOwnMigratedTestTools()
        {
            var registry = new AgentToolRegistry();

            registry.Discover();

            Assert.That(registry.TryGetTool("unity.run_editmode_tests", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.run_playmode_tests", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.agent_bridge_self_test", out _), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_088.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_088")]
        public void AgentToolRegistry_ManualRegister_FindsSelfTestTool()
        {
            var registry = new AgentToolRegistry();
            registry.Discover();

            var queue = new AgentCommandQueue(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, "Temp/AgentBridge");
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            var facade = new StubFacade();
            var runner = new AgentBridgeSelfTestRunner(facade, queue, settings);
            var tool = new TestRunnerPlugin.UnitySelfTestTool(runner);

            registry.Register(tool);

            Assert.That(registry.TryGetTool("unity.agent_bridge_self_test", out var found), Is.True);
            Assert.That(found, Is.SameAs(tool));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_064.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_064")]
        public void TestToolSchemas_Exist()
        {
            Assert.That(File.Exists(GetAbsolutePath("Packages/com.unitymcp.agent-bridge/Documentation~/schemas/unity.run_editmode_tests.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetAbsolutePath("Packages/com.unitymcp.agent-bridge/Documentation~/schemas/unity.run_playmode_tests.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetAbsolutePath("Packages/com.unitymcp.agent-bridge/Documentation~/schemas/unity.agent_bridge_self_test.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetAbsolutePath("Packages/com.unitymcp.agent-bridge/Documentation~/schemas/unity.read_report.args.schema.json")), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_152.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_152")]
        public void TestRunMetrics_RecordPath_UsesRepositoryEvidenceLocation()
        {
            var method = typeof(UnityTestOperationManager).GetMethod("ResolveRecordPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var recordPath = (string)method.Invoke(null, new object[] { "AGB_147" });

            Assert.That(recordPath, Is.EqualTo("Documentation~/AgentBridge/test_records/AGB_147.md"));
        }

        private static string GetAbsolutePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class StubFacade : IUnityToolFacade
        {
            public ToolResult Execute(AgentCommand command, IAgentCancellation cancellation)
            {
                return new ToolResult
                {
                    commandId = command.commandId,
                    tool = command.tool,
                    success = true,
                    status = ToolResultStatus.Success,
                    summary = "ok",
                    metricsObjectJson = "{}",
                };
            }

            public System.Collections.Generic.IReadOnlyList<ToolDescriptor> ListTools()
            {
                return Array.Empty<ToolDescriptor>();
            }

            public bool TryGetTool(string toolName, out IAgentTool tool)
            {
                tool = null;
                return false;
            }
        }
    }
}
