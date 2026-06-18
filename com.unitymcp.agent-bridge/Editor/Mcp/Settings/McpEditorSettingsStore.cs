using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpEditorSettingsStore : IMcpEditorSettingsStore
    {
        internal const string CurrentSchemaVersion = "1.0";
        internal const int DefaultDiagnosticTimeoutMs = 30000;

        private readonly string _settingsFilePath;

        public McpEditorSettingsStore()
            : this(GetDefaultSettingsFilePath())
        {
        }

        public McpEditorSettingsStore(string settingsFilePath)
        {
            if (string.IsNullOrWhiteSpace(settingsFilePath))
            {
                throw new ArgumentException("settingsFilePath must not be empty.", nameof(settingsFilePath));
            }

            _settingsFilePath = settingsFilePath;
        }

        public McpEditorSettings Load()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return McpEditorSettingsDefaults.Create();
            }

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var dto = CreateLoadDto();
                JsonUtility.FromJsonOverwrite(json, dto);
                return Normalize(dto);
            }
            catch (Exception)
            {
                return McpEditorSettingsDefaults.Create();
            }
        }

        public void Save(McpEditorSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var normalized = Normalize(settings);
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonUtility.ToJson(ToDto(normalized), true);
            File.WriteAllText(_settingsFilePath, json);
        }

        internal static string GetDefaultSettingsFilePath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Unable to determine Unity project root from Application.dataPath.");
            }

            return Path.Combine(projectRoot, "Library", "AgentBridge", "mcp-editor-settings.json");
        }

        internal static McpEditorSettings Normalize(McpEditorSettings settings)
        {
            if (settings == null)
            {
                return McpEditorSettingsDefaults.Create();
            }

            return Normalize(ToDto(settings));
        }

        internal static McpEditorSettings Normalize(McpEditorSettingsFileDto dto)
        {
            var defaults = McpEditorSettingsDefaults.Create();
            if (dto == null)
            {
                return defaults;
            }

            return new McpEditorSettings
            {
                SchemaVersion = string.Equals(dto.schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal)
                    ? CurrentSchemaVersion
                    : CurrentSchemaVersion,
                DotnetPath = NormalizePathValue(dto.dotnetPath),
                WorkspaceRoot = NormalizePathValue(dto.workspaceRoot),
                ToolsRoot = NormalizePathValue(dto.toolsRoot),
                McpServerRoot = NormalizePathValue(dto.mcpServerRoot),
                CliExecutablePath = NormalizePathValue(dto.cliExecutablePath),
                PreferPublishedCli = dto.preferPublishedCli,
                DiagnosticTimeoutMs = NormalizePositive(dto.diagnosticTimeoutMs, defaults.DiagnosticTimeoutMs),
            };
        }

        private static McpEditorSettingsFileDto ToDto(McpEditorSettings settings)
        {
            return new McpEditorSettingsFileDto
            {
                schemaVersion = CurrentSchemaVersion,
                dotnetPath = NormalizePathValue(settings.DotnetPath),
                workspaceRoot = NormalizePathValue(settings.WorkspaceRoot),
                toolsRoot = NormalizePathValue(settings.ToolsRoot),
                mcpServerRoot = NormalizePathValue(settings.McpServerRoot),
                cliExecutablePath = NormalizePathValue(settings.CliExecutablePath),
                preferPublishedCli = settings.PreferPublishedCli,
                diagnosticTimeoutMs = NormalizePositive(settings.DiagnosticTimeoutMs, DefaultDiagnosticTimeoutMs),
            };
        }

        private static string NormalizePathValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static int NormalizePositive(int value, int fallback)
        {
            if (value <= 0)
            {
                return fallback;
            }

            return value;
        }

        private static McpEditorSettingsFileDto CreateLoadDto()
        {
            return new McpEditorSettingsFileDto();
        }

        [Serializable]
        internal sealed class McpEditorSettingsFileDto
        {
            public string schemaVersion;
            public string dotnetPath;
            public string workspaceRoot;
            public string toolsRoot;
            public string mcpServerRoot;
            public string cliExecutablePath;
            public bool preferPublishedCli;
            public int diagnosticTimeoutMs;
        }
    }

    internal static class McpEditorSettingsDefaults
    {
        public static McpEditorSettings Create()
        {
            return new McpEditorSettings
            {
                SchemaVersion = McpEditorSettingsStore.CurrentSchemaVersion,
                DotnetPath = string.Empty,
                WorkspaceRoot = string.Empty,
                ToolsRoot = string.Empty,
                McpServerRoot = string.Empty,
                CliExecutablePath = string.Empty,
                PreferPublishedCli = false,
                DiagnosticTimeoutMs = McpEditorSettingsStore.DefaultDiagnosticTimeoutMs,
            };
        }
    }
}
