using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.AgentIntelligentAnalysis
{
    public static class IntelligentAnalysisValidator
    {
        public static ValidationResult<SpecialistProfile> ValidateSpecialist(SpecialistProfile specialist)
        {
            var errors = new List<ValidationError>();
            if (specialist == null) return ValidationResult<SpecialistProfile>.Invalid(Error("SPECIALIST_NULL", "Specialist is required."));
            CheckVersion(specialist.SchemaVersion, errors);
            Required(specialist.Id, "SPECIALIST_ID_REQUIRED", "Specialist id is required.", errors);
            Required(specialist.Domain, "SPECIALIST_DOMAIN_REQUIRED", "Specialist domain is required.", errors);
            ValidateCapability(specialist.Authority, errors);
            if (specialist.AnalysisCapabilities.Count == 0) errors.Add(Error("ANALYSIS_CAPABILITY_REQUIRED", "At least one analysis capability is required."));
            if (specialist.AllowedEvidenceKinds.Any(string.IsNullOrWhiteSpace)) errors.Add(Error("EVIDENCE_KIND_INVALID", "Evidence kinds must be non-empty."));
            return errors.Count == 0 ? ValidationResult<SpecialistProfile>.Valid(specialist) : ValidationResult<SpecialistProfile>.Invalid(errors.ToArray());
        }

        public static ValidationResult<RegisteredAnalysisTemplate> ValidateTemplate(VerifiedAnalysisTemplate template, SpecialistProfile specialist)
        {
            var errors = new List<ValidationError>();
            if (template == null) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(Error("TEMPLATE_NULL", "Template is required."));
            if (specialist == null) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(Error("SPECIALIST_NULL", "Registered specialist is required."));
            CheckVersion(template.SchemaVersion, errors);
            Required(template.Id, "TEMPLATE_ID_REQUIRED", "Template id is required.", errors);
            Required(template.SpecialistId, "SPECIALIST_ID_REQUIRED", "Template specialist id is required.", errors);
            Required(template.ProblemType, "PROBLEM_TYPE_REQUIRED", "Problem type is required.", errors);
            if (!string.Equals(template.SpecialistId, specialist.Id, StringComparison.Ordinal)) errors.Add(Error("SPECIALIST_MISMATCH", "Template specialist does not match the registered specialist."));
            ValidateCapability(template.RequestedCapability, errors);
            var effective = Intersect(specialist.Authority, template.RequestedCapability, errors);
            foreach (var kind in template.RequiredEvidenceKinds)
            {
                if (!specialist.AllowedEvidenceKinds.Contains(kind, StringComparer.Ordinal)) errors.Add(Error("EVIDENCE_KIND_NOT_ALLOWED", "Required evidence kind is outside specialist authority: " + kind));
            }
            if (!template.OutputValidationDefined) errors.Add(Error("OUTPUT_VALIDATION_REQUIRED", "Output validation must be defined."));
            if (!template.LifecycleTestsPassed) errors.Add(Error("LIFECYCLE_GATE_REQUIRED", "Lifecycle tests must pass before publication."));
            if (!template.NegativePermissionTestsPassed) errors.Add(Error("NEGATIVE_PERMISSION_GATE_REQUIRED", "Negative permission tests must pass before publication."));
            if (!template.AcceptanceEvidenceRecorded) errors.Add(Error("ACCEPTANCE_GATE_REQUIRED", "Acceptance evidence is required before publication."));
            return errors.Count == 0 ? ValidationResult<RegisteredAnalysisTemplate>.Valid(new RegisteredAnalysisTemplate(template, effective)) : ValidationResult<RegisteredAnalysisTemplate>.Invalid(errors.ToArray());
        }

        public static ValidationResult<EvidenceBundle> ValidateEvidence(EvidenceBundle bundle, CapabilityProfile capability, IEnumerable<string> requiredKinds)
        {
            var errors = new List<ValidationError>();
            if (bundle == null) return ValidationResult<EvidenceBundle>.Invalid(Error("EVIDENCE_NULL", "Evidence bundle is required."));
            CheckVersion(bundle.SchemaVersion, errors);
            if (!bundle.Complete) errors.Add(Error("EVIDENCE_INCOMPLETE", "Evidence bundle is incomplete."));
            var max = capability == null || capability.Budget == null ? 0 : capability.Budget.MaxEvidenceBytes;
            if (max > 0 && bundle.Items.Sum(item => item == null ? 0 : item.SizeBytes) > max) errors.Add(Error("EVIDENCE_LIMIT_EXCEEDED", "Evidence exceeds the configured byte budget."));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in bundle.Items)
            {
                if (item == null) { errors.Add(Error("EVIDENCE_ITEM_NULL", "Evidence items cannot be null.")); continue; }
                Required(item.Id, "EVIDENCE_ID_REQUIRED", "Evidence id is required.", errors);
                Required(item.Kind, "EVIDENCE_KIND_REQUIRED", "Evidence kind is required.", errors);
                Required(item.Source, "EVIDENCE_SOURCE_REQUIRED", "Evidence source is required.", errors);
                if (!ids.Add(item.Id)) errors.Add(Error("EVIDENCE_ID_DUPLICATE", "Evidence ids must be unique: " + item.Id));
            }
            foreach (var kind in requiredKinds ?? Enumerable.Empty<string>()) if (!bundle.Items.Any(item => item != null && string.Equals(item.Kind, kind, StringComparison.Ordinal))) errors.Add(Error("EVIDENCE_REQUIRED_MISSING", "Required evidence is missing: " + kind));
            return errors.Count == 0 ? ValidationResult<EvidenceBundle>.Valid(bundle) : ValidationResult<EvidenceBundle>.Invalid(errors.ToArray());
        }

        public static ValidationResult<ProblemAnalysisResult> ValidateResult(ProblemAnalysisResult result, EvidenceBundle evidence)
        {
            var errors = new List<ValidationError>();
            if (result == null) return ValidationResult<ProblemAnalysisResult>.Invalid(Error("RESULT_NULL", "Analysis result is required."));
            CheckVersion(result.SchemaVersion, errors);
            var ids = new HashSet<string>((evidence ?? new EvidenceBundle(null, false)).Items.Where(item => item != null).Select(item => item.Id), StringComparer.Ordinal);
            ValidateClaims(result.Facts, ids, errors);
            ValidateClaims(result.Hypotheses, ids, errors);
            ValidateClaim(result.RootCause, ids, errors, true);
            ValidateClaims(result.Impact, ids, errors);
            ValidateClaims(result.Recommendations, ids, errors);
            ValidateClaims(result.VerificationPlan, ids, errors);
            return errors.Count == 0 ? ValidationResult<ProblemAnalysisResult>.Valid(result) : ValidationResult<ProblemAnalysisResult>.Invalid(errors.ToArray());
        }

        private static void ValidateClaims(IEnumerable<AnalysisClaim> claims, HashSet<string> ids, List<ValidationError> errors) { foreach (var claim in claims ?? Enumerable.Empty<AnalysisClaim>()) ValidateClaim(claim, ids, errors, true); }
        private static void ValidateClaim(AnalysisClaim claim, HashSet<string> ids, List<ValidationError> errors, bool material)
        {
            if (claim == null) { errors.Add(Error("CLAIM_NULL", "Analysis claims cannot be null.")); return; }
            Required(claim.Text, "CLAIM_TEXT_REQUIRED", "Analysis claim text is required.", errors);
            if (material && claim.EvidenceIds.Count == 0) errors.Add(Error("CLAIM_CITATION_REQUIRED", "Material claims require evidence citations."));
            if (claim.EvidenceIds.Any(id => !ids.Contains(id))) errors.Add(Error("CLAIM_CITATION_UNKNOWN", "Claim references unknown evidence."));
            if (claim.Confidence < 0 || claim.Confidence > 1) errors.Add(Error("CONFIDENCE_INVALID", "Confidence must be between 0 and 1."));
        }

        private static CapabilityProfile Intersect(CapabilityProfile authority, CapabilityProfile requested, List<ValidationError> errors)
        {
            if (authority == null || requested == null) return null;
            var tools = requested.Tools.Distinct(StringComparer.Ordinal).ToList();
            if (tools.Any(tool => !authority.Tools.Contains(tool, StringComparer.Ordinal))) errors.Add(Error("TOOL_NOT_ALLOWED", "Template requests a tool outside specialist authority."));
            if (requested.Model == null || authority.Model == null || !string.Equals(requested.Model.Provider, authority.Model.Provider, StringComparison.Ordinal) || !string.Equals(requested.Model.Model, authority.Model.Model, StringComparison.Ordinal)) errors.Add(Error("MODEL_NOT_ALLOWED", "Template cannot select a different model."));
            var read = NormalizeSubset(requested.Paths.ReadFolders, authority.Paths.ReadFolders, authority.Paths.DenyFolders, errors, "READ_PATH_NOT_ALLOWED");
            var writes = NormalizeSubset(requested.Paths.WriteFiles, authority.Paths.WriteFiles, authority.Paths.DenyFolders, errors, "WRITE_PATH_NOT_ALLOWED");
            var patterns = NormalizeSubset(requested.Paths.WriteFilePatterns, authority.Paths.WriteFilePatterns, authority.Paths.DenyFolders, errors, "WRITE_PATTERN_NOT_ALLOWED");
            if (requested.NetworkAllowed && !authority.NetworkAllowed) errors.Add(Error("NETWORK_NOT_ALLOWED", "Template cannot expand network authority."));
            var budget = new ExecutionBudget(Math.Min(requested.Budget.MaxTurns, authority.Budget.MaxTurns), Math.Min(requested.Budget.MaxEvidenceBytes, authority.Budget.MaxEvidenceBytes), Math.Min(requested.Budget.MaxOutputBytes, authority.Budget.MaxOutputBytes), Math.Min(requested.Budget.TimeoutSeconds, authority.Budget.TimeoutSeconds));
            return new CapabilityProfile(requested.Id, authority.Model, tools, new PathPolicy(read, writes, patterns, authority.Paths.DenyFolders), budget, authority.NetworkAllowed && requested.NetworkAllowed);
        }

        private static IReadOnlyList<string> NormalizeSubset(IEnumerable<string> requested, IReadOnlyList<string> allowed, IReadOnlyList<string> denied, List<ValidationError> errors, string code)
        {
            var result = new List<string>();
            foreach (var raw in requested ?? Enumerable.Empty<string>())
            {
                string path;
                if (!TryNormalize(raw, out path) || path.EndsWith("/", StringComparison.Ordinal) || path == ".") { errors.Add(Error("PATH_INVALID", "Path is not project-relative and bounded: " + raw)); continue; }
                if (denied.Any(rule => IsUnder(path, rule))) { errors.Add(Error("PATH_DENIED", "Path is under a denied folder: " + path)); continue; }
                if (!allowed.Any(rule => IsUnder(path, rule))) errors.Add(Error(code, "Path is outside authority: " + path)); else result.Add(path);
            }
            return result.AsReadOnly();
        }

        private static void ValidateCapability(CapabilityProfile capability, List<ValidationError> errors)
        {
            if (capability == null) { errors.Add(Error("CAPABILITY_REQUIRED", "Capability profile is required.")); return; }
            CheckVersion(capability.SchemaVersion, errors);
            Required(capability.Id, "CAPABILITY_ID_REQUIRED", "Capability id is required.", errors);
            if (capability.Model == null || string.IsNullOrWhiteSpace(capability.Model.Provider) || string.IsNullOrWhiteSpace(capability.Model.Model)) errors.Add(Error("MODEL_REQUIRED", "Provider and model are required."));
            if (capability.Tools.Any(tool => !AbstractToolCapabilities.IsKnown(tool))) errors.Add(Error("TOOL_UNKNOWN", "Only registered abstract tools are allowed."));
            if (capability.Budget == null || capability.Budget.MaxTurns <= 0 || capability.Budget.MaxEvidenceBytes <= 0 || capability.Budget.MaxOutputBytes <= 0 || capability.Budget.TimeoutSeconds <= 0) errors.Add(Error("BUDGET_INVALID", "Execution budgets must be positive."));
            ValidatePaths(capability.Paths, errors);
        }

        private static void ValidatePaths(PathPolicy paths, List<ValidationError> errors)
        {
            if (paths == null) { errors.Add(Error("PATH_POLICY_REQUIRED", "Path policy is required.")); return; }
            foreach (var path in paths.ReadFolders.Concat(paths.WriteFiles).Concat(paths.WriteFilePatterns).Concat(paths.DenyFolders)) { string normalized; if (!TryNormalize(path, out normalized)) errors.Add(Error("PATH_INVALID", "Path must be project-relative: " + path)); }
            foreach (var path in paths.WriteFiles.Concat(paths.WriteFilePatterns)) if (string.IsNullOrWhiteSpace(path) || path.EndsWith("/", StringComparison.Ordinal) || path == ".") errors.Add(Error("BROAD_WRITE", "Directory-wide writes are not allowed: " + path));
        }

        private static bool TryNormalize(string raw, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf(':') >= 0 || raw.StartsWith("/", StringComparison.Ordinal) || raw.StartsWith("\\", StringComparison.Ordinal)) return false;
            var pieces = raw.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var clean = new List<string>();
            foreach (var piece in pieces) { if (piece == "..") return false; if (piece != ".") clean.Add(piece); }
            normalized = string.Join("/", clean.ToArray());
            return normalized.Length > 0;
        }

        private static bool IsUnder(string path, string folder)
        {
            string normalized;
            if (!TryNormalize(folder, out normalized)) return false;
            return string.Equals(path, normalized, StringComparison.Ordinal) || path.StartsWith(normalized + "/", StringComparison.Ordinal);
        }

        private static void CheckVersion(int version, List<ValidationError> errors) { if (version != AnalysisSchema.CurrentVersion) errors.Add(Error("UNSUPPORTED_SCHEMA_VERSION", "Unsupported schema version: " + version)); }
        private static void Required(string value, string code, string message, List<ValidationError> errors) { if (string.IsNullOrWhiteSpace(value)) errors.Add(Error(code, message)); }
        private static ValidationError Error(string code, string message) => new ValidationError(code, message);
    }
}
