using System;
using System.Collections.Generic;

namespace UnityMcp.AgentIntelligentAnalysis
{
    public abstract class AnalysisRequest
    {
        public abstract string ProblemId { get; }
        public abstract IReadOnlyList<string> EvidenceIds { get; }
        public abstract ValidationResult<AnalysisRequest> ValidateRequest();
    }
    /// <summary>Creates the bounded Unity asset problem-analysis Specialist slice.</summary>
    public static class UnityAssetAnalystSpecialistFactory
    {
        public const string SpecialistId = "unity-asset-analyst";
        public const string TemplateId = "unity_asset_problem_analysis";
        public const string ProblemType = "asset-problem-analysis";
        public const string AssetReportEvidenceKind = "asset-report";

        public static SpecialistProfile CreateSpecialist()
        {
            var authority = new CapabilityProfile(
                "unity-asset-analyst-authority",
                new ModelProfile("deepseek", "deepseek-flash"),
                new[] { AbstractToolCapabilities.ProjectRead, AbstractToolCapabilities.ProjectSearch, AbstractToolCapabilities.AssetReport },
                new PathPolicy(new[] { "Assets", "Packages", "ProjectSettings", "Documentation~" }, null, null, new[] { "Library", "Temp", "Logs", "Build" }),
                new ExecutionBudget(8, 262144, 65536, 120),
                networkAllowed: false);

            return new SpecialistProfile(
                SpecialistId,
                "Unity asset dependencies, import settings, prefab/scene relationships, and runtime resource risks",
                new[] { "problem-analysis", "asset-problem-analysis" },
                new[] { AssetReportEvidenceKind },
                authority);
        }

        public static VerifiedAnalysisTemplate CreateAssetProblemTemplate()
        {
            var authority = CreateSpecialist().Authority;
            return new VerifiedAnalysisTemplate(
                TemplateId,
                SpecialistId,
                ProblemType,
                new CapabilityProfile(
                    "unity-asset-problem-capability",
                    authority.Model,
                    new[] { AbstractToolCapabilities.ProjectRead, AbstractToolCapabilities.ProjectSearch, AbstractToolCapabilities.AssetReport },
                    new PathPolicy(new[] { "Assets", "Packages", "ProjectSettings", "Documentation~" }),
                    new ExecutionBudget(6, 131072, 32768, 90),
                    networkAllowed: false),
                new[] { AssetReportEvidenceKind },
                outputValidationDefined: true,
                lifecycleTestsPassed: true,
                negativePermissionTestsPassed: true,
                acceptanceEvidenceRecorded: true);
        }

        public static ValidationResult<RegisteredAnalysisTemplate> Register(AnalysisTemplateRegistry registry)
        {
            if (registry == null) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(new ValidationError("REGISTRY_REQUIRED", "Registry is required."));
            var specialist = registry.RegisterSpecialist(CreateSpecialist());
            if (!specialist.IsValid) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(ToArray(specialist.Errors));
            return registry.RegisterTemplate(CreateAssetProblemTemplate());
        }

        private static ValidationError[] ToArray(IReadOnlyList<ValidationError> errors)
        {
            var result = new ValidationError[errors.Count];
            for (var i = 0; i < errors.Count; i++) result[i] = errors[i];
            return result;
        }
    }

    public sealed class AssetProblemAnalysisRequest : AnalysisRequest
    {
        public AssetProblemAnalysisRequest(string problemId, IEnumerable<string> evidenceIds)
        {
            ProblemId = problemId ?? string.Empty;
            EvidenceIds = new List<string>(evidenceIds ?? Array.Empty<string>()).AsReadOnly();
        }

        public override string ProblemId { get; }
        public override IReadOnlyList<string> EvidenceIds { get; }

        public ValidationResult<AssetProblemAnalysisRequest> Validate()
        {
            if (string.IsNullOrWhiteSpace(ProblemId)) return ValidationResult<AssetProblemAnalysisRequest>.Invalid(new ValidationError("PROBLEM_ID_REQUIRED", "Problem id is required."));
            if (EvidenceIds.Count == 0) return ValidationResult<AssetProblemAnalysisRequest>.Invalid(new ValidationError("EVIDENCE_REFERENCE_REQUIRED", "At least one evidence reference is required."));
            return ValidationResult<AssetProblemAnalysisRequest>.Valid(this);
        }

        public override ValidationResult<AnalysisRequest> ValidateRequest()
        {
            var result = Validate();
            return result.IsValid
                ? ValidationResult<AnalysisRequest>.Valid(this)
                : ValidationResult<AnalysisRequest>.Invalid(ToArray(result.Errors));
        }

        private static ValidationError[] ToArray(IReadOnlyList<ValidationError> errors)
        {
            var result = new ValidationError[errors.Count];
            for (var i = 0; i < errors.Count; i++) result[i] = errors[i];
            return result;
        }
    }

    public static class UnityAssetEvidence
    {
        public static EvidenceItem Create(string evidenceId, string reportSource, string boundedExcerpt, int sizeBytes = 0)
        {
            return new EvidenceItem(evidenceId, UnityAssetAnalystSpecialistFactory.AssetReportEvidenceKind, reportSource, boundedExcerpt, sizeBytes);
        }
    }
}
