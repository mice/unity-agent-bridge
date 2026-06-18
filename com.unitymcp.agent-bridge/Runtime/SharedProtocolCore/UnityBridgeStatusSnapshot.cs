using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityBridgeStatusSnapshot
    {
        public string logVersion = "1.0";
        public string schemaVersion = JsonUtil.CurrentSchemaVersion;
        public string timestamp;
        public string heartbeatUtc;
        public string currentCommandId;
        public string currentStage;
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public string lastError;
        public string projectPath;
        public int currentCompileEpoch;
        public int[] activeTargetEpochs;
        public string[] activeCompileCommandIds;
        public string compileLifecycleStage;
        public string compileLastTransition;
        public string compileLastTransitionAtUtc;
        public string compileTimeoutReason;
        public string stalePrimaryClassification;
        public string staleEvidencePriorityPath;
        public long staleHeartbeatAgeMs;
        public string staleConfiguredProjectPath;
        public string staleDetectedProjectPath;
        public string staleProjectBindingKind;
        public string staleRuntimeIdentity;
    }
}
