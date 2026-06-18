using System;
using System.IO;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class GitHubCopilotProjectConfigWriter : IMcpClientConfigWriter
    {
        private const string ContainerName = "servers";

        private readonly ManagedJsonMerger _jsonMerger;
        private readonly McpPathResolver _pathResolver;

        public GitHubCopilotProjectConfigWriter()
            : this(new ManagedJsonMerger(), new McpPathResolver())
        {
        }

        internal GitHubCopilotProjectConfigWriter(ManagedJsonMerger jsonMerger, McpPathResolver pathResolver)
        {
            _jsonMerger = jsonMerger ?? throw new ArgumentNullException(nameof(jsonMerger));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ManagedBlockApplyResult Apply(McpEditorSettings settings)
        {
            if (!CursorProjectConfigWriter.TryBuildLauncherCommand(settings, _pathResolver, out var launcherCommand, out var projectRoot))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = GetTargetPath(settings, _pathResolver),
                    Reason = "launcher_missing",
                };
            }

            return _jsonMerger.Apply(GetTargetPath(settings, _pathResolver), CursorProjectConfigWriter.BuildManagedJson(launcherCommand, projectRoot), ContainerName);
        }

        public ManagedBlockApplyResult Remove()
        {
            return _jsonMerger.Remove(GetTargetPath(_pathResolver), ContainerName);
        }

        public string Preview(McpEditorSettings settings)
        {
            if (!CursorProjectConfigWriter.TryBuildLauncherCommand(settings, _pathResolver, out var launcherCommand, out var projectRoot))
            {
                return "{\n  \"" + ContainerName + "\": {\n    \"unity_agent_bridge\": {\n      \"error\": \"launcher_missing\",\n      \"message\": \"Prepared Unity Agent Bridge launcher does not exist. Prepare the project-local MCP runtime before applying managed MCP config.\"\n    }\n  }\n}";
            }

            return "{\n  \"" + ContainerName + "\": {\n    \"unity_agent_bridge\": " + CursorProjectConfigWriter.BuildManagedJson(launcherCommand, projectRoot) + "\n  }\n}";
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

            return Path.Combine(workspaceRoot, ".vscode", "mcp.json");
        }
    }
}
