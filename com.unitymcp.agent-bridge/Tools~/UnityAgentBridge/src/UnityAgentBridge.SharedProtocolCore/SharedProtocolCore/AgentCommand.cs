using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class AgentCommand
    {
        public string schemaVersion;
        public int targetSchemaVersion;
        public string projectId;
        public string editorSessionId;
        public string connectionId;
        public string commandId;
        public string tool;
        public int timeoutMs;
        public string createdAt;
        public string rawArgsJson;
    }
}
