using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public static class McpRuntimeSmokeMethods
    {
        public static void PrepareMcpRuntime(PrepareMcpRuntimeArgs args)
        {
            var settings = new McpEditorSettings
            {
                ToolsRoot = args != null ? args.toolsRoot ?? string.Empty : string.Empty,
                WorkspaceRoot = args != null ? args.workspaceRoot ?? string.Empty : string.Empty
            };

            var result = new McpRuntimeInitializer()
                .InitializeRuntimeAsync(settings, default)
                .GetAwaiter()
                .GetResult();

            if (result != null && result.Applied)
            {
                AgentBridgeBootstrap.Reconfigure();
            }

            Debug.Log("McpRuntimeSmokeMethods.PrepareMcpRuntime().Done");
            Debug.Log(JsonConvert.SerializeObject(result, Formatting.None));
        }

        public static void PrepareRuntimeAndApplyCodexConfig(PrepareMcpRuntimeArgs args)
        {
            var settings = new McpEditorSettings
            {
                ToolsRoot = args != null ? args.toolsRoot ?? string.Empty : string.Empty,
                WorkspaceRoot = args != null ? args.workspaceRoot ?? string.Empty : string.Empty
            };

            var prepareResult = new McpRuntimeInitializer()
                .InitializeRuntimeAsync(settings, default)
                .GetAwaiter()
                .GetResult();

            if (prepareResult == null || !prepareResult.Applied)
            {
                throw new InvalidOperationException("Prepare Runtime failed: " + JsonConvert.SerializeObject(prepareResult, Formatting.None));
            }

            var configResult = new CodexProjectConfigWriter().Apply(settings);
            if (configResult == null || !configResult.Applied)
            {
                throw new InvalidOperationException("Apply Codex MCP config failed: " + JsonConvert.SerializeObject(configResult, Formatting.None));
            }

            var configText = File.Exists(configResult.TargetPath)
                ? File.ReadAllText(configResult.TargetPath)
                : string.Empty;
            var expectedRuntimeSegment = Path.Combine(".unitymcp", "runtime", "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid(), McpRuntimeInitializer.GetProductExecutableName());
            if (configText.IndexOf("mcp-server", StringComparison.OrdinalIgnoreCase) < 0 ||
                configText.IndexOf("unity-agent-bridge", StringComparison.OrdinalIgnoreCase) < 0 ||
                configText.IndexOf(expectedRuntimeSegment.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("Codex MCP config does not target the project-local runtime executable.");
            }

            var payload = new
            {
                prepareResult,
                configResult,
                expectedRuntimeSegment
            };
            Debug.Log("McpRuntimeSmokeMethods.PrepareRuntimeAndApplyCodexConfig().Done");
            Debug.Log(JsonConvert.SerializeObject(payload, Formatting.None));
        }
    }

    [Serializable]
    public sealed class PrepareMcpRuntimeArgs : IStaticMethodArgsValidator
    {
        public string workspaceRoot;
        public string toolsRoot;

        public bool Validate(out string validationMessage)
        {
            validationMessage = null;
            return true;
        }
    }
}
