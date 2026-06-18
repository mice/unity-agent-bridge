using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class AgentBridgeSelfTestCaseResult
    {
        public string id;
        public string scenario;
        public string tool;
        public string expectedStatus;
        public string actualStatus;
        public bool passed;
        public string summary;
        public long durationMs;
        public string startedAt;
        public string finishedAt;
        public string reportPath;
        public List<ToolWarning> warnings = new List<ToolWarning>();
        public List<ToolError> errors = new List<ToolError>();
        public string metrics = "{}";
        public string metricsObjectJson = "{}";
    }

    [Serializable]
    public sealed class AgentBridgeSelfTestMetrics
    {
        public string suiteVersion = "1.0";
        public bool overallPassed;
        public int caseCount;
        public int passedCount;
        public int failedCount;
        public int cancelledCount;
        public AgentBridgeSelfTestCaseResult[] cases = Array.Empty<AgentBridgeSelfTestCaseResult>();
    }

    [Serializable]
    public sealed class SelfTestRunOptions
    {
        public bool includeEditModeCase = true;
        public bool includeDiagnosticCase = true;
        public bool continueOnFailure = true;
        public long timeoutMs = 120000;
    }
}
