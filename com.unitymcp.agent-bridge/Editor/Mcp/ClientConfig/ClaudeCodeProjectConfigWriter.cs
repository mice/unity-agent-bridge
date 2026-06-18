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

            return _jsonMerger.Apply(GetTargetPath(settings, _pathResolver), BuildManagedJson(executableCommand));
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

            return "{\n  \"mcpServers\": {\n    \"unity_agent_bridge\": " + BuildManagedJson(executableCommand) + "\n  }\n}";
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
            executableCommand = (executableCommand ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\n      \"command\": \"" + executableCommand + "\",\n      \"args\": [\"mcp-server\"],\n      \"cwd\": \".\"\n    }";
        }
    }
}
