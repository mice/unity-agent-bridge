using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityTestRunArgs
    {
        public string filter;
        public string[] testNames;
        public string[] assemblyNames;
        public string[] categoryNames;
        public string[] groupNames;
        public int timeoutMs;
    }
}
