using System;
using System.Collections.Generic;
using System.Linq;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentIntelligentAnalysis;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class PiAgentTemplateCatalogRow
    {
        internal PiAgentTemplateCatalogRow(string id, string specialistId, string problemType, string evidenceKind, IReadOnlyList<string> readFolders, IReadOnlyList<string> denyFolders, IReadOnlyList<string> tools, int maxTurns, int maxEvidenceBytes, int maxOutputBytes, int timeoutMilliseconds, string validationError, string executablePath = null, string model = null)
        {
            Id = id; SpecialistId = specialistId; ProblemType = problemType; EvidenceKind = evidenceKind;
            ReadFolders = readFolders; DenyFolders = denyFolders; Tools = tools; MaxTurns = maxTurns;
            MaxEvidenceBytes = maxEvidenceBytes; MaxOutputBytes = maxOutputBytes; TimeoutMilliseconds = timeoutMilliseconds; ValidationError = validationError;
            ExecutablePath = executablePath; Model = model;
        }
        public string Id { get; }
        public string SpecialistId { get; }
        public string ProblemType { get; }
        public string EvidenceKind { get; }
        public IReadOnlyList<string> ReadFolders { get; }
        public IReadOnlyList<string> DenyFolders { get; }
        public IReadOnlyList<string> Tools { get; }
        public int MaxTurns { get; }
        public int MaxEvidenceBytes { get; }
        public int MaxOutputBytes { get; }
        public int TimeoutMilliseconds { get; }
        public int TimeoutSeconds => TimeoutMilliseconds / 1000;
        public string ExecutablePath { get; }
        public string Model { get; }
        public string ValidationError { get; }
        public bool IsValid => string.IsNullOrWhiteSpace(ValidationError);
        public string WritePolicy => "none";
        public string NetworkPolicy => "disabled";
    }

    public static class PiAgentTemplateCatalog
    {
        public static IReadOnlyList<PiAgentTemplateCatalogRow> List(McpEditorSettings settings = null)
        {
            settings = settings ?? McpEditorSettingsDefaults.Create();
            var rows = new List<PiAgentTemplateCatalogRow>();
            Add(rows, UnityProgrammerSpecialistFactory.TemplateId, registry => UnityProgrammerSpecialistFactory.Register(registry), settings);
            Add(rows, UnityAssetAnalystSpecialistFactory.TemplateId, registry => UnityAssetAnalystSpecialistFactory.Register(registry), settings);
            return rows.AsReadOnly();
        }

        private static void Add(ICollection<PiAgentTemplateCatalogRow> rows, string expectedId, Func<AnalysisTemplateRegistry, ValidationResult<RegisteredAnalysisTemplate>> register, McpEditorSettings settings)
        {
            var registry = new AnalysisTemplateRegistry();
            var result = register(registry);
            var template = registry.Get(expectedId);
            if (!result.IsValid || template == null)
            {
                var error = result.Errors.FirstOrDefault();
                rows.Add(new PiAgentTemplateCatalogRow(expectedId, string.Empty, string.Empty, string.Empty, new string[0], new string[0], new string[0], 0, 0, 0, 0, error == null ? "Template did not register." : error.Code + ": " + error.Message));
                return;
            }
            var capability = template.EffectiveCapability;
            PiAgentRunnerOptions options = null;
            string validationError = null;
            try
            {
                if (!PiAgentConfigurationValidator.TryValidate(settings, out validationError))
                    throw new InvalidOperationException(validationError);
                options = PiAgentRunnerProjectSettings.Resolve(PiAgentConfigurationValidator.ToRunnerSettings(settings), template);
            }
            catch (Exception exception) { validationError = "PI_CONFIGURATION_INVALID: " + exception.Message; }
            rows.Add(new PiAgentTemplateCatalogRow(template.Template.Id, template.Template.SpecialistId,
                template.Template.ProblemType, template.Template.RequiredEvidenceKinds.FirstOrDefault() ?? string.Empty,
                Array.AsReadOnly(capability.Paths.ReadFolders.ToArray()), Array.AsReadOnly(capability.Paths.DenyFolders.ToArray()),
                Array.AsReadOnly(options?.Tools.ToArray() ?? Array.Empty<string>()), options?.MaxTurns ?? 0,
                capability.Budget.MaxEvidenceBytes, options?.MaxOutputBytes ?? 0, options?.TimeoutMilliseconds ?? 0,
                validationError, options?.ExecutablePath, options?.Model));
        }
    }

    public static class PiAgentConfigurationValidator
    {
        public static bool TryValidate(McpEditorSettings settings, out string error)
        {
            if (settings == null) { error = "Pi settings are unavailable."; return false; }
            if (!string.IsNullOrWhiteSpace(settings.PiLoadError)) { error = settings.PiLoadError; return false; }
            if (settings.PiSchemaVersion != PiAgentRunnerProjectSettings.CurrentSchemaVersion)
            { error = "Unsupported Pi configuration schema: " + settings.PiSchemaVersion; return false; }
            return PiAgentRunnerSettings.TryValidate(ToRunnerSettings(settings), out error);
        }

        public static PiAgentRunnerSettings ToRunnerSettings(McpEditorSettings settings)
        {
            return new PiAgentRunnerSettings
            {
                ExecutablePath = settings.PiExecutablePath,
                Model = settings.PiModel,
                TimeoutMilliseconds = settings.PiTimeoutMilliseconds,
                MaxTurns = settings.PiMaxTurns,
                MaxOutputBytes = settings.PiMaxOutputBytes,
                Tools = settings.PiTools == null ? null : new List<string>(settings.PiTools)
            };
        }

    }
}
