using System.Collections.Generic;

namespace UnityMcp.AgentBridge.Mcp
{
    public interface IMcpReadinessAggregator
    {
        McpReadiness Aggregate(IReadOnlyList<McpDiagnosticCheck> checks, McpEditorSettings settings);
    }

    public enum McpReadiness
    {
        NotChecked,
        Diagnosing,
        Ready,
        Degraded,
        Unavailable,
    }
}
