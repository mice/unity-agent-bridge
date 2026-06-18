using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityEditModeTestToolTests
    {
        private const string DemoFullName = "UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests.DemoEditModeProbe_Passes";
        private const string DemoAssembly = "UnityMcp.AgentBridge.Tests.Editor";
        private const string DemoCategory = "AGB_061";
        private const string DemoGroup = "UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests";

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_065.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_065")]
        public void Execute_NullSettings_ReturnsInvalidArgs()
        {
            var tool = new UnityEditModeTestTool();

            var result = tool.Execute(
                CreateContext("agb.testedit.065", "{}", null),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_SETTINGS_NULL"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_066.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_066")]
        public void CreateRunnerFilter_NoArgs_DoesNotSetAnyStructuredFields()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(TestMode.EditMode, new UnityTestRunArgs());

            Assert.That(filter.testMode, Is.EqualTo(TestMode.EditMode));
            Assert.That(filter.testNames, Is.Null);
            Assert.That(filter.assemblyNames, Is.Null);
            Assert.That(filter.categoryNames, Is.Null);
            Assert.That(filter.groupNames, Is.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_067.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_067")]
        public void CreateRunnerFilter_LegacyFilter_MapsToTestNames()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    filter = DemoFullName
                });

            Assert.That(filter.testNames, Is.EqualTo(new[] { DemoFullName }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_068.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_068")]
        public void CreateRunnerFilter_TestNames_UseStructuredValues()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    testNames = new[] { DemoFullName }
                });

            Assert.That(filter.testNames, Is.EqualTo(new[] { DemoFullName }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_069.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_069")]
        public void CreateRunnerFilter_AssemblyNames_MapToAssemblyFilter()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    assemblyNames = new[] { DemoAssembly }
                });

            Assert.That(filter.assemblyNames, Is.EqualTo(new[] { DemoAssembly }));
            Assert.That(filter.testMode, Is.EqualTo(TestMode.EditMode));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_071.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_071")]
        public void CreateRunnerFilter_CategoryNames_MapToCategoryFilter()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    categoryNames = new[] { DemoCategory }
                });

            Assert.That(filter.categoryNames, Is.EqualTo(new[] { DemoCategory }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_072.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_072")]
        public void CreateRunnerFilter_GroupNames_MapToGroupFilter()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    groupNames = new[] { DemoGroup }
                });

            Assert.That(filter.groupNames, Is.EqualTo(new[] { DemoGroup }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_073.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_073")]
        public void CreateRunnerFilter_AssemblyNamesAndTestNames_CombineWithoutLoss()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(
                TestMode.EditMode,
                new UnityTestRunArgs
                {
                    assemblyNames = new[] { DemoAssembly },
                    testNames = new[] { DemoFullName }
                });

            Assert.That(filter.assemblyNames, Is.EqualTo(new[] { DemoAssembly }));
            Assert.That(filter.testNames, Is.EqualTo(new[] { DemoFullName }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_074.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_074")]
        public void Execute_FilterAndTestNamesConflict_ReturnsInvalidArgs()
        {
            var tool = new UnityEditModeTestTool();
            var settings = CreateSettings();
            var result = tool.Execute(
                CreateContext(
                    "agb.testedit.074",
                    "{\"filter\":\"legacy\",\"testNames\":[\"Demo.Test\"]}",
                    settings),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_TEST_FILTER_CONFLICT"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_075.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_075")]
        public void Execute_TestNamesWildcard_ReturnsInvalidArgs()
        {
            AssertWildcardInvalidArgs("{\"testNames\":[\"Demo.*\"]}");
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_076.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_076")]
        public void Execute_AssemblyNamesWildcard_ReturnsInvalidArgs()
        {
            AssertWildcardInvalidArgs("{\"assemblyNames\":[\"UnityMcp.AgentBridge.Tests.*\"]}");
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_077.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_077")]
        public void Execute_CategoryNamesWildcard_ReturnsInvalidArgs()
        {
            AssertWildcardInvalidArgs("{\"categoryNames\":[\"AGB_*\"]}");
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_078.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_078")]
        public void Execute_GroupNamesWildcard_ReturnsInvalidArgs()
        {
            AssertWildcardInvalidArgs("{\"groupNames\":[\"UnityMcp.*\"]}");
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_101.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_101")]
        public void Execute_WhileUnityTestRunnerActive_ReturnsBlocked()
        {
            UnityTestOperationManager.SetTestRunActiveProviderForTests(() => true);
            try
            {
                var tool = new UnityEditModeTestTool();
                var result = tool.Execute(
                    CreateContext("agb.testedit.101", "{\"testNames\":[\"" + DemoFullName + "\"]}", CreateSettings()),
                    NoOpAgentCancellation.Instance);

                Assert.That(result.status, Is.EqualTo(ToolResultStatus.Blocked));
                Assert.That(result.summary, Does.Contain("Unity Test Runner is already running"));
            }
            finally
            {
                UnityTestOperationManager.SetTestRunActiveProviderForTests(null);
            }
        }

        private static void AssertWildcardInvalidArgs(string rawArgsJson)
        {
            var tool = new UnityEditModeTestTool();
            var settings = CreateSettings();
            var result = tool.Execute(
                CreateContext("agb.testedit.wildcard", rawArgsJson, settings),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_TEST_FILTER_WILDCARD_UNSUPPORTED"));
        }

        private static AgentBridgeSettings CreateSettings()
        {
            return new AgentBridgeSettings
            {
                maxToolDurationMs = 30000,
                tempRoot = "Temp/AgentBridge"
            };
        }

        private static AgentToolContext CreateContext(string commandId, string rawArgsJson, AgentBridgeSettings settings, int timeoutMs = 5000)
        {
            return new AgentToolContext
            {
                Command = new AgentCommand
                {
                    schemaVersion = "1.0",
                    commandId = commandId,
                    tool = "unity.run_editmode_tests",
                    timeoutMs = timeoutMs,
                    createdAt = "2026-06-08T10:00:00Z",
                    rawArgsJson = rawArgsJson
                },
                RawArgsJson = rawArgsJson,
                Settings = settings
            };
        }
    }
}
