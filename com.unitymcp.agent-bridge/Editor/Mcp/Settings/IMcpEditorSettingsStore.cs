namespace UnityMcp.AgentBridge.Mcp
{
    public interface IMcpEditorSettingsStore
    {
        McpEditorSettings Load();

        void Save(McpEditorSettings settings);
    }
}
