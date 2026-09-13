using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UnityMcp.AgentBridge;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public static class McpToolCatalog
{
    private const int PluginCatalogVersion = 1;
    private const int PluginCatalogMaxTools = 64;
    private const int PluginCatalogMaxUtf8Bytes = 65536;
    private const int PluginSchemaMaxUtf8Bytes = 1048576;
    private static readonly Regex PluginIdPattern = new("^[a-z0-9][a-z0-9-]*(?:\\.[a-z0-9][a-z0-9-]*)+$", RegexOptions.CultureInvariant);
    private static readonly Regex PluginVersionPattern = new("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);
    private static readonly Regex AssemblyNamePattern = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex BridgeToolPattern = new("^unity(?:\\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant);
    private static readonly Regex McpNamePattern = new("^unity_[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal) { "version", "tools" };
    private static readonly HashSet<string> ToolProperties = new(StringComparer.Ordinal)
    {
        "pluginId", "pluginVersion", "assemblyName", "bridgeTool", "mcpName", "title", "description",
        "defaultTimeoutMs", "allowedRuntimeModes", "sideEffect", "mayTriggerDomainReload", "inputSchemaJson"
    };
    private static readonly HashSet<string> RuntimeModes = new(StringComparer.Ordinal) { "Edit", "Play", "EditAndPlay" };
    private static readonly HashSet<string> SideEffects = new(StringComparer.Ordinal) { "None", "ReadsProject", "MutatesProject", "RunsUserCode" };
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
            var catalogBytes = File.ReadAllBytes(catalogPath);
            if (catalogBytes.Length > PluginCatalogMaxUtf8Bytes)
            {
                Console.Error.WriteLine($"Plugin catalog exceeds the {PluginCatalogMaxUtf8Bytes}-byte Catalog V1 limit.");
                return Array.Empty<ToolMetadata>();
            }

            using var document = JsonDocument.Parse(catalogBytes);
            var root = document.RootElement;
            if (!HasExactProperties(root, RootProperties) ||
                !root.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out var versionValue) ||
                versionValue != PluginCatalogVersion ||
                !root.TryGetProperty("tools", out var tools) ||
                tools.ValueKind != JsonValueKind.Array ||
                tools.GetArrayLength() > PluginCatalogMaxTools)
            {
                Console.Error.WriteLine("Plugin catalog does not conform to the Catalog V1 envelope.");
                return Array.Empty<ToolMetadata>();
            }

            var accepted = new List<ToolMetadata>();
            var bridgeNames = new HashSet<string>(StringComparer.Ordinal);
            var mcpNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tool in tools.EnumerateArray())
            {
                if (!TryReadCatalogTool(tool, out var metadata, out var reason) ||
                    bridgeNames.Contains(metadata!.BridgeTool) ||
                    mcpNames.Contains(metadata.Name))
                {
                    Console.Error.WriteLine($"Plugin catalog entry rejected: {reason ?? "duplicate bridge or MCP name"}.");
                    continue;
                }

                accepted.Add(metadata);
                bridgeNames.Add(metadata.BridgeTool);
                mcpNames.Add(metadata.Name);
            }

            return accepted;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Plugin catalog could not be read: {exception.Message}");
            return Array.Empty<ToolMetadata>();
        }
    }

    private static bool TryReadCatalogTool(JsonElement tool, out ToolMetadata? metadata, out string? reason)
    {
        metadata = null;
        reason = null;
        if (!HasExactProperties(tool, ToolProperties) ||
            !TryGetBoundedString(tool, "pluginId", 3, 255, PluginIdPattern, out _) ||
            !TryGetBoundedString(tool, "pluginVersion", 5, 64, PluginVersionPattern, out _) ||
            !TryGetBoundedString(tool, "assemblyName", 1, 255, AssemblyNamePattern, out _) ||
            !TryGetBoundedString(tool, "bridgeTool", 7, 255, BridgeToolPattern, out var bridgeTool) ||
            !TryGetBoundedString(tool, "mcpName", 7, 255, McpNamePattern, out var mcpName) ||
            !TryGetBoundedString(tool, "title", 1, 256, null, out var title) ||
            !TryGetBoundedString(tool, "description", 1, 4096, null, out var description) ||
            !tool.TryGetProperty("defaultTimeoutMs", out var timeout) ||
            !timeout.TryGetInt32(out var defaultTimeoutMs) ||
            defaultTimeoutMs <= 0 ||
            !TryGetEnum(tool, "allowedRuntimeModes", RuntimeModes) ||
            !TryGetEnum(tool, "sideEffect", SideEffects) ||
            !tool.TryGetProperty("mayTriggerDomainReload", out var reload) ||
            (reload.ValueKind != JsonValueKind.True && reload.ValueKind != JsonValueKind.False) ||
            !TryGetBoundedString(tool, "inputSchemaJson", 2, int.MaxValue, null, out var schemaJson) ||
            Encoding.UTF8.GetByteCount(schemaJson) > PluginSchemaMaxUtf8Bytes ||
            !IsJsonObject(schemaJson))
        {
            reason = "field shape or value violates Catalog V1";
            return false;
        }

        metadata = CreateForwardedToolMetadata(mcpName, title, description, schemaJson, bridgeTool, defaultTimeoutMs);
        return true;
    }

    private static bool HasExactProperties(JsonElement element, ISet<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = element.EnumerateObject().ToArray();
        var actual = new HashSet<string>(properties.Select(property => property.Name), StringComparer.Ordinal);
        return properties.Length == expected.Count && actual.SetEquals(expected);
    }

    private static bool TryGetBoundedString(JsonElement element, string propertyName, int minLength, int maxLength, Regex? pattern, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length >= minLength && value.Length <= maxLength && (pattern == null || pattern.IsMatch(value));
    }

    private static bool TryGetEnum(JsonElement element, string propertyName, ISet<string> values)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               values.Contains(property.GetString() ?? string.Empty);
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ToolMetadata CreateToolMetadata(string name, string title, string description, string schemaJson, string bridgeTool, int defaultTimeoutMs, bool isForwardedToUnityQueue, bool supportsTaskExecution = false)
    {
        return new ToolMetadata(
            name,
            title,
            description,
            schemaJson,
            bridgeTool,
            defaultTimeoutMs,
            isForwardedToUnityQueue,
            supportsTaskExecution);
    }

    private static ToolMetadata CreateForwardedToolMetadata(string title, string description, string schemaJson, string bridgeTool, int defaultTimeoutMs)
    {
        return CreateToolMetadata(McpToolNameMapper.ToCanonicalMcpName(bridgeTool), title, description, schemaJson, bridgeTool, defaultTimeoutMs, true, true);
    }

    private static ToolMetadata CreateForwardedToolMetadata(string mcpName, string title, string description, string schemaJson, string bridgeTool, int defaultTimeoutMs)
    {
        return CreateToolMetadata(mcpName, title, description, schemaJson, bridgeTool, defaultTimeoutMs, true, true);
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
                InputSchema = JsonDocument.Parse(metadata.SchemaJson).RootElement.Clone(),
                Meta = metadata.SupportsTaskExecution
                    ? new JsonObject { ["execution"] = new JsonObject { ["taskSupport"] = "optional" } }
                    : null
            },
            SchemaJson = metadata.SchemaJson,
            BridgeTool = metadata.BridgeTool,
            IsForwardedToUnityQueue = metadata.IsForwardedToUnityQueue,
            SupportsTaskExecution = metadata.SupportsTaskExecution,
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
        bool IsForwardedToUnityQueue,
        bool SupportsTaskExecution);
}
