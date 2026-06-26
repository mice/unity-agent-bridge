using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    [UnityMcpPlugin("com.unitymcp.builtin.monobehaviour-semantics", "1.0.0")]
    public sealed class MonoBehaviourSemanticsProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            return new IUnityMcpTool[]
            {
                new FindScriptGuidUsagesTool()
            };
        }
    }
}
