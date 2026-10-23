using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class ProjectRegistration
    {
        public int schemaVersion = 1;
        public string projectId;
        public string projectPath;
        public string queueRoot;
        public string registeredAt;
        public string lastConnectedAt;
    }

    [Serializable]
    public sealed class EditorSessionInfo
    {
        public int schemaVersion = 1;
        public string projectId;
        public string editorSessionId;
        public int processId;
        public string processStartTime;
        public string heartbeatUtc;
        public long revision;
        public bool bridgeEnabled;
        public string bridgeVersion;
        public string capabilityRevision;
    }

    [Serializable]
    public sealed class ProjectSessionInfo
    {
        public ProjectRegistration registration;
        public EditorSessionInfo editorSession;
        public string availability;
        public string reason;
        public bool ready;
    }

    public static class CommandTargetValidator
    {
        public static string Validate(AgentCommand command, string projectId, string editorSessionId)
        {
            if (command.targetSchemaVersion == 0 && string.IsNullOrEmpty(command.projectId) && string.IsNullOrEmpty(command.editorSessionId)) return null;
            if (command.targetSchemaVersion != 1) return "PROJECT_PROTOCOL_UNSUPPORTED";
            if (command.projectId != projectId) return "PROJECT_ID_MISMATCH";
            if (command.editorSessionId != editorSessionId) return "EDITOR_SESSION_MISMATCH";
            return null;
        }
    }
}
