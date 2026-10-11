using NUnit.Framework;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentTaskControlProtocolTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_230.md
        [Test]
        [Category("AGB_230")]
        public void CompileCreate_RejectsUnknownPayloadType()
        {
            var response = AgentTaskCompileExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create,
                TaskType = "wrong",
                Payload = "{}"
            });
            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo("UNKNOWN_TASK_TYPE"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_231.md
        [Test]
        [Category("AGB_231")]
        public void CompileQueryAndCancel_UnknownBindingReturnNotFound()
        {
            var query = AgentTaskCompileExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Query,
                AgentTaskId = "compile-missing"
            });
            var cancel = AgentTaskCompileExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Cancel,
                AgentTaskId = "compile-missing"
            });
            Assert.That(query.ErrorCode, Is.EqualTo("NOT_FOUND"));
            Assert.That(cancel.Success, Is.False);
            Assert.That(cancel.Cancellation, Is.EqualTo(AgentTaskCancellationDisposition.NotFound));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_232.md
        [Test]
        [Category("AGB_232")]
        public void PlayModeCreate_RejectsMalformedPayload()
        {
            var response = AgentTaskPlayModeExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create,
                TaskType = "run_playmode_tests",
                Payload = "{ malformed"
            });
            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo("INVALID_ARGS"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_233.md
        [Test]
        [Category("AGB_233")]
        public void PlayModeQueryAndCancel_UnknownBindingReturnNotFound()
        {
            var query = AgentTaskPlayModeExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Query,
                AgentTaskId = "playmode-missing"
            });
            var cancel = AgentTaskPlayModeExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Cancel,
                AgentTaskId = "playmode-missing"
            });
            Assert.That(query.ErrorCode, Is.EqualTo("NOT_FOUND"));
            Assert.That(cancel.Success, Is.False);
            Assert.That(cancel.Cancellation, Is.EqualTo(AgentTaskCancellationDisposition.NotFound));
        }
    }
}
