using System.Collections.Generic;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    [UnityMcpPlugin("com.unitymcp.builtin.unity-queries", "1.0.0")]
    public sealed class UnityQueriesProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            return new IUnityMcpTool[]
            {
                new UnityAssetDatabaseSearchTool(),
                new UnityGetHierarchyTool(),
                new UnityGameObjectComponentInfoTool(),
                new UnitySelectionInfoTool(),
                new UnityReadReportTool()
            };
        }
    }
}
