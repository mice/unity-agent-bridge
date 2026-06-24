using ModelContextProtocol.Protocol;

namespace UnityAgentBridge.Mcp;

public sealed class McpToolDefinition
{
    public required Tool ProtocolTool { get; init; }

    public required string SchemaJson { get; init; }

    public string? BridgeTool { get; init; }

    public required Func<string, CancellationToken, Task<string>> InvokeAsync { get; init; }
}
