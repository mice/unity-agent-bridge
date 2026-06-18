using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace UnityAgentBridge.Mcp;

public static class McpProbeRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var diagnostics = McpHostDiagnostics.Resolve();
        var queuePaths = new ExternalBridgeClientCore.QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
        ExternalBridgeClientCore.CommandStore.EnsureQueueDirectories(queuePaths);
        McpToolRuntimeContext.QueuePaths = queuePaths;

        try
        {
            var service = new McpServerService(
                new ExternalBridgeClientCore.ExternalBridgeClient(),
                diagnostics,
                new McpStageLogger(diagnostics.ServerLogPath));

            var tools = service.ListTools(cancellationToken);
            var pingResult = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "mcp__unity__ping",
                    Arguments = new Dictionary<string, JsonElement>()
                },
                cancellationToken);
            var echoResult = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "mcp_echo",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["value"] = JsonDocument.Parse("\"probe\"").RootElement.Clone(),
                        ["payload"] = JsonDocument.Parse("""{"source":"mcp-probe"}""").RootElement.Clone()
                    }
                },
                cancellationToken);
            var healthResult = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "unity_bridge_health",
                    Arguments = new Dictionary<string, JsonElement>()
                },
                cancellationToken);
            var projectInfoResult = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "mcp__unity__project_get_info",
                    Arguments = new Dictionary<string, JsonElement>()
                },
                cancellationToken);

            var payload = new
            {
                listedToolCount = tools.Tools.Count,
                toolNames = tools.Tools.Select(tool => tool.Name).ToArray(),
                pingResult = AdaptCallResult(pingResult),
                echoResult = AdaptCallResult(echoResult),
                healthResult = AdaptCallResult(healthResult),
                projectInfoResult = AdaptCallResult(projectInfoResult)
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
        finally
        {
            McpToolRuntimeContext.QueuePaths = null;
        }
    }

    private static object AdaptCallResult(CallToolResult result)
    {
        return new
        {
            content = result.Content
                .OfType<TextContentBlock>()
                .Select(block => new { type = "text", text = block.Text })
                .ToArray(),
            structuredContent = JsonDocument.Parse(result.StructuredContent.GetValueOrDefault().GetRawText()).RootElement.Clone(),
            isError = result.IsError
        };
    }
}
