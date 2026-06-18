using System;
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
