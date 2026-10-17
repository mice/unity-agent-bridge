using NUnit.Framework;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;
using UnityMcp.BuiltInPlugins.TestRunner;
using System.IO;
using System.Reflection;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityEditModeTestToolTests
    {
        private const string DemoFullName = "UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests.DemoEditModeProbe_Passes";
        private const string DemoAssembly = "UnityMcp.AgentBridge.Tests.Editor";
        private const string DemoCategory = "AGB_061";
        private const string DemoGroup = "UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests";
        private AgentBridgeSettings _settings;

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                Object.DestroyImmediate(_settings);
                _settings = null;
            }
        }

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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_218.md
        [Test]
        [Category("AGB_TestTools")]
        [Category("AGB_218")]
        public void TaskBackedLegacyFilter_PreservesRunnerFilterSelection()
        {
            var filter = UnityTestOperationManager.CreateRunnerFilter(TestMode.EditMode, new UnityTestRunArgs { filter = DemoFullName });
            Assert.That(filter.testMode, Is.EqualTo(TestMode.EditMode));
            Assert.That(filter.testNames, Is.EqualTo(new[] { DemoFullName }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_355.md
        [Test, Category("AGB_TestTools"), Category("AGB_355")]
        public void TerminalResult_AfterSettingsDestroyed_CompletesCapturedQueue()
        {
            var settings = CreateSettings();
            settings.tempRoot = "Temp/EditModeQueue-" + System.Guid.NewGuid().ToString("N");
            var queue = new AgentCommandQueue(Directory.GetParent(Application.dataPath).FullName, settings.tempRoot);
            var run = CreateTerminalRun(settings.tempRoot, "agb.queue.destroyed");
            Object.DestroyImmediate(settings);
            Assert.That(settings == null, Is.True, "Exercise Unity's destroyed-object null semantics.");
            try
            {
                CompleteTerminalRun(run);
                var path = Path.Combine(queue.OutboxDirectory, "agb.queue.destroyed.result.json");
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.ReadAllText(path), Does.Contain("\"status\":\"success\""));
            }
            finally
            {
                Directory.Delete(queue.QueueRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_356.md
        [Test, Category("AGB_TestTools"), Category("AGB_356")]
        public void TerminalResult_WhenWriteFails_RemainsRetryable()
        {
            var relativeRoot = "Temp/EditModeQueue-" + System.Guid.NewGuid().ToString("N");
            var queue = new AgentCommandQueue(Directory.GetParent(Application.dataPath).FullName, relativeRoot);
            var run = CreateTerminalRun(relativeRoot, "agb.queue.retry");
            Directory.Delete(queue.OutboxDirectory);
            File.WriteAllText(queue.OutboxDirectory, "Deliberately block this task-owned output directory.");
            try
            {
                Assert.Throws<TargetInvocationException>(() => CompleteTerminalRun(run));
                Assert.That(run.GetType().GetField("ResultWritten").GetValue(run), Is.False);
                File.Delete(queue.OutboxDirectory);
                Directory.CreateDirectory(queue.OutboxDirectory);
                CompleteTerminalRun(run);
                Assert.That(File.Exists(Path.Combine(queue.OutboxDirectory, "agb.queue.retry.result.json")), Is.True);
                Assert.That(run.GetType().GetField("ResultWritten").GetValue(run), Is.True);
            }
            finally
            {
                Directory.Delete(queue.QueueRoot, true);
            }
        }

        private static object CreateTerminalRun(string queueRoot, string commandId)
        {
            var type = typeof(AgentTaskEditModeExecutionService).GetNestedType("ActiveRun", BindingFlags.NonPublic);
            var run = System.Activator.CreateInstance(type, true);
            type.GetField("TaskId").SetValue(run, new AgentTaskId(System.Guid.NewGuid().ToString("N")));
            type.GetField("Command").SetValue(run, new AgentCommand { commandId = commandId, tool = "unity.run_editmode_tests" });
            type.GetField("QueueRoot").SetValue(run, queueRoot);
            return run;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_359.md
        [Test, Category("AGB_TestTools"), Category("AGB_359")]
        public void TerminalDelivery_ReloadRecoversPersistedResultWithoutRunningTests()
        {
            var queue = new AgentCommandQueue(Directory.GetParent(Application.dataPath).FullName,
                "Temp/EditModeQueue-" + System.Guid.NewGuid().ToString("N"));
            var commandId = "agb.queue.reload." + System.Guid.NewGuid().ToString("N");
            var run = CreateTerminalRun(queue.RelativeRoot, commandId);
            var statePath = Path.Combine(Application.dataPath, "../Library/AgentBridge/agent-task-editmode", commandId + ".task.json");
            Directory.Delete(queue.OutboxDirectory);
            File.WriteAllText(queue.OutboxDirectory, "Blocked output fixture");
            try
            {
                Assert.Throws<TargetInvocationException>(() => CompleteTerminalRun(run));
                Assert.That(File.Exists(statePath), Is.True);
                File.Delete(queue.OutboxDirectory);
                RestoreRuns();
                Assert.That(File.Exists(Path.Combine(queue.OutboxDirectory, commandId + ".result.json")), Is.True);
                Assert.That(File.Exists(statePath), Is.False);
            }
            finally
            {
                if (File.Exists(statePath)) File.Delete(statePath);
                Directory.Delete(queue.QueueRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_360.md
        [Test, Category("AGB_TestTools"), Category("AGB_360")]
        public void LegacyTerminalDelivery_ReloadPreservesDurableMetrics()
        {
            var project = Directory.GetParent(Application.dataPath).FullName;
            var queue = new AgentCommandQueue(project, "Temp/EditModeQueue-" + System.Guid.NewGuid().ToString("N"));
            var commandId = "agb.queue.legacy." + System.Guid.NewGuid().ToString("N");
            var taskId = System.Guid.NewGuid().ToString("N");
            var statePath = Path.Combine(project, "Library/AgentBridge/agent-task-editmode", commandId + ".task.json");
            var retainedPath = Path.Combine(project, "Library/AgentBridge/agent-task-results", taskId + ".json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                Directory.CreateDirectory(Path.GetDirectoryName(retainedPath));
                var response = new AgentTaskControlResponse { Success = true, AgentTaskId = taskId, State = "Completed",
                    ResultKind = "Succeeded", ResultPayloadJson = "{\"tests\":[{\"outcome\":\"Passed\"}]}" };
                File.WriteAllText(retainedPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { version = 1, project,
                    response = new AgentTaskControlCodec().SerializeResponse(response) }));
                File.WriteAllText(statePath, Newtonsoft.Json.JsonConvert.SerializeObject(new { version = 1,
                    agentTaskId = taskId, commandId, queueRoot = queue.RelativeRoot, completeQueue = true,
                    commandJson = Newtonsoft.Json.JsonConvert.SerializeObject(new AgentCommand { commandId = commandId, tool = "unity.run_editmode_tests" }) }));
                RestoreRuns();
                var result = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(queue.OutboxDirectory, commandId + ".result.json")));
                Assert.That(result.Value<string>("status"), Is.EqualTo("success"));
                Assert.That(result["metrics"]?["tests"]?[0]?["outcome"]?.ToString(), Is.EqualTo("Passed"));
                Assert.That(File.Exists(statePath), Is.False);
                Assert.That(File.Exists(retainedPath), Is.True);
            }
            finally
            {
                if (File.Exists(statePath)) File.Delete(statePath);
                if (File.Exists(retainedPath)) File.Delete(retainedPath);
                Directory.Delete(queue.QueueRoot, true);
            }
        }

        private static void RestoreRuns() => typeof(AgentTaskEditModeExecutionService)
            .GetMethod("RestorePersistedRuns", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        private static void CompleteTerminalRun(object run)
        {
            typeof(AgentTaskEditModeExecutionService).GetMethod("WriteTerminalResult", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { run, new ToolResult { success = true, status = ToolResultStatus.Success, summary = "Completed fixture." } });
        }

        private void AssertWildcardInvalidArgs(string rawArgsJson)
        {
            var tool = new UnityEditModeTestTool();
            var settings = CreateSettings();
            var result = tool.Execute(
                CreateContext("agb.testedit.wildcard", rawArgsJson, settings),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_TEST_FILTER_WILDCARD_UNSUPPORTED"));
        }

        private AgentBridgeSettings CreateSettings()
        {
            _settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            _settings.maxToolDurationMs = 30000;
            _settings.tempRoot = "Temp/AgentBridge";
            return _settings;
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
