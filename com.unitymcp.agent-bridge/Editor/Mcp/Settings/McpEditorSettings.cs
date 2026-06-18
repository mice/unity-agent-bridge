using System;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpEditorSettings
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string DotnetPath { get; set; } = string.Empty;
        public string WorkspaceRoot { get; set; } = string.Empty;
        public string ToolsRoot { get; set; } = string.Empty;
        public string McpServerRoot { get; set; } = string.Empty;
        public string CliExecutablePath { get; set; } = string.Empty;
        public bool PreferPublishedCli { get; set; }
        public int DiagnosticTimeoutMs { get; set; } = 30000;
    }
}
