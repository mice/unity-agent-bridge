using NUnit.Framework;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class VerifiedAnalysisTaskControlTests
    {
        // TestRecord: Documentation~/AgentBridge/test_records/AGB_262.md
        [Test, Category("AGB_262")]
        public void ProviderConfigurationFailureRejectsBothAnalysisTasksWithQueryableIdentity()
        {
            using (AgentRunnerProviderRegistry.Register(new InvalidConfigurationProvider()))
            {
                foreach (var taskType in new[] { AgentTaskVerifiedAnalysisExecutionService.TaskType,
                    AgentTaskVerifiedAnalysisExecutionService.AssetTaskType })
                {
                    var response = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
                    {
                        Operation = AgentTaskControlProtocol.Create,
                        TaskType = taskType,
                        Payload = "{\"problemId\":\"p1\",\"evidenceIds\":[\"missing-test-evidence\"]}"
                    });
                    Assert.That(response.Success, Is.False);
                    Assert.That(response.ErrorCode, Is.EqualTo("PI_CONFIGURATION_INVALID"));
                    Assert.That(response.AgentTaskId, Does.StartWith("analysis-"));
                    var query = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
                    { Operation = AgentTaskControlProtocol.Query, AgentTaskId = response.AgentTaskId });
                    Assert.That(query.ErrorCode, Is.EqualTo("PI_CONFIGURATION_INVALID"));
                }
            }
        }

        private sealed class InvalidConfigurationProvider : IAgentRunnerProvider
        {
            public string Id => "fixture-invalid-configuration";
            public System.Collections.Generic.IEnumerable<string> TemplateIds => new[] { "unity_compile_failure_analysis", "unity_asset_problem_analysis" };
            public IAgentRunnerFactory Prepare(AgentRunnerPolicy policy) => throw new AgentProviderException("PI_CONFIGURATION_INVALID", "Fixture provider rejected its captured configuration.");
        }

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

        // TestRecord: Documentation~/Testing/Unit/AgentRuntime/AGB_253.md
        [Test]
        [Category("AGB_253")]
        public void AssetAnalysisCreateRejectsForbiddenOverridesWithStableFailureIdentity()
        {
            var response = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create,
                TaskType = AgentTaskVerifiedAnalysisExecutionService.AssetTaskType,
                Payload = "{\"problemId\":\"p1\",\"evidenceIds\":[\"e1\"],\"tools\":[\"shell\"]}"
            });
            Assert.That(response.Success, Is.False);
            Assert.That(response.AgentTaskId, Does.StartWith("analysis-"));
            Assert.That(response.ErrorCode, Is.EqualTo("FORBIDDEN_OVERRIDE"));
        }

        // TestRecord: Documentation~/Testing/Unit/AgentRuntime/AGB_254.md
        [Test]
        [Category("AGB_254")]
        public void AssetSpecialistRegistersOnlyBoundedVerifiedTemplate()
        {
            var registry = new UnityMcp.AgentIntelligentAnalysis.AnalysisTemplateRegistry();
            var registration = UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.Register(registry);
            Assert.That(registration.IsValid, Is.True);
            Assert.That(registry.Get(UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.TemplateId), Is.Not.Null);
            var capability = registry.Get(UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.TemplateId).EffectiveCapability;
            Assert.That(capability.NetworkAllowed, Is.False);
            Assert.That(capability.Paths.WriteFiles, Is.Empty);
            Assert.That(capability.Paths.WriteFilePatterns, Is.Empty);
            Assert.That(capability.Tools, Does.Contain(UnityMcp.AgentIntelligentAnalysis.AbstractToolCapabilities.AssetReport));
        }

        // TestRecord: Documentation~/Testing/Unit/AgentRuntime/AGB_255.md
        [Test]
        [Category("AGB_255")]
        public void DeterministicAssetOperationCannotBecomeAnalysisTemplate()
        {
            var registry = new UnityMcp.AgentIntelligentAnalysis.AnalysisTemplateRegistry();
            var specialist = registry.RegisterSpecialist(UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.CreateSpecialist());
            Assert.That(specialist.IsValid, Is.True);
            var template = new UnityMcp.AgentIntelligentAnalysis.VerifiedAnalysisTemplate(
                "unity_prefab_scan", UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.SpecialistId, "prefab-scan",
                UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.CreateAssetProblemTemplate().RequestedCapability,
                new[] { UnityMcp.AgentIntelligentAnalysis.UnityAssetAnalystSpecialistFactory.AssetReportEvidenceKind }, true, true, true, true);
            var result = registry.RegisterTemplate(template);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(error => error.Code == "DETERMINISTIC_TOOL_TEMPLATE_FORBIDDEN"), Is.True);
        }
    }
}
