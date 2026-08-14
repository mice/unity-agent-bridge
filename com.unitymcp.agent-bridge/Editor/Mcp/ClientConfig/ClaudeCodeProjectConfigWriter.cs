using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class ClaudeCodeProjectConfigWriter : IMcpClientConfigWriter
    {
        private readonly ManagedJsonMerger _jsonMerger;
        private readonly McpPathResolver _pathResolver;

        public ClaudeCodeProjectConfigWriter()
            : this(new ManagedJsonMerger(), new McpPathResolver())
        {
        }

        internal ClaudeCodeProjectConfigWriter(ManagedJsonMerger jsonMerger, McpPathResolver pathResolver)
        {
            _jsonMerger = jsonMerger ?? throw new ArgumentNullException(nameof(jsonMerger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ManagedBlockApplyResult Apply(McpEditorSettings settings)
        {
            if (!CodexProjectConfigWriter.TryBuildExecutableCommand(settings, _pathResolver, out var executableCommand))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = GetTargetPath(settings, _pathResolver),
                    Reason = "cli_executable_missing",
                };
            }

            if (IsMachineRuntime(settings))
            {
                var launcherPath = _pathResolver.ResolveLauncherPath(settings);
                if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
                {
                    return new ManagedBlockApplyResult { Applied = false, TargetPath = GetTargetPath(settings, _pathResolver), Reason = "machine_launcher_missing" };
                }
                return _jsonMerger.Apply(
                    GetTargetPath(settings, _pathResolver),
                    BuildManagedLauncherJson(launcherPath, _pathResolver.GetProjectRoot()));
            }

            return _jsonMerger.Apply(
                GetTargetPath(settings, _pathResolver),
                BuildManagedJson(executableCommand, string.Empty, false));
        }

        public ManagedBlockApplyResult Remove()
        {
            return _jsonMerger.Remove(GetTargetPath(_pathResolver));
        }

        public string Preview(McpEditorSettings settings)
        {
            if (!CodexProjectConfigWriter.TryBuildExecutableCommand(settings, _pathResolver, out var executableCommand))
            {
                return "{\n  \"mcpServers\": {\n    \"unity_agent_bridge\": {\n      \"error\": \"cli_executable_missing\",\n      \"message\": \"Resolved unity_agent_bridge executable path does not exist. Prepare the project-local MCP runtime before applying managed MCP config.\"\n    }\n  }\n}";
            }

            if (IsMachineRuntime(settings) && !File.Exists(_pathResolver.ResolveLauncherPath(settings)))
            {
                return "{\n  \"mcpServers\": {\n    \"unity_agent_bridge\": {\n      \"error\": \"machine_launcher_missing\"\n    }\n  }\n}";
            }

            var serverJson = IsMachineRuntime(settings)
                ? BuildManagedLauncherJson(_pathResolver.ResolveLauncherPath(settings), _pathResolver.GetProjectRoot())
                : BuildManagedJson(executableCommand, string.Empty, false);
            return "{\n  \"mcpServers\": {\n    \"unity_agent_bridge\": " + serverJson + "\n  }\n}";
        }

        internal static string GetTargetPath()
        {
            return GetTargetPath(new McpPathResolver());
        }

        internal static string GetTargetPath(McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            var workspaceRoot = resolver.GetWorkspaceRoot();
            return GetTargetPath(workspaceRoot);
        }

        internal static string GetTargetPath(McpEditorSettings settings, McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            var workspaceRoot = resolver.GetWorkspaceRoot(settings);
            return GetTargetPath(workspaceRoot);
        }

        internal static string GetTargetPath(string workspaceRoot)
        {
            if (string.IsNullOrEmpty(workspaceRoot))
            {
                throw new InvalidOperationException("Unable to determine MCP workspace root.");
            }

            return Path.Combine(workspaceRoot, ".mcp.json");
        }

        internal static string BuildManagedJson(string executableCommand)
        {
            return BuildManagedJson(executableCommand, string.Empty, false);
        }

        internal static string BuildManagedJson(string executableCommand, string projectRoot, bool includeProjectBinding)
        {
            executableCommand = (executableCommand ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var json = "{\n      \"command\": \"" + executableCommand + "\",\n      \"args\": [\"mcp-server\"],\n      \"cwd\": \".\"";
            if (includeProjectBinding && !string.IsNullOrWhiteSpace(projectRoot))
            {
                var escapedProject = projectRoot.Replace("\\", "\\\\").Replace("\"", "\\\"");
                json += ",\n      \"env\": {\n        \"UNITY_AGENT_BRIDGE_PROJECT_PATH\": \"" + escapedProject + "\"\n      }";
            }

            return json + "\n    }";
        }

        internal static string BuildManagedLauncherJson(string launcherPath, string projectRoot)
        {
            var launcher = (launcherPath ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var project = (projectRoot ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\n      \"command\": \"cmd\",\n      \"args\": [\"/d\", \"/s\", \"/c\", \"" + launcher + "\"],\n      \"cwd\": \".\",\n      \"env\": {\n        \"UNITY_AGENT_BRIDGE_PROJECT_PATH\": \"" + project + "\"\n      }\n    }";
        }

        private static bool IsMachineRuntime(McpEditorSettings settings)
        {
            return settings != null && string.Equals(settings.RuntimeMode, "machine", StringComparison.OrdinalIgnoreCase);
        }
    }
}
