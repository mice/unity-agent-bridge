using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    [UnityMcpPlugin("com.unitymcp.builtin.editor-basics", "1.0.0")]
    public sealed class EditorBasicsProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            return new IUnityMcpTool[]
            {
                new UnityPingTool(),
                new UnityConsoleLogTool(),
                new UnityGetEditorStateTool()
            };
        }
    }
}
