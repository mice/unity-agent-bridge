namespace UnityAgentBridge.Mcp;

public sealed class McpArgumentValidationException : Exception
{
    public McpArgumentValidationException(string message)
        : base(message)
    {
    }
}
