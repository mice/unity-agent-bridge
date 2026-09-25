using NUnit.Framework;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class VerifiedAnalysisTaskControlTests
    {
        // TestRecord: Documentation~/Testing/Unit/AgentRuntime/AGB_250.md
        [Test]
        [Category("AGB_250")]
        public void AnalysisCreateRejectsForbiddenOverridesWithStableFailureIdentity()
        {
            var response = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create,
                TaskType = "analysis_compile_failure",
                Payload = "{\"problemId\":\"p1\",\"evidenceIds\":[\"e1\"],\"model\":\"caller\"}"
            });
            Assert.That(response.Success, Is.False);
            Assert.That(response.AgentTaskId, Does.StartWith("analysis-"));
            Assert.That(response.ErrorCode, Is.EqualTo("FORBIDDEN_OVERRIDE"));
        }

        // TestRecord: Documentation~/Testing/Unit/AgentRuntime/AGB_251.md
        [Test]
        [Category("AGB_251")]
        public void AnalysisQueryAndCancelUnknownBindingRemainProjectLocal()
        {
            var query = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Query,
                AgentTaskId = "analysis-missing"
            });
            var cancel = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Cancel,
                AgentTaskId = "analysis-missing"
            });
            Assert.That(query.ErrorCode, Is.EqualTo("NOT_FOUND"));
            Assert.That(cancel.Cancellation, Is.EqualTo(AgentTaskCancellationDisposition.NotFound));
        }
    }
}
