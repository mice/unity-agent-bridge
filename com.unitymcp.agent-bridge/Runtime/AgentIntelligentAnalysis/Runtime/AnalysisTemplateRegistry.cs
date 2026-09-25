using System;
using System.Collections.Generic;

namespace UnityMcp.AgentIntelligentAnalysis
{
    public sealed class AnalysisTemplateRegistry
    {
        private readonly Dictionary<string, SpecialistProfile> specialists = new Dictionary<string, SpecialistProfile>(StringComparer.Ordinal);
        private readonly Dictionary<string, RegisteredAnalysisTemplate> templates = new Dictionary<string, RegisteredAnalysisTemplate>(StringComparer.Ordinal);

        public ValidationResult<SpecialistProfile> RegisterSpecialist(SpecialistProfile specialist)
        {
            var result = IntelligentAnalysisValidator.ValidateSpecialist(specialist);
            if (!result.IsValid) return result;
            specialists[specialist.Id] = specialist;
            return result;
        }

        public ValidationResult<RegisteredAnalysisTemplate> RegisterTemplate(VerifiedAnalysisTemplate template)
        {
            if (template != null && string.Equals(template.Id, "agent.run", StringComparison.Ordinal)) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(new ValidationError("GENERIC_AGENT_RUN_FORBIDDEN", "Generic agent.run is not a verified analysis template."));
            SpecialistProfile specialist;
            if (template == null || !specialists.TryGetValue(template.SpecialistId, out specialist)) return ValidationResult<RegisteredAnalysisTemplate>.Invalid(new ValidationError("SPECIALIST_NOT_REGISTERED", "Template specialist is not registered."));
            var result = IntelligentAnalysisValidator.ValidateTemplate(template, specialist);
            if (result.IsValid) templates[template.Id] = result.Value;
            return result;
        }

        public RegisteredAnalysisTemplate Get(string id)
        {
            RegisteredAnalysisTemplate value;
            return id != null && templates.TryGetValue(id, out value) ? value : null;
        }

        public IReadOnlyList<RegisteredAnalysisTemplate> List()
        {
            return new List<RegisteredAnalysisTemplate>(templates.Values).AsReadOnly();
        }
    }
}
