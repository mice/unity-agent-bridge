using System.IO;
using UnityEditor;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Mcp
{
    [InitializeOnLoad]
    internal static class McpRoslynCompilerPayloadResolver
    {
        static McpRoslynCompilerPayloadResolver()
        {
            UnityMcpPluginRuntime.ConfigureRoslynCompilerPayloadResolver(Resolve);
        }

        private static UnityMcpRoslynCompilerPayload Resolve(string projectRoot)
        {
            var settingsPath = Path.Combine(projectRoot, "Library", "AgentBridge", "mcp-editor-settings.json");
            var settings = new McpEditorSettingsStore(settingsPath).Load();
            var resolver = new McpPathResolver(() => projectRoot);
            return new UnityMcpRoslynCompilerPayload
            {
                CompilerPath = resolver.ResolveRoslynCompilerPath(settings),
                RuntimeMode = resolver.ResolveRuntimeMode(settings),
                RuntimeVersion = resolver.ResolveRuntimeVersion(settings)
            };
        }
    }
}
