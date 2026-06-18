using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    internal sealed class CompileLifecycleState
    {
        public int compileEpoch;
        public int lastStartedEpoch;
        public int lastFinishedEpoch;
        public bool isCompiling;
        public string startedAtUtc;
        public string finishedAtUtc;
        public string lastAssemblyPath;
        public int assemblyFinishedCount;
        public int errorCount;
        public int warningCount;
        public string lastTransition;
        public string lastTransitionAtUtc;
        public List<string> activeCommandIds = new List<string>();
        public List<int> activeTargetEpochs = new List<int>();
        public string currentStage;
        public string timeoutReason;
        public string projectPath;
    }
}
