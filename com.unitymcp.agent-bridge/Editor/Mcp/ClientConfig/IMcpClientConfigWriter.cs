using System;

namespace UnityMcp.AgentBridge.Mcp
{
    public interface IMcpClientConfigWriter
    {
        ManagedBlockApplyResult Apply(McpEditorSettings settings);

        ManagedBlockApplyResult Remove();

        string Preview(McpEditorSettings settings);
    }

    public sealed class ManagedBlockApplyResult
    {
        public bool Applied { get; set; }
        public string TargetPath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
    }
}
