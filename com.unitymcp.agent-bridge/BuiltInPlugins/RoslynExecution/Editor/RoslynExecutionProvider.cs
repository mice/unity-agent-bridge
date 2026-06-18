using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.RoslynExecution
{
    [UnityMcpPlugin("com.unitymcp.builtin.roslyn-execution", "1.0.0")]
    public sealed class RoslynExecutionProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            if (!RoslynExecutionRuntimeState.TryResolveToolAvailability(context?.ProjectRoot, out var availability))
            {
                return new IUnityMcpTool[0];
            }

            return new IUnityMcpTool[]
            {
                new UnityExecuteCSharpTool(availability)
            };
        }
    }
}
