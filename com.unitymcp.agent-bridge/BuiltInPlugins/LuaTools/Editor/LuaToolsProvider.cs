using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.LuaTools
{
    [UnityMcpPlugin("com.unitymcp.builtin.lua-tools", "1.0.0")]
    public sealed class LuaToolsProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            if (!LuaToolsRuntimeState.TryResolveToolAvailability(context?.ProjectRoot, out var availability))
            {
                return new IUnityMcpTool[0];
            }

            return new IUnityMcpTool[]
            {
                new UnityLuaLintTool(availability),
                new UnityLuaCompileTool(availability)
            };
        }
    }
}
