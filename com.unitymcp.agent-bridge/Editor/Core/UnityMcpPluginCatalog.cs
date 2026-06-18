using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityMcpPluginCatalog
    {
        public int version = 1;
        public List<UnityMcpPluginCatalogTool> tools = new List<UnityMcpPluginCatalogTool>();
    }

    [Serializable]
    public sealed class UnityMcpPluginCatalogTool
    {
        public string pluginId;
        public string pluginVersion;
        public string assemblyName;
        public string bridgeTool;
        public string mcpName;
        public string title;
        public string description;
        public int defaultTimeoutMs;
        public string allowedRuntimeModes;
        public string sideEffect;
        public bool mayTriggerDomainReload;
        public string inputSchemaJson;
    }
}
