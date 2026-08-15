using ModelContextProtocol.Protocol;
using System.Text.Json;
using UnityMcp.AgentBridge;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public static class McpToolCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, ToolMetadata> BuiltInMetadataByName = CreateBuiltInMetadata().ToDictionary(
        metadata => metadata.Name,
        StringComparer.Ordinal);

    public static IReadOnlyList<McpToolDefinition> GetTools(McpHostDiagnostics diagnostics)
    {
        return BuildDefinitions(diagnostics).Values.Select(CreateDefinition).ToList();
    }

    public static McpToolDefinition? TryGet(string toolName, McpHostDiagnostics diagnostics)
    {
        return BuildDefinitions(diagnostics).TryGetValue(toolName, out var metadata) ? CreateDefinition(metadata) : null;
    }

    private static IReadOnlyDictionary<string, ToolMetadata> BuildDefinitions(McpHostDiagnostics diagnostics)
    {
        var merged = new Dictionary<string, ToolMetadata>(BuiltInMetadataByName, StringComparer.Ordinal);
        foreach (var pluginTool in LoadPluginTools(diagnostics))
        {
            if (merged.TryGetValue(pluginTool.Name, out var existingTool))
            {
                if (existingTool.IsForwardedToUnityQueue &&
                    string.Equals(existingTool.BridgeTool, pluginTool.BridgeTool, StringComparison.Ordinal))
                {
                    continue;
                }

                Console.Error.WriteLine($"MCP tool name conflict: '{pluginTool.Name}' was already registered; rejecting the later plugin catalog entry.");
                continue;
            }

            merged.Add(pluginTool.Name, pluginTool);
        }

        return merged;
    }

    private static IEnumerable<ToolMetadata> CreateBuiltInMetadata()
    {
        yield return CreateToolMetadata(
            "mcp_echo",
            "MCP Echo",
            "Return a local diagnostic response without touching the Unity bridge queue.",
            """
            {"type":"object","properties":{"value":{"type":"string"},"payload":{"type":"object","propertyNames":{"type":"string"},"additionalProperties":{}}},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}
            """,
            "mcp.echo",
            5000,
            false);

        yield return CreateToolMetadata(
            "unity_bridge_health",
            "Unity Bridge Health",
            "Read queue and status-file health for the Unity bridge without executing a Unity tool.",
            """{"type":"object","properties":{},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""",
            "unity.bridge_health",
            5000,
            false);

        yield return CreateToolMetadata(
            "unity_bridge_submit_only",
            "Unity Bridge Submit Only",
            "Write a Unity bridge command and return its commandId without waiting for the result.",
            """
            {"type":"object","properties":{"tool":{"type":"string","minLength":1},"args":{"type":"object","propertyNames":{"type":"string"},"additionalProperties":{}},"submitTimeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["tool"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}
            """,
            "unity.bridge_submit_only",
            15000,
            false);

        yield return CreateToolMetadata(
            "unity_bridge_wait_result",
            "Unity Bridge Wait Result",
            "Wait for a previously submitted Unity bridge command result with a bounded timeout.",
            """
            {"type":"object","properties":{"commandId":{"type":"string","minLength":1},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["commandId","timeoutMs"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}
            """,
            "unity.bridge_wait_result",
            15000,
            false);

        yield return CreateToolMetadata(
            "unity_editor_list",
            "Unity Editor List",
            "Report running Unity Editor processes and project/version evidence without touching the Unity bridge queue.",
            """
            {"type":"object","properties":{"includeBridgeHealth":{"type":"boolean"},"projectPath":{"type":"string","minLength":1}},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}
            """,
            "unity.editor_list",
            5000,
            false);

        yield return CreateToolMetadata(
            "unity_editor_open",
            "Unity Editor Open",
            "Open a Unity project through guarded local launch logic without dispatching through the Unity bridge queue.",
            """
            {"type":"object","properties":{"projectPath":{"type":"string","minLength":1},"unityExecutablePath":{"type":"string","minLength":1},"allowVersionFallback":{"type":"boolean","default":false},"waitForBridge":{"type":"boolean","default":false},"bridgeReadyTimeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991,"default":120000},"bridgePollIntervalMs":{"type":"integer","minimum":1,"maximum":9007199254740991,"default":1000},"maxRunningUnityEditors":{"type":"integer","minimum":1,"maximum":9007199254740991,"default":3}},"required":["projectPath"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}
            """,
            "unity.editor_open",
            120000,
            false);

        foreach (var metadata in CreateUnityBridgeForwardedTools())
        {
            yield return metadata;
        }
    }

    private static IEnumerable<ToolMetadata> CreateUnityBridgeForwardedTools()
    {
        yield return CreateForwardedToolMetadata("Unity Ping", "Call unity.ping through the Unity Agent Bridge CLI.", """{"type":"object","properties":{},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.ping", 5000);
        yield return CreateForwardedToolMetadata("Unity Compile", "Call unity.compile through the Unity Agent Bridge CLI.", """{"type":"object","properties":{},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.compile", 60000);
        yield return CreateForwardedToolMetadata("Unity Console", "Call unity.get_console through the Unity Agent Bridge CLI.", """{"type":"object","properties":{"types":{"minItems":1,"maxItems":3,"type":"array","items":{"type":"string","enum":["error","warning","info"]}},"count":{"type":"integer","minimum":0,"maximum":1000},"filter":{"type":"string"},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["types"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.get_console", 10000);
        yield return CreateForwardedToolMetadata("Unity Get Editor State", "Call unity.get_editor_state through the Unity Agent Bridge CLI.", """{"type":"object","properties":{"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.get_editor_state", 10000);
        yield return CreateForwardedToolMetadata("Unity Open Scene", "Call unity.open_scene through the Unity Agent Bridge CLI.", """{"type":"object","properties":{"scenePath":{"type":"string","minLength":1},"mode":{"type":"string","enum":["single","additive"]},"setActive":{"type":"boolean"},"saveModifiedScenes":{"type":"boolean"},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["scenePath"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.open_scene", 10000);
        yield return CreateForwardedToolMetadata("Unity Run Static Method", "Call unity.run_static_method through the Unity Agent Bridge CLI.", """{"type":"object","properties":{"typeName":{"type":"string","minLength":1},"methodName":{"type":"string","minLength":1},"parameters":{"type":"object","propertyNames":{"type":"string"},"additionalProperties":{}},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["typeName","methodName"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.run_static_method", 60000);
        yield return CreateForwardedToolMetadata("Unity Run Diagnostic", "Call unity.run_diagnostic through the Unity Agent Bridge CLI.", """{"type":"object","properties":{"diagnosticType":{"type":"string","enum":["fx_prefab","scene","prefab","texture_import","shader_variant","material_instance","vat_mesh","bakeroot"]},"targetPath":{"type":"string","minLength":1},"timeoutMs":{"type":"integer","minimum":1,"maximum":9007199254740991}},"required":["diagnosticType","targetPath"],"$schema":"http://json-schema.org/draft-07/schema#","additionalProperties":false}""", "unity.run_diagnostic", 120000);
    }

    private static IEnumerable<ToolMetadata> LoadPluginTools(McpHostDiagnostics diagnostics)
    {
        var catalogPath = Path.Combine(diagnostics.ProjectPath, "Library", "AgentBridge", "plugin-catalog.json");
        if (!File.Exists(catalogPath))
        {
            return Array.Empty<ToolMetadata>();
        }

        try
        {
            var rawJson = File.ReadAllText(catalogPath);
            var catalog = JsonSerializer.Deserialize<McpPluginCatalog>(rawJson, JsonOptions);
            if (catalog?.Tools == null)
            {
                return Array.Empty<ToolMetadata>();
            }

            return catalog.Tools
                .Where(tool => !string.IsNullOrWhiteSpace(tool.BridgeTool) &&
                               !string.IsNullOrWhiteSpace(tool.Title) &&
                               !string.IsNullOrWhiteSpace(tool.Description) &&
                               !string.IsNullOrWhiteSpace(tool.InputSchemaJson) &&
                               tool.DefaultTimeoutMs > 0 &&
                               McpToolNameMapper.TryToCanonicalMcpName(tool.BridgeTool, out _))
                .Select(tool => CreateForwardedToolMetadata(
                    tool.Title,
                    tool.Description,
                    tool.InputSchemaJson,
                    tool.BridgeTool,
                    tool.DefaultTimeoutMs))
                .ToArray();
        }
        catch
        {
            return Array.Empty<ToolMetadata>();
        }
    }

    private static ToolMetadata CreateToolMetadata(string name, string title, string description, string schemaJson, string bridgeTool, int defaultTimeoutMs, bool isForwardedToUnityQueue)
    {
        return new ToolMetadata(
            name,
            title,
            description,
            schemaJson,
            bridgeTool,
            defaultTimeoutMs,
            isForwardedToUnityQueue);
    }

    private static ToolMetadata CreateForwardedToolMetadata(string title, string description, string schemaJson, string bridgeTool, int defaultTimeoutMs)
    {
        return CreateToolMetadata(McpToolNameMapper.ToCanonicalMcpName(bridgeTool), title, description, schemaJson, bridgeTool, defaultTimeoutMs, true);
    }

    private static McpToolDefinition CreateDefinition(ToolMetadata metadata)
    {
        return new McpToolDefinition
        {
            ProtocolTool = new Tool
            {
                Name = metadata.Name,
                Title = metadata.Title,
                Description = metadata.Description,
                InputSchema = JsonDocument.Parse(metadata.SchemaJson).RootElement.Clone()
            },
            SchemaJson = metadata.SchemaJson,
            BridgeTool = metadata.BridgeTool,
            IsForwardedToUnityQueue = metadata.IsForwardedToUnityQueue,
            InvokeAsync = async (argumentsJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = new ExternalBridgeClient();
                var queuePaths = McpToolRuntimeContext.QueuePaths ?? throw new InvalidOperationException("MCP queue paths were not initialized.");
                var commandId = client.CreateCommandId();
                var result = await client.ExecuteAsync(queuePaths, commandId, new BridgeCommandSpec(metadata.BridgeTool, metadata.DefaultTimeoutMs, argumentsJson), cancellationToken);
                return result.RawJson;
            }
        };
    }

    private sealed record ToolMetadata(
        string Name,
        string Title,
        string Description,
        string SchemaJson,
        string BridgeTool,
        int DefaultTimeoutMs,
        bool IsForwardedToUnityQueue);
}
