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
        public string RuntimeMode { get; set; } = "project-local";
        public string RuntimeVersion { get; set; } = string.Empty;
        public string RuntimeChannel { get; set; } = string.Empty;
        public string MachineRuntimeRoot { get; set; } = string.Empty;
        public int DiagnosticTimeoutMs { get; set; } = 30000;
        public string PiSchemaVersion { get; set; } = "1.0";
        public string PiLoadError { get; set; }
        public string PiExecutablePath { get; set; } = "pi";
        public string PiModel { get; set; } = "deepseek/deepseek-flash";
        public int PiTimeoutMilliseconds { get; set; } = 120000;
        public int PiMaxTurns { get; set; } = 8;
        public int PiMaxOutputBytes { get; set; } = 65536;
        public string[] PiTools { get; set; } = new[] { "read", "grep", "find", "ls" };
    }

}
