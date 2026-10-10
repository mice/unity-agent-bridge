using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.AgentIntelligentAnalysis;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class VerifiedAnalysisProviderTests
    {
        private string _report;
        private string _evidence;
        private AgentRunnerProviderRegistration _registration;
        private AgentTaskControlResponse _created;

        [SetUp] public void SetUp()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var name = "compile_provider_" + Guid.NewGuid().ToString("N") + ".json";
            _report = Path.Combine(root, "Temp", "AgentBridge", "reports", name);
            _evidence = name;
            Directory.CreateDirectory(Path.GetDirectoryName(_report));
            File.WriteAllText(_report, "{\"contractVersion\":\"1.0\",\"diagnostics\":[]}");
        }
        [TearDown] public void TearDown()
        {
            _registration?.Dispose();
            if (_created?.AgentTaskId != null) AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
                { Operation = AgentTaskControlProtocol.Cancel, AgentTaskId = _created.AgentTaskId });
            if (File.Exists(_report)) File.Delete(_report);
        }

        private AgentTaskControlResponse Create()
        {
            _created = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create, TaskType = AgentTaskVerifiedAnalysisExecutionService.TaskType,
                Payload = new JObject { ["problemId"] = "provider-fixture", ["evidenceIds"] = new JArray(_evidence), ["timeoutMs"] = 10000 }.ToString()
            });
            return _created;
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_333.md
        [Test, Category("AGB_333")]
        public void AlternativeProviderStillUsesHostCitationValidation()
        {
            var claim = new JObject { ["text"] = "fixture", ["evidenceIds"] = new JArray("unknown"), ["confidence"] = 0.8 };
            var output = new JObject { ["facts"] = new JArray(claim), ["hypotheses"] = new JArray(), ["rootCause"] = claim,
                ["impact"] = new JArray(claim), ["recommendations"] = new JArray(claim), ["verificationPlan"] = new JArray(claim) }.ToString();
            _registration = AgentRunnerProviderRegistry.Register(new FixtureProvider(output));
            var result = Create();
            Assert.That(result.AgentTaskId, Does.StartWith("analysis-"));
            Assert.That(result.Snapshot.State, Is.EqualTo(AgentTaskState.Failed));
            Assert.That(result.ErrorCode, Is.EqualTo("CLAIM_CITATION_UNKNOWN"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_334.md
        [Test, Category("AGB_334")]
        public void RetiredProviderCancelsRunAndRetainsNativeTaskIdentity()
        {
            var provider = new FixtureProvider(null);
            _registration = AgentRunnerProviderRegistry.Register(provider);
            var created = Create();
            Assert.That(created.Snapshot.State, Is.EqualTo(AgentTaskState.Running));
            Assert.That(provider.Policy.TimeoutMilliseconds, Is.EqualTo(10000));
            _registration.Dispose();
            var observed = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
                { Operation = AgentTaskControlProtocol.Result, AgentTaskId = created.AgentTaskId });
            Assert.That(observed.AgentTaskId, Is.EqualTo(created.AgentTaskId));
            Assert.That(observed.Snapshot.State, Is.EqualTo(AgentTaskState.Cancelled));
            Assert.That(provider.Runner.Cancelled, Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_335.md
        [Test, Category("AGB_335")]
        public void UnavailableProviderKeepsRejectedTaskQueryable()
        {
            var created = Create();
            Assert.That(created.ErrorCode, Is.EqualTo("AGENT_PROVIDER_UNAVAILABLE"));
            Assert.That(created.AgentTaskId, Does.StartWith("analysis-"));
            var observed = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest
                { Operation = AgentTaskControlProtocol.Query, AgentTaskId = created.AgentTaskId });
            Assert.That(observed.Snapshot.State, Is.EqualTo(AgentTaskState.Failed));
            Assert.That(observed.AgentTaskId, Is.EqualTo(created.AgentTaskId));
        }

        private sealed class FixtureProvider : IAgentRunnerProvider, IAgentRunnerFactory
        {
            private readonly string _output;
            public FixtureProvider(string output) => _output = output;
            public string Id { get; } = "fixture-" + Guid.NewGuid().ToString("N");
            public IEnumerable<string> TemplateIds => new[] { UnityProgrammerSpecialistFactory.TemplateId };
            public AgentRunnerPolicy Policy;
            public FixtureRunner Runner;
            public IAgentRunnerFactory Prepare(AgentRunnerPolicy policy) { Policy = policy; return this; }
            public IAgentRunner Create(AgentRunRequest request) => Runner = new FixtureRunner(_output);
        }
        private sealed class FixtureRunner : IAgentRunner
        {
            private readonly string _output;
            public FixtureRunner(string output) => _output = output;
            public bool CanCancel => true;
            public bool Cancelled;
            public void Start(AgentRunRequest request, IAgentRunnerContext context)
            { if (_output != null) context.Complete(AgentRunResult.Succeeded(_output)); }
            public void Advance(IAgentRunnerContext context) { }
            public void RequestCancellation(IAgentRunnerContext context) { Cancelled = true; context.Cancel(); }
        }
    }
}
