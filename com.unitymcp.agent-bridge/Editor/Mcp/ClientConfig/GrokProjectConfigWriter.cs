using System;
using System.IO;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class GrokProjectConfigWriter : IMcpClientConfigWriter
    {
        private readonly ManagedTomlConfigEditor _configEditor;
        private readonly McpPathResolver _pathResolver;

        public GrokProjectConfigWriter()
            : this(new ManagedTomlConfigEditor(), new McpPathResolver())
        {
        }

        internal GrokProjectConfigWriter(ManagedTomlConfigEditor configEditor, McpPathResolver pathResolver)
        {
            _configEditor = configEditor ?? throw new ArgumentNullException(nameof(configEditor));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ManagedBlockApplyResult Apply(McpEditorSettings settings)
        {
            var targetPath = GetTargetPath(settings, _pathResolver);
            if (!CursorProjectConfigWriter.TryBuildLauncherCommand(
                    settings,
                    _pathResolver,
                    out var launcherCommand,
                    out var projectRoot))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    Reason = "launcher_missing",
                };
            }

            return _configEditor.Apply(
                targetPath,
                preservedChildSections => BuildManagedBlockBody(
                    launcherCommand,
                    projectRoot,
                    preservedChildSections),
                createBackup: true);
        }

        public ManagedBlockApplyResult Remove()
        {
            return _configEditor.Remove(GetTargetPath(_pathResolver), createBackup: true);
        }

        public string Preview(McpEditorSettings settings)
        {
            if (!CursorProjectConfigWriter.TryBuildLauncherCommand(
                    settings,
                    _pathResolver,
                    out var launcherCommand,
                    out var projectRoot))
            {
                return "# launcher_missing" + Environment.NewLine +
                       "# Selected Unity Agent Bridge launcher or project binding is unavailable.";
            }

            return new ManagedBlockTextEditor().Apply(
                string.Empty,
                BuildManagedBlockBody(launcherCommand, projectRoot, string.Empty));
        }

        internal static string GetTargetPath(McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            return GetTargetPath(resolver.GetWorkspaceRoot());
        }

        internal static string GetTargetPath(McpEditorSettings settings, McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            return GetTargetPath(resolver.GetWorkspaceRoot(settings));
        }

        internal static string GetTargetPath(string workspaceRoot)
        {
            if (string.IsNullOrEmpty(workspaceRoot))
            {
                throw new InvalidOperationException("Unable to determine MCP workspace root.");
            }

            return Path.Combine(workspaceRoot, ".grok", "config.toml");
        }

        internal static string BuildManagedBlockBody(
            string launcherCommand,
            string projectRoot,
            string preservedChildSections)
        {
            var launcher = EscapeTomlString(launcherCommand);
            var project = EscapeTomlString(NormalizeProjectRoot(projectRoot));
            var body = "[mcp_servers.unity_agent_bridge]" + Environment.NewLine +
                       "command = \"cmd\"" + Environment.NewLine +
                       "args = [\"/d\", \"/s\", \"/c\", \"" + launcher + "\"]" + Environment.NewLine +
                       "enabled = true" + Environment.NewLine +
                       "startup_timeout_sec = 30" + Environment.NewLine +
                       "tool_timeout_sec = 300" + Environment.NewLine + Environment.NewLine +
                       "[mcp_servers.unity_agent_bridge.env]" + Environment.NewLine +
                       "UNITY_AGENT_BRIDGE_PROJECT_PATH = \"" + project + "\"";

            return ManagedTomlConfigEditor.AppendPreservedChildSections(body, preservedChildSections);
        }

        private static string EscapeTomlString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            return string.IsNullOrWhiteSpace(projectRoot)
                ? string.Empty
                : Path.GetFullPath(projectRoot.Trim());
        }
    }
}
