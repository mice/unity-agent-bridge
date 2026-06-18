using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpReadinessAggregator : IMcpReadinessAggregator
    {
        public McpReadiness Aggregate(IReadOnlyList<McpDiagnosticCheck> checks, McpEditorSettings settings)
        {
            if (checks == null || checks.Count == 0)
            {
                return McpReadiness.NotChecked;
            }

            bool IsError(string code)
            {
                for (var index = 0; index < checks.Count; index++)
                {
                    if (string.Equals(checks[index].Code, code, StringComparison.Ordinal) &&
                        checks[index].Severity == McpDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (IsError("MCP003") || IsError("MCP006") || IsError("MCP007") || IsError("MCP008"))
            {
                return McpReadiness.Unavailable;
            }

            var bridgeHealthy = false;
            for (var index = 0; index < checks.Count; index++)
            {
                if (string.Equals(checks[index].Code, "MCP001", StringComparison.Ordinal) &&
                    checks[index].Severity == McpDiagnosticSeverity.Info)
                {
                    bridgeHealthy = true;
                    break;
                }
            }

            if (!bridgeHealthy
                || IsError("MCP002")
                || IsError("MCP004")
                || IsError("MCP009")
                || IsError("MCP010"))
            {
                return McpReadiness.Degraded;
            }

            return McpReadiness.Ready;
        }
    }
}
