using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public static class McpServerRuntime
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var diagnostics = McpHostDiagnostics.Resolve();
        var queuePaths = new QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
        CommandStore.EnsureQueueDirectories(queuePaths);
        McpToolRuntimeContext.QueuePaths = queuePaths;
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ExternalBridgeClient());
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton(queuePaths);
        builder.Services.AddSingleton(new McpStageLogger(diagnostics.ServerLogPath));
        builder.Services.AddSingleton<McpServerService>();
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "unity-agent-bridge-mcp",
                    Version = "1.1.6"
                };
            })
            .WithStdioServerTransport()
            .WithListToolsHandler(ListToolsAsync)
            .WithCallToolHandler(CallToolAsync);

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
    }

    private static ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken)
    {
        var service = context.Services!.GetRequiredService<McpServerService>();
        return ValueTask.FromResult(service.ListTools(cancellationToken));
    }

    private static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        var service = context.Services!.GetRequiredService<McpServerService>();
        return await service.CallToolAsync(context.Params, cancellationToken);
    }
}
