using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcp.AgentIntelligentAnalysis;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge
{
    /// <summary>Shared project configuration parser and effective verified-template policy.</summary>
    public static class PiAgentRunnerProjectSettings
    {
        public const string CurrentSchemaVersion = "1.0";

        public static PiAgentRunnerSettings Read(string settingsPath = null)
        {
            var path = settingsPath ?? Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "AgentBridge", "mcp-editor-settings.json");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : PiAgentRunnerSettings.CreateDefaults();
        }

        public static PiAgentRunnerOptions Load() => Resolve(Read());

        public static PiAgentRunnerSettings Parse(string json)
        {
            var document = JObject.Parse(json);
            var allowed = new HashSet<string>(new[] { "piSchemaVersion", "piExecutablePath", "piModel",
                "piTimeoutMilliseconds", "piMaxTurns", "piMaxOutputBytes", "piTools" }, StringComparer.Ordinal);
            var forbidden = new HashSet<string>(new[] { "prompt", "task", "working_directory", "workingDirectory",
                "tools", "model", "rawArguments", "rawPiArguments", "arguments", "shell", "write", "writePath", "writePaths",
                "writePermissions", "network", "networkAllowed", "extensions", "skills", "session", "sessions" }, StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.Properties())
            {
                if ((property.Name.StartsWith("pi", StringComparison.OrdinalIgnoreCase) && !allowed.Contains(property.Name)) ||
                    forbidden.Contains(property.Name))
                    throw new InvalidOperationException("Forbidden Pi configuration field: " + property.Name);
            }

            var schema = ReadString(document, "piSchemaVersion", CurrentSchemaVersion);
            if (schema != CurrentSchemaVersion)
                throw new InvalidOperationException("Unsupported Pi configuration schema: " + schema);
            var settings = new PiAgentRunnerSettings
            {
                ExecutablePath = ReadString(document, "piExecutablePath", PiAgentRunnerSettings.DefaultExecutablePath),
                Model = ReadString(document, "piModel", PiAgentRunnerSettings.DefaultModel),
                TimeoutMilliseconds = ReadInt(document, "piTimeoutMilliseconds", PiAgentRunnerSettings.DefaultTimeoutMilliseconds),
                MaxTurns = ReadInt(document, "piMaxTurns", PiAgentRunnerSettings.DefaultMaxTurns),
                MaxOutputBytes = ReadInt(document, "piMaxOutputBytes", PiAgentRunnerSettings.DefaultMaxOutputBytes)
            };
            if (document.TryGetValue("piTools", out var tools))
            {
                if (tools is not JArray array || array.Any(tool => tool.Type != JTokenType.String))
                    throw new InvalidOperationException("Pi tools must be an array of tool names.");
                settings.Tools = array.Values<string>().ToList();
            }
            return settings;
        }

        public static PiAgentRunnerOptions Resolve(PiAgentRunnerSettings configured,
            RegisteredAnalysisTemplate template = null, int? requestTimeoutMilliseconds = null, bool useEnvironment = true)
        {
            // Validate the saved policy before environment overrides or capability intersection can hide an error.
            if (!PiAgentRunnerSettings.TryValidate(configured, out var error))
                throw new InvalidOperationException(error);
            var effective = configured.Clone();
            if (useEnvironment)
            {
                var path = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_PI_PATH");
                var model = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_PI_MODEL");
                if (!string.IsNullOrWhiteSpace(path)) effective.ExecutablePath = path;
                if (!string.IsNullOrWhiteSpace(model)) effective.Model = model;
            }
            if (template != null)
            {
                var capability = template.EffectiveCapability;
                effective.Tools = effective.Tools.Where(tool => tool == "read"
                    ? capability.Tools.Contains(AbstractToolCapabilities.ProjectRead)
                    : capability.Tools.Contains(AbstractToolCapabilities.ProjectSearch)).ToList();
                effective.MaxTurns = Math.Min(effective.MaxTurns, capability.Budget.MaxTurns);
                effective.MaxOutputBytes = Math.Min(effective.MaxOutputBytes, capability.Budget.MaxOutputBytes);
                effective.TimeoutMilliseconds = Math.Min(effective.TimeoutMilliseconds, capability.Budget.TimeoutSeconds * 1000);
            }
            if (!PiAgentRunnerSettings.TryValidate(effective, out error))
                throw new InvalidOperationException(error);
            if (requestTimeoutMilliseconds.HasValue)
            {
                if (requestTimeoutMilliseconds.Value < 1 || requestTimeoutMilliseconds.Value > 90000)
                    throw new InvalidOperationException("Analysis timeout must be between 1 and 90000 milliseconds.");
            }
            var options = effective.ToOptions(requestTimeoutMilliseconds);
            options.ReadFolders = PiReadPathScope.NormalizeFolders(template == null ? PiReadPathScope.DefaultReadFolders : template.EffectiveCapability.Paths.ReadFolders);
            options.DenyFolders = PiReadPathScope.NormalizeFolders(template == null ? PiReadPathScope.DefaultDenyFolders : template.EffectiveCapability.Paths.DenyFolders);
            return options;
        }

        private static string ReadString(JObject document, string name, string fallback)
        {
            if (!document.TryGetValue(name, out var token)) return fallback;
            if (token.Type != JTokenType.String) throw new InvalidOperationException(name + " must be a string.");
            return token.Value<string>();
        }

        private static int ReadInt(JObject document, string name, int fallback)
        {
            if (!document.TryGetValue(name, out var token)) return fallback;
            if (token.Type != JTokenType.Integer) throw new InvalidOperationException(name + " must be an integer.");
            return token.Value<int>();
        }
    }
}
