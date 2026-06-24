using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class QueueCommandState
    {
        public string commandId;
        public string tool;
        public string status;
        public string startedAt;
        public int timeoutMs;
        public string runGuid;
        public string filter;
        public string testMode;
    }
}
