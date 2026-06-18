using System;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    [Serializable]
    public sealed class UnityPingArgs
    {
    }

    [Serializable]
    public sealed class UnityPingMetrics
    {
        public string unityVersion;
        public bool isCompiling;
    }
}
