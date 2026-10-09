using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityMcp.AgentIntelligentAnalysis;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class VerifiedAnalysisOutputTests
    {
        private static EvidenceBundle Evidence => new EvidenceBundle(new[] { UnityCompileEvidence.Create("e1", "Assets/Fixture.cs", "synthetic") }, true);
        private static JObject Claim => new JObject { ["text"] = "Synthetic finding", ["evidenceIds"] = new JArray("e1"), ["confidence"] = 0.8 };
        private static JObject Result => new JObject { ["facts"] = new JArray(Claim), ["hypotheses"] = new JArray(),
            ["rootCause"] = Claim, ["impact"] = new JArray(Claim), ["recommendations"] = new JArray(Claim), ["verificationPlan"] = new JArray(Claim) };

        private static ProblemAnalysisResult Parse(JObject value) => (ProblemAnalysisResult)typeof(AgentTaskVerifiedAnalysisExecutionService)
            .GetMethod("ParseResult", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { value.ToString(), Evidence });

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_279.md
        [Test, Category("AGB_279")]
        public void MixedUnknownCitationIsPreservedAndRejected()
        {
            var value = Result;
            value["facts"][0]["evidenceIds"] = new JArray("e1", "unknown");
            var parsed = Parse(value);
            Assert.That(parsed.Facts[0].EvidenceIds, Does.Contain("unknown"));
            var validation = IntelligentAnalysisValidator.ValidateResult(parsed, Evidence);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Errors.Any(error => error.Code == "CLAIM_CITATION_UNKNOWN"), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_280.md
        [Test, Category("AGB_280")]
        public void MissingRequiredCollectionIsNotAnEmptySuccessfulResult()
        {
            var value = Result;
            value.Remove("verificationPlan");
            var error = Assert.Throws<TargetInvocationException>(() => Parse(value));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_281.md
        [Test, Category("AGB_281")]
        public void MalformedClaimMemberIsNotSilentlyDropped()
        {
            var value = Result;
            ((JArray)value["facts"]).Add("invalid member");
            var error = Assert.Throws<TargetInvocationException>(() => Parse(value));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_282.md
        [Test, Category("AGB_282")]
        public void MissingConfidenceDoesNotBecomeAValidZeroDefault()
        {
            var value = Result;
            ((JObject)value["facts"][0]).Remove("confidence");
            var error = Assert.Throws<TargetInvocationException>(() => Parse(value));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_283.md
        [TestCase(double.NaN), TestCase(double.PositiveInfinity), TestCase(double.NegativeInfinity), Category("AGB_283")]
        public void NonFiniteConfidenceFailsDomainValidation(double confidence)
        {
            var claim = new AnalysisClaim("synthetic", new[] { "e1" }, confidence);
            var result = new ProblemAnalysisResult(new[] { claim }, null, claim, null, null, null);
            var validation = IntelligentAnalysisValidator.ValidateResult(result, Evidence);
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Errors.Any(error => error.Code == "CONFIDENCE_INVALID"), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_284.md
        [Test, Category("AGB_284")]
        public void CompleteCitedShapeRemainsAccepted()
        {
            var parsed = Parse(Result);
            Assert.That(IntelligentAnalysisValidator.ValidateResult(parsed, Evidence).IsValid, Is.True);
            Assert.That(parsed.Facts[0].EvidenceIds, Is.EqualTo(new[] { "e1" }));
            Assert.That(parsed.RootCause.Confidence, Is.EqualTo(0.8));
        }
    }
}
