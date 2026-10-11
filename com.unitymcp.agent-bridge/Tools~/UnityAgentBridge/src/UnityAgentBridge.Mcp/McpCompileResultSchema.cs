using System.Text.Json;

namespace UnityAgentBridge.Mcp;

internal static class McpCompileResultSchema
{
    internal static readonly JsonElement Value = Load();

    private static JsonElement Load()
    {
        using var stream = typeof(McpCompileResultSchema).Assembly.GetManifestResourceStream("UnityAgentBridge.Mcp.CompileResultSchema.json")
            ?? throw new InvalidOperationException("The embedded compile result schema is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
