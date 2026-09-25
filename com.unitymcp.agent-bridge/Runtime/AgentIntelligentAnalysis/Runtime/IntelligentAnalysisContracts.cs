using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.AgentIntelligentAnalysis
{
    public static class AnalysisSchema
    {
        public const int CurrentVersion = 1;
    }

    public static class AbstractToolCapabilities
    {
        public const string ProjectRead = "project.read";
        public const string ProjectSearch = "project.search";
        public const string UnityCompileReport = "unity.compile.report";
        public const string UnityTestReport = "unity.test.report";
        public const string ConsoleLogReport = "unity.console.report";
        public const string AssetReport = "unity.asset.report";

        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        {
            ProjectRead, ProjectSearch, UnityCompileReport, UnityTestReport,
            ConsoleLogReport, AssetReport
        };

        public static bool IsKnown(string value) => !string.IsNullOrWhiteSpace(value) && Known.Contains(value);
    }

    public sealed class ValidationError
    {
        public ValidationError(string code, string message)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "INVALID" : code;
            Message = message ?? string.Empty;
        }

        public string Code { get; }
        public string Message { get; }
        public override string ToString() => Code + ": " + Message;
    }

    public sealed class ValidationResult<T>
    {
        private ValidationResult(T value, IEnumerable<ValidationError> errors)
        {
            Value = value;
            Errors = new List<ValidationError>(errors ?? Enumerable.Empty<ValidationError>()).AsReadOnly();
        }

        public T Value { get; }
        public IReadOnlyList<ValidationError> Errors { get; }
        public bool IsValid => Errors.Count == 0;
        public static ValidationResult<T> Valid(T value) => new ValidationResult<T>(value, null);
        public static ValidationResult<T> Invalid(params ValidationError[] errors) => new ValidationResult<T>(default(T), errors);
    }

    public sealed class ModelProfile
    {
        public ModelProfile(string provider, string model, bool callerOverrideAllowed = false)
        {
            Provider = provider ?? string.Empty;
            Model = model ?? string.Empty;
            CallerOverrideAllowed = callerOverrideAllowed;
        }

        public string Provider { get; }
        public string Model { get; }
        public bool CallerOverrideAllowed { get; }
    }

    public sealed class ExecutionBudget
    {
        public ExecutionBudget(int maxTurns = 8, int maxEvidenceBytes = 262144, int maxOutputBytes = 65536, int timeoutSeconds = 120)
        {
            MaxTurns = maxTurns;
            MaxEvidenceBytes = maxEvidenceBytes;
            MaxOutputBytes = maxOutputBytes;
            TimeoutSeconds = timeoutSeconds;
        }

        public int MaxTurns { get; }
        public int MaxEvidenceBytes { get; }
        public int MaxOutputBytes { get; }
        public int TimeoutSeconds { get; }
    }

    public sealed class PathPolicy
    {
        public PathPolicy(IEnumerable<string> readFolders = null, IEnumerable<string> writeFiles = null,
            IEnumerable<string> writeFilePatterns = null, IEnumerable<string> denyFolders = null)
        {
            ReadFolders = Copy(readFolders);
            WriteFiles = Copy(writeFiles);
            WriteFilePatterns = Copy(writeFilePatterns);
            DenyFolders = Copy(denyFolders);
        }

        public IReadOnlyList<string> ReadFolders { get; }
        public IReadOnlyList<string> WriteFiles { get; }
        public IReadOnlyList<string> WriteFilePatterns { get; }
        public IReadOnlyList<string> DenyFolders { get; }

        private static IReadOnlyList<string> Copy(IEnumerable<string> values)
            => new List<string>(values ?? Enumerable.Empty<string>()).AsReadOnly();
    }

    public sealed class CapabilityProfile
    {
        public CapabilityProfile(string id, ModelProfile model, IEnumerable<string> tools,
            PathPolicy paths = null, ExecutionBudget budget = null, bool networkAllowed = false, int schemaVersion = AnalysisSchema.CurrentVersion)
        {
            Id = id ?? string.Empty;
            SchemaVersion = schemaVersion;
            Model = model;
            Tools = new List<string>(tools ?? Enumerable.Empty<string>()).AsReadOnly();
            Paths = paths ?? new PathPolicy();
            Budget = budget ?? new ExecutionBudget();
            NetworkAllowed = networkAllowed;
        }

        public string Id { get; }
        public int SchemaVersion { get; }
        public ModelProfile Model { get; }
        public IReadOnlyList<string> Tools { get; }
        public PathPolicy Paths { get; }
        public ExecutionBudget Budget { get; }
        public bool NetworkAllowed { get; }
    }

    public sealed class SpecialistProfile
    {
        public SpecialistProfile(string id, string domain, IEnumerable<string> analysisCapabilities,
            IEnumerable<string> evidenceKinds, CapabilityProfile authority, int schemaVersion = AnalysisSchema.CurrentVersion)
        {
            Id = id ?? string.Empty;
            Domain = domain ?? string.Empty;
            SchemaVersion = schemaVersion;
            AnalysisCapabilities = new List<string>(analysisCapabilities ?? Enumerable.Empty<string>()).AsReadOnly();
            AllowedEvidenceKinds = new List<string>(evidenceKinds ?? Enumerable.Empty<string>()).AsReadOnly();
            Authority = authority;
        }

        public string Id { get; }
        public string Domain { get; }
        public int SchemaVersion { get; }
        public IReadOnlyList<string> AnalysisCapabilities { get; }
        public IReadOnlyList<string> AllowedEvidenceKinds { get; }
        public CapabilityProfile Authority { get; }
    }

    public sealed class EvidenceItem
    {
        public EvidenceItem(string id, string kind, string source, string excerpt = null, int sizeBytes = 0)
        {
            Id = id ?? string.Empty;
            Kind = kind ?? string.Empty;
            Source = source ?? string.Empty;
            Excerpt = excerpt ?? string.Empty;
            SizeBytes = sizeBytes > 0 ? sizeBytes : Excerpt.Length;
        }

        public string Id { get; }
        public string Kind { get; }
        public string Source { get; }
        public string Excerpt { get; }
        public int SizeBytes { get; }
    }

    public sealed class EvidenceBundle
    {
        public EvidenceBundle(IEnumerable<EvidenceItem> items, bool complete, int schemaVersion = AnalysisSchema.CurrentVersion)
        {
            SchemaVersion = schemaVersion;
            Items = new List<EvidenceItem>(items ?? Enumerable.Empty<EvidenceItem>()).AsReadOnly();
            Complete = complete;
        }

        public int SchemaVersion { get; }
        public IReadOnlyList<EvidenceItem> Items { get; }
        public bool Complete { get; }
    }

    public sealed class AnalysisClaim
    {
        public AnalysisClaim(string text, IEnumerable<string> evidenceIds, double confidence = 1.0)
        {
            Text = text ?? string.Empty;
            EvidenceIds = new List<string>(evidenceIds ?? Enumerable.Empty<string>()).AsReadOnly();
            Confidence = confidence;
        }

        public string Text { get; }
        public IReadOnlyList<string> EvidenceIds { get; }
        public double Confidence { get; }
    }

    public sealed class ProblemAnalysisResult
    {
        public ProblemAnalysisResult(IEnumerable<AnalysisClaim> facts, IEnumerable<AnalysisClaim> hypotheses,
            AnalysisClaim rootCause, IEnumerable<AnalysisClaim> impact, IEnumerable<AnalysisClaim> recommendations,
            IEnumerable<AnalysisClaim> verificationPlan, int schemaVersion = AnalysisSchema.CurrentVersion)
        {
            SchemaVersion = schemaVersion;
            Facts = Copy(facts);
            Hypotheses = Copy(hypotheses);
            RootCause = rootCause;
            Impact = Copy(impact);
            Recommendations = Copy(recommendations);
            VerificationPlan = Copy(verificationPlan);
        }

        public int SchemaVersion { get; }
        public IReadOnlyList<AnalysisClaim> Facts { get; }
        public IReadOnlyList<AnalysisClaim> Hypotheses { get; }
        public AnalysisClaim RootCause { get; }
        public IReadOnlyList<AnalysisClaim> Impact { get; }
        public IReadOnlyList<AnalysisClaim> Recommendations { get; }
        public IReadOnlyList<AnalysisClaim> VerificationPlan { get; }

        private static IReadOnlyList<AnalysisClaim> Copy(IEnumerable<AnalysisClaim> values)
            => new List<AnalysisClaim>(values ?? Enumerable.Empty<AnalysisClaim>()).AsReadOnly();
    }

    public sealed class VerifiedAnalysisTemplate
    {
        public VerifiedAnalysisTemplate(string id, string specialistId, string problemType,
            CapabilityProfile requestedCapability, IEnumerable<string> requiredEvidenceKinds,
            bool outputValidationDefined, bool lifecycleTestsPassed, bool negativePermissionTestsPassed,
            bool acceptanceEvidenceRecorded, int schemaVersion = AnalysisSchema.CurrentVersion)
        {
            Id = id ?? string.Empty;
            SpecialistId = specialistId ?? string.Empty;
            ProblemType = problemType ?? string.Empty;
            SchemaVersion = schemaVersion;
            RequestedCapability = requestedCapability;
            RequiredEvidenceKinds = new List<string>(requiredEvidenceKinds ?? Enumerable.Empty<string>()).AsReadOnly();
            OutputValidationDefined = outputValidationDefined;
            LifecycleTestsPassed = lifecycleTestsPassed;
            NegativePermissionTestsPassed = negativePermissionTestsPassed;
            AcceptanceEvidenceRecorded = acceptanceEvidenceRecorded;
        }

        public string Id { get; }
        public string SpecialistId { get; }
        public string ProblemType { get; }
        public int SchemaVersion { get; }
        public CapabilityProfile RequestedCapability { get; }
        public IReadOnlyList<string> RequiredEvidenceKinds { get; }
        public bool OutputValidationDefined { get; }
        public bool LifecycleTestsPassed { get; }
        public bool NegativePermissionTestsPassed { get; }
        public bool AcceptanceEvidenceRecorded { get; }
    }

    public sealed class RegisteredAnalysisTemplate
    {
        internal RegisteredAnalysisTemplate(VerifiedAnalysisTemplate template, CapabilityProfile effectiveCapability)
        {
            Template = template;
            EffectiveCapability = effectiveCapability;
        }

        public VerifiedAnalysisTemplate Template { get; }
        public CapabilityProfile EffectiveCapability { get; }
    }
}
