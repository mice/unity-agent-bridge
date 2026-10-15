using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentTaskTemplateCatalogEntry
    {
        internal AgentTaskTemplateCatalogEntry(AgentTaskTemplateDefinition definition, IEnumerable<string> diagnostics)
        { Definition = definition; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
        public AgentTaskTemplateDefinition Definition { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public bool Available => Definition.Enabled && Diagnostics.Count == 0;
    }

    public sealed class AgentTaskTemplateCatalog
    {
        public const string AnalysisInputSchema = "{\"type\":\"object\",\"properties\":{\"problemId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256},\"evidenceIds\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":16,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256}},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":90000}},\"required\":[\"problemId\",\"evidenceIds\"],\"additionalProperties\":false}";
        private readonly IReadOnlyList<AgentTaskTemplateCatalogEntry> entries;
        private AgentTaskTemplateCatalog(IEnumerable<AgentTaskTemplateCatalogEntry> entries, IEnumerable<string> diagnostics)
        { this.entries = Array.AsReadOnly(entries.OrderBy(e => e.Definition.Id, StringComparer.Ordinal).ToArray()); SourceDiagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
        public IReadOnlyList<AgentTaskTemplateCatalogEntry> Entries => entries;
        public IReadOnlyList<string> SourceDiagnostics { get; }
        public static AgentTaskTemplateCatalog Load(string projectRoot, IEnumerable<AgentTaskTemplateDefinition> builtins)
        {
            var definitions = new List<AgentTaskTemplateDefinition>(builtins);
            var errors = new Dictionary<AgentTaskTemplateDefinition, List<string>>();
            var sourceErrors = new List<string>();
            var path = Path.Combine(projectRoot, "ProjectSettings", "AgentTaskTemplates.json");
            if (File.Exists(path))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    if (Encoding.UTF8.GetByteCount(text) > 1048576) throw new ArgumentException("Configuration exceeds 1 MiB.");
                    var root = ParseObject(text);
                    Exact(root, "schemaVersion", "templates");
                    if (root["schemaVersion"]?.Type != JTokenType.Integer || root.Value<int>("schemaVersion") != 1 || !(root["templates"] is JArray rows) || rows.Count > 256) throw new ArgumentException("Expected schemaVersion:1 and at most 256 templates.");
                    for (var index = 0; index < rows.Count; index++)
                    {
                        var rowErrors = new List<string>();
                        var row = rows[index] as JObject;
                        var id = row?["id"]?.Type == JTokenType.String ? (string)row["id"] : "@invalid-row-" + index;
                        AgentTaskTemplateDefinition definition;
                        try
                        {
                            if (row == null) throw new ArgumentException("Template row must be an object.");
                            Exact(row, "id", "displayName", "description", "enabled", "executorId", "inputSchema", "outputSchema", "execution");
                            foreach (var key in new[] { "id", "displayName", "description", "executorId" })
                                if (row[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)row[key]) || ((string)row[key]).Length > (key == "description" ? 4096 : 256)) throw new ArgumentException("Invalid " + key);
                            if (!Regex.IsMatch(id, "^[a-z][a-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid template ID.");
                            if (row["enabled"]?.Type != JTokenType.Boolean) throw new ArgumentException("enabled must be boolean.");
                            AgentTaskTemplateSchema.Check(row["inputSchema"]); AgentTaskTemplateSchema.Check(row["outputSchema"]);
                            if (row["inputSchema"].Value<string>("type") != "object" || !(row["execution"] is JObject)) throw new ArgumentException("Object inputs and execution are required.");
                            definition = new AgentTaskTemplateDefinition(id, (string)row["displayName"], (string)row["description"], (bool)row["enabled"], (string)row["executorId"], Compact(row["inputSchema"]), Compact(row["outputSchema"]), Compact(row["execution"]), "configured", Hash(Compact(row)));
                        }
                        catch (Exception exception)
                        {
                            rowErrors.Add("CONFIG_ROW_INVALID: " + exception.Message);
                            definition = new AgentTaskTemplateDefinition(id, id, "Invalid configured row " + index, false, row?["executorId"]?.Type == JTokenType.String ? (string)row["executorId"] : "", "{}", "{}", "{}", "configured", Hash(rows[index].ToString(Formatting.None)));
                        }
                        definitions.Add(definition); errors[definition] = rowErrors;
                    }
                }
                catch (Exception exception) { sourceErrors.Add("CONFIG_SOURCE_INVALID: " + exception.Message); }
            }
            var conflicts = new HashSet<string>(definitions.GroupBy(d => d.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            var result = new List<AgentTaskTemplateCatalogEntry>();
            foreach (var definition in definitions)
            {
                var diagnostics = errors.TryGetValue(definition, out var rowErrors) ? rowErrors : new List<string>();
                if (conflicts.Contains(definition.Id)) diagnostics.Add("TEMPLATE_ID_CONFLICT");
                if (!definition.Enabled) diagnostics.Add("TEMPLATE_DISABLED");
                var executor = AgentTaskTemplateExecutors.Resolve(definition.ExecutorId);
                if (executor == null) diagnostics.Add("EXECUTOR_UNAVAILABLE_OR_CONFLICT");
                else if (diagnostics.Count == 0)
                {
                    try
                    {
                        var schema = ParseObject(executor.Executor.ConfigurationSchemaJson);
                        AgentTaskTemplateSchema.Check(schema);
                        AgentTaskTemplateSchema.Validate(schema, ParseObject(definition.Execution));
                        var reason = executor.Executor.Validate(definition);
                        if (!string.IsNullOrWhiteSpace(reason)) diagnostics.Add(reason);
                    }
                    catch (Exception exception) { diagnostics.Add("EXECUTOR_CONFIGURATION_INVALID: " + exception.Message); }
                }
                result.Add(new AgentTaskTemplateCatalogEntry(definition, diagnostics));
            }
            return new AgentTaskTemplateCatalog(result, sourceErrors);
        }

        public AgentTaskTemplateAdmission Admit(string payload)
        {
            var args = ParseObject(payload);
            Exact(args, "templateId", "inputs");
            if (args["templateId"]?.Type != JTokenType.String || !(args["inputs"] is JObject)) throw new ArgumentException("templateId string and inputs object are required.");
            var matches = entries.Where(e => e.Definition.Id == (string)args["templateId"]).ToArray();
            if (matches.Length != 1 || !matches[0].Available) throw new ArgumentException("TEMPLATE_UNAVAILABLE: " + (matches.Length == 0 ? "unknown ID" : string.Join(", ", matches.SelectMany(e => e.Diagnostics))));
            var definition = matches[0].Definition;
            AgentTaskTemplateSchema.Validate(ParseObject(definition.InputSchema), args["inputs"]);
            var executor = AgentTaskTemplateExecutors.Resolve(definition.ExecutorId);
            if (executor == null) throw new ArgumentException("EXECUTOR_UNAVAILABLE");
            return new AgentTaskTemplateAdmission(definition, Compact(args["inputs"]), executor.Executor);
        }

        public string ToJson() => JsonConvert.SerializeObject(new
        {
            schemaVersion = 1, sourceDiagnostics = SourceDiagnostics,
            templates = entries.Select(e => new
            {
                id = e.Definition.Id, displayName = e.Definition.DisplayName, description = e.Definition.Description,
                source = e.Definition.Source, version = e.Definition.Version, executorId = e.Definition.ExecutorId,
                inputSchema = ParseObject(e.Definition.InputSchema), outputSchema = ParseObject(e.Definition.OutputSchema),
                dedicatedEntrypoint = e.Definition.DedicatedEntrypoint, enabled = e.Definition.Enabled, available = e.Available, diagnostics = e.Diagnostics
            })
        });
        public static JObject ParseObject(string text) => JObject.Parse(text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        private static void Exact(JObject obj, params string[] keys)
        { if (obj.Properties().Any(p => !keys.Contains(p.Name)) || keys.Any(k => obj[k] == null)) throw new ArgumentException("Required/unknown properties: " + string.Join(", ", keys)); }
        private static string Compact(JToken token) => token.ToString(Formatting.None);
        private static string Hash(string text)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    }
}
