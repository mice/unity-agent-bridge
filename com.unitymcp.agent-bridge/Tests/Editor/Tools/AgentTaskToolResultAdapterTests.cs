using NUnit.Framework;
using System.Collections.Generic;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentTaskToolResultAdapterTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_120.md
        [Test]
        [Category("AGB_120")]
        public void RunningSnapshot_ProjectsToRunning()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-running"));
            var id = manager.Register(new NoOpTask());
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), new AgentCommand { commandId = "cmd", tool = "tool" }, "tool");
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Running));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_121.md
        [Test]
        [Category("AGB_121")]
        public void DomainFailure_ProjectsToFailedWithDiagnostic()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-domain"));
            var id = manager.Register(new CompletingTask(AgentTaskResult.DomainFailure("TestAssertionsFailed")));
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "tool");
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENT_TASK_DOMAIN_FAILURE"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_122.md
        [Test]
        [Category("AGB_122")]
        public void TimeoutError_ProjectsToTimeout()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-timeout"));
            var id = manager.Register(new FailingTask());
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "tool");
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Timeout));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_219.md
        [Test]
        [Category("AGB_219")]
        public void CompletedPayload_ProjectsToLegacyMetrics()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-payload"));
            var payload = new Dictionary<string, object>
            {
                ["tests"] = new[] { new { fullName = "Example.Test", outcome = "Passed", durationMs = 3 } }
            };
            var id = manager.Register(new CompletingTask(AgentTaskResult.Succeeded(payload)));
            manager.Start(id);

            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "unity.run_editmode_tests");

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("Example.Test"));
            Assert.That(result.metricsObjectJson, Does.Contain("durationMs"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_215.md
        [Test]
        [Category("AGB_215")]
        public void AssertionFailure_RemainsCompletedDomainFailure()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-assertion"));
            var id = manager.Register(new CompletingTask(AgentTaskResult.DomainFailure("TestAssertionsFailed")));
            manager.Start(id);
            var snapshot = manager.Get(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(snapshot, null, "unity.run_editmode_tests");
            Assert.That(snapshot.State, Is.EqualTo(AgentTaskState.Completed));
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENT_TASK_DOMAIN_FAILURE"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_216.md
        [Test]
        [Category("AGB_216")]
        public void StartupFailure_MapsToInfrastructureException()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-startup"));
            var id = manager.Register(new FailingTaskWithCode("AGENT_TASK_TEST_RUNNER_START"));
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "unity.run_editmode_tests");
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Exception));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENT_TASK_TEST_RUNNER_START"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_217.md
        [Test]
        [Category("AGB_217")]
        public void NoMatch_DomainFailure_MapsToLegacyFailedResult()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("adapter-nomatch"));
            var id = manager.Register(new CompletingTask(AgentTaskResult.DomainFailure("NoTestsMatched")));
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "unity.run_editmode_tests");
            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(result.summary, Is.EqualTo("NoTestsMatched"));
        }

        private sealed class NoOpTask : IAgentTask
        {
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) { }
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }

        private sealed class CompletingTask : IAgentTask
        {
            private readonly AgentTaskResult result;
            public CompletingTask(AgentTaskResult result) { this.result = result; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) { context.Complete(result); }
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }

        private sealed class FailingTask : IAgentTask
        {
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) { context.Fail(new AgentTaskError("AGENT_TASK_TIMEOUT", "timeout")); }
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }

        private sealed class FailingTaskWithCode : IAgentTask
        {
            private readonly string code;
            public FailingTaskWithCode(string code) { this.code = code; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) { context.Fail(new AgentTaskError(code, "runner startup failed")); }
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }
    }
}
