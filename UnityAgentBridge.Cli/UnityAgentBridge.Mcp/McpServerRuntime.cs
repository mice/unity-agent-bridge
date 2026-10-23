using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Extensions.Tasks;
using System.Text.Json.Nodes;
using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

#pragma warning disable MCPEXP002

public static class McpServerRuntime
{
    private static McpServerService? _service;
    public static async Task RunAsync(CancellationToken cancellationToken, McpRoutingOptions? routingOptions = null)
    {
        var routing = routingOptions ?? McpRoutingOptions.Parse();
        var diagnostics = routing.MultiProject ? McpHostDiagnostics.ForWorkspace(routing.WorkspaceRoot) : McpHostDiagnostics.Resolve();
        var queuePaths = new QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
        if (!routing.MultiProject) CommandStore.EnsureQueueDirectories(queuePaths);
        var multiProject = routing.MultiProject ? new McpMultiProjectHost(routing, diagnostics) : null;
        using var hostLease = multiProject is null ? null : McpHostLease.Create(multiProject.ConnectionId);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var externalClient = new ExternalBridgeClient();
        builder.Services.AddSingleton(externalClient);
        builder.Services.AddSingleton(diagnostics);
        if (multiProject is not null) builder.Services.AddSingleton(multiProject);
        builder.Services.AddSingleton(queuePaths);
        builder.Services.AddSingleton(new McpStageLogger(diagnostics.ServerLogPath));
        var taskAdapter = multiProject is null ? new McpTaskAdapter(externalClient, queuePaths, diagnostics.ProjectPath) : null;
        var taskCoordinator = taskAdapter is null ? null : new McpTaskExecutionCoordinator(taskAdapter);
        var taskStore = multiProject is null ? new McpTaskStore(diagnostics.ProjectPath, taskCoordinator) : null;
        if (taskAdapter is not null) builder.Services.AddSingleton(taskAdapter);
        if (taskCoordinator is not null) builder.Services.AddSingleton(taskCoordinator);
        if (taskStore is not null) builder.Services.AddSingleton(taskStore);
        if (multiProject is null) builder.Services.AddHostedService<McpTaskMaintenanceService>();
        else builder.Services.AddHostedService<McpMultiProjectMaintenanceService>();
        builder.Services.AddSingleton<McpServerService>();
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "unity-agent-bridge-mcp",
                    Version = RuntimeIdentity.RuntimeVersion
                };
                options.ServerInstructions = McpServerInstructions.Value;
                options.Capabilities ??= new ServerCapabilities();
                options.Capabilities.Extensions ??= new Dictionary<string, object>();
                options.Capabilities.Extensions[TasksProtocol.ExtensionId] = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["operations"] = new JsonArray("tasks/get", "tasks/update", "tasks/cancel"),
                    ["legacyOperations"] = new JsonArray("tasks/create", "tasks/result")
                });
                options.RequestHandlers = new List<McpServerRequestHandler>
                {
                    CreateTaskRequestHandler("tasks/create", HandleTaskCreateAsync),
                    CreateTaskRequestHandler("tasks/result", HandleTaskResultAsync),
                };
            })
            .WithTasks((ModelContextProtocol.Extensions.Tasks.IMcpTaskStore?)multiProject?.Store ?? taskStore!, options => options.ExecutionModeSelector = context =>
            {
                var toolName = context.Params?.Name ?? string.Empty;
                var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>();
                var argumentsJson = JsonSerializer.Serialize(arguments);
                ProjectContext? projectContext = null;
                McpToolDefinition? definition;
                try
                {
                    if (multiProject is not null && !McpProjectRouter.IsHostTool(toolName))
                    {
                        projectContext = multiProject.Resolve(toolName, JsonNode.Parse(argumentsJson)!.AsObject());
                        definition = multiProject.Definition(toolName, projectContext);
                        if (definition is not null) McpArgumentValidator.ValidateOrThrow(toolName, McpMultiProjectHost.PublicTool(definition).InputSchema.GetRawText(), argumentsJson);
                        argumentsJson = McpProjectRouter.BusinessArguments(JsonNode.Parse(argumentsJson)!.AsObject()).ToJsonString();
                    }
                    else definition = McpToolCatalog.TryGet(toolName, diagnostics, queuePaths, externalClient);
                }
                catch (Exception e) when (e is McpProjectRoutingException or McpArgumentValidationException or InvalidOperationException or ArgumentException)
                { McpTaskExecutionContext.Clear(); return McpTaskExecutionMode.Synchronous; }
                if (definition?.SupportsTaskExecution == true)
                {
                    try { McpArgumentValidator.ValidateOrThrow(definition.ProtocolTool.Name, definition.SchemaJson, argumentsJson); }
                    catch (McpArgumentValidationException)
                    {
                        McpTaskExecutionContext.Clear();
                        return McpTaskExecutionMode.Synchronous;
                    }
                    McpTaskExecutionContext.Prepare(context.Params?.Name ?? string.Empty, argumentsJson, projectContext);
                }
                return definition?.SupportsTaskExecution == true
                    ? McpTaskExecutionMode.Optional
                    : McpTaskExecutionMode.Synchronous;
            })
            .WithStdioServerTransport()
            .WithListToolsHandler(ListToolsAsync)
            .WithCallToolHandler(CallToolAsync);

        using var host = builder.Build();
        _service = host.Services.GetRequiredService<McpServerService>();
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

    private static McpServerRequestHandler CreateTaskRequestHandler(string method, Func<McpServerRequestHandlerContext, Task<JsonNode>> handler)
    {
        return new McpServerRequestHandler
        {
            Method = method,
            Handler = async (request, cancellationToken) => await handler(new McpServerRequestHandlerContext(request, cancellationToken))
        };
    }

    private sealed record McpServerRequestHandlerContext(JsonRpcRequest Request, CancellationToken CancellationToken);

    private static async Task<JsonNode> HandleTaskCreateAsync(McpServerRequestHandlerContext context)
    {
        var service = ResolveService(context.Request);
        var parameters = context.Request.Params?.AsObject() ?? new JsonObject();
        var taskType = parameters["taskType"]?.GetValue<string>() ?? "run_editmode_tests";
        var payload = parameters["payload"]?.GetValue<string>() ?? parameters["filter"]?.GetValue<string>() ?? string.Empty;
        var result = await service.CreateTaskAsync(taskType, payload, SerializeCapabilities(context.Request.Context?.ClientCapabilities), context.CancellationToken,
            parameters["projectPath"]?.GetValue<string>(), parameters["projectId"]?.GetValue<string>());
        return result;
    }

    private static Task<JsonNode> HandleTaskGetAsync(McpServerRequestHandlerContext context) => HandleMappedTaskAsync(context, false);
    private static Task<JsonNode> HandleTaskResultAsync(McpServerRequestHandlerContext context) => HandleMappedTaskAsync(context, true);

    private static async Task<JsonNode> HandleMappedTaskAsync(McpServerRequestHandlerContext context, bool result)
    {
        var service = ResolveService(context.Request);
        var parameters = context.Request.Params?.AsObject() ?? new JsonObject();
        var taskId = parameters["taskId"]?.GetValue<string>() ?? string.Empty;
        return result
            ? await service.GetTaskResultAsync(taskId, context.CancellationToken)
            : await service.GetTaskAsync(taskId, context.CancellationToken);
    }

    private static async Task<JsonNode> HandleTaskCancelAsync(McpServerRequestHandlerContext context)
    {
        var service = ResolveService(context.Request);
        var parameters = context.Request.Params?.AsObject() ?? new JsonObject();
        var taskId = parameters["taskId"]?.GetValue<string>() ?? string.Empty;
        return await service.CancelTaskAsync(taskId, context.CancellationToken);
    }

    private static McpServerService ResolveService(JsonRpcRequest request)
    {
        return _service ?? throw new InvalidOperationException("MCP service is unavailable.");
    }

    private static JsonElement? SerializeCapabilities(ClientCapabilities? capabilities)
    {
        if (capabilities is null) return null;
        return System.Text.Json.JsonSerializer.SerializeToElement(capabilities);
    }
}
