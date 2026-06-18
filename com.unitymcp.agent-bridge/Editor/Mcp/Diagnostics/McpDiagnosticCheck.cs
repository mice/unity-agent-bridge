using System;

namespace UnityMcp.AgentBridge.Mcp
{
    public enum McpDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class McpDiagnosticCheck
    {
        public string Code { get; set; } = string.Empty;
        public McpDiagnosticSeverity Severity { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Remediation { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }
}
