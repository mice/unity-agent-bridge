using System;
using System.Collections.Generic;

namespace UnityMcp.AgentIntelligentAnalysis
{
    /// <summary>Creates the first bounded, runtime-neutral Unity programmer analysis slice.</summary>
    public static class UnityProgrammerSpecialistFactory
    {
        public const string SpecialistId = "unity-programmer";
        public const string TemplateId = "unity_compile_failure_analysis";
        public const string ProblemType = "compile-failure";
        public const string CompileReportEvidenceKind = "compile-report";

        public static SpecialistProfile CreateSpecialist()
        {
            var authority = new CapabilityProfile(
                "unity-programmer-authority",
                new ModelProfile("deepseek", "deepseek-flash"),
                new[] { AbstractToolCapabilities.ProjectRead, AbstractToolCapabilities.ProjectSearch, AbstractToolCapabilities.UnityCompileReport },
                new PathPolicy(new[] { "Assets", "Packages", "ProjectSettings", "Documentation~" }, null, null, new[] { "Library", "Temp", "Logs", "Build" }),
                new ExecutionBudget(8, 262144, 65536, 120),
                networkAllowed: false);

            return new SpecialistProfile(
                SpecialistId,
                "Unity C# programming, compilation, and editor tooling",
                new[] { "problem-analysis", "compile-failure-analysis" },
                new[] { CompileReportEvidenceKind },
                authority);
        }

        public static VerifiedAnalysisTemplate CreateCompileFailureTemplate()
        {
            var authority = CreateSpecialist().Authority;
            return new VerifiedAnalysisTemplate(
                TemplateId,
                SpecialistId,
                ProblemType,
                new CapabilityProfile(
                    "unity-compile-failure-capability",
                    authority.Model,
                    new[] { AbstractToolCapabilities.ProjectRead, AbstractToolCapabilities.ProjectSearch, AbstractToolCapabilities.UnityCompileReport },
                    new PathPolicy(new[] { "Assets", "Packages", "ProjectSettings", "Documentation~" }),
                    new ExecutionBudget(6, 131072, 32768, 90),
                    networkAllowed: false),
                new[] { CompileReportEvidenceKind },
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
            return registry.RegisterTemplate(CreateCompileFailureTemplate());
        }

        private static ValidationError[] ToArray(IReadOnlyList<ValidationError> errors)
        {
            var result = new ValidationError[errors.Count];
            for (var i = 0; i < errors.Count; i++) result[i] = errors[i];
            return result;
        }
    }

    public sealed class CompileFailureAnalysisRequest : AnalysisRequest
    {
        public CompileFailureAnalysisRequest(string problemId, IEnumerable<string> evidenceIds)
        {
            ProblemId = problemId ?? string.Empty;
            EvidenceIds = new List<string>(evidenceIds ?? Array.Empty<string>()).AsReadOnly();
        }

        public override string ProblemId { get; }
        public override IReadOnlyList<string> EvidenceIds { get; }

        public ValidationResult<CompileFailureAnalysisRequest> Validate()
        {
            if (string.IsNullOrWhiteSpace(ProblemId)) return ValidationResult<CompileFailureAnalysisRequest>.Invalid(new ValidationError("PROBLEM_ID_REQUIRED", "Problem id is required."));
            if (EvidenceIds.Count == 0) return ValidationResult<CompileFailureAnalysisRequest>.Invalid(new ValidationError("EVIDENCE_REFERENCE_REQUIRED", "At least one evidence reference is required."));
            return ValidationResult<CompileFailureAnalysisRequest>.Valid(this);
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

    public static class UnityCompileEvidence
    {
        public static EvidenceItem Create(string evidenceId, string reportSource, string boundedExcerpt, int sizeBytes = 0)
        {
            return new EvidenceItem(evidenceId, UnityProgrammerSpecialistFactory.CompileReportEvidenceKind, reportSource, boundedExcerpt, sizeBytes);
        }
    }
}
