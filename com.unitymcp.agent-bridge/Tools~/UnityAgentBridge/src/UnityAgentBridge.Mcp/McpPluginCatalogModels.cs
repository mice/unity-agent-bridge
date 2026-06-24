namespace UnityAgentBridge.Mcp;

internal sealed class McpPluginCatalog
{
    public int Version { get; set; }

    public List<McpPluginCatalogTool> Tools { get; set; } = new();
}

internal sealed class McpPluginCatalogTool
{
    public string PluginId { get; set; } = string.Empty;

    public string PluginVersion { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string BridgeTool { get; set; } = string.Empty;

    public string McpName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int DefaultTimeoutMs { get; set; }

    public string AllowedRuntimeModes { get; set; } = string.Empty;

    public string SideEffect { get; set; } = string.Empty;

    public bool MayTriggerDomainReload { get; set; }

    public string InputSchemaJson { get; set; } = string.Empty;
}
