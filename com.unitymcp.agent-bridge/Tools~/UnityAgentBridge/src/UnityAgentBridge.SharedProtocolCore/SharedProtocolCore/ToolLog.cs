using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class ToolLog
    {
        public string level;
        public string message;
        public string timestamp;
    }
}
