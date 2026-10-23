using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public sealed class McpServerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ExternalBridgeClient _client;
    private readonly McpHostDiagnostics _diagnostics;
    private readonly McpStageLogger _stageLogger;
    private readonly McpTaskAdapter? _taskAdapter;
    private readonly McpTaskExecutionCoordinator? _taskCoordinator;
    private readonly McpMultiProjectHost? _multiProject;
    private readonly QueuePaths _queuePaths;

    public McpServerService(ExternalBridgeClient client, McpHostDiagnostics diagnostics, McpStageLogger stageLogger, McpTaskAdapter? taskAdapter = null, McpTaskExecutionCoordinator? taskCoordinator = null, McpMultiProjectHost? multiProject = null, QueuePaths? paths = null)
    {
        _client = client;
        _diagnostics = diagnostics;
        _stageLogger = stageLogger;
        var queuePaths = paths ?? (multiProject is null ? McpToolRuntimeContext.QueuePaths : null) ?? new QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
        _queuePaths = queuePaths;
        _multiProject = multiProject;
        _taskAdapter = taskAdapter ?? (multiProject is null ? new McpTaskAdapter(client, queuePaths, diagnostics.ProjectPath) : null);
        _taskCoordinator = taskCoordinator;
    }

    public bool SupportsTasks(JsonElement? clientCapabilities) => _taskAdapter?.Supports(clientCapabilities) ??
        (clientCapabilities is { ValueKind: JsonValueKind.Object } value && (value.TryGetProperty("tasks", out _) || (value.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty(McpTaskAdapter.ExtensionId, out _))));

    public Task<JsonObject> CreateTaskAsync(string taskType, string payload, JsonElement? clientCapabilities, CancellationToken cancellationToken, string? projectPath = null, string? projectId = null)
    {
        if (!SupportsTasks(clientCapabilities))
            return Task.FromResult(new JsonObject { ["success"] = false, ["errorCode"] = "TASK_CAPABILITY_REQUIRED", ["errorMessage"] = "The client did not advertise MCP task support." });
        if (_multiProject is not null)
        {
            JsonObject selectors;
            try { selectors = JsonNode.Parse(payload) as JsonObject ?? new(); } catch (JsonException) { selectors = new(); }
            if (projectPath is not null) selectors["projectPath"] = projectPath;
            if (projectId is not null) selectors["projectId"] = projectId;
            var context = _multiProject.Resolve("tasks/create", selectors);
            string businessPayload = payload;
            try { if (JsonNode.Parse(payload) is JsonObject body) businessPayload = McpProjectRouter.BusinessArguments(body).ToJsonString(); } catch (JsonException) { }
            return CreateRoutedTaskAsync(context, taskType, businessPayload, selectors, cancellationToken);
        }
        return _taskAdapter!.CreateAsync(taskType, payload, cancellationToken);
    }
    private async Task<JsonObject> CreateRoutedTaskAsync(ProjectContext context, string taskType, string payload, JsonObject selectors, CancellationToken cancellationToken)
    {
        var toolName = taskType switch
        {
            "compile" => "unity_project_compile",
            "run_editmode_tests" => "unity_tests_run_edit_mode",
            "run_playmode_tests" => "unity_tests_run_play_mode",
            "analysis_compile_failure" => "unity_analysis_compile_failure",
            "analysis_asset_problem" => "unity_analysis_asset_problem",
            "template_run" => "unity_agent_task_template_run",
            _ => throw new McpProjectRoutingException("PROJECT_TASK_TYPE_UNSUPPORTED", "Task type has no supported public tool contract: " + taskType)
        };
        var host = _multiProject!;
        var definition = host.Definition(toolName, context) ?? throw new McpProjectRoutingException("PROJECT_TOOL_NOT_FOUND", "The selected project does not expose this task tool.");
        JsonObject? business = null;
        try { business = JsonNode.Parse(payload) as JsonObject; } catch (JsonException) { }
        // The legacy task protocol permits a plain filter string for test tasks.
        if (business is null && taskType is "run_editmode_tests" or "run_playmode_tests")
            business = string.IsNullOrEmpty(payload) ? new JsonObject() : new JsonObject { ["filter"] = payload };
        if (business is null && taskType == "compile" && string.IsNullOrWhiteSpace(payload)) business = new JsonObject();
        if (business is null) throw new McpProjectRoutingException("MCP_INVALID_ARGS", "Task payload must satisfy the public tool's object contract.");
        var publicArguments = (JsonObject)business.DeepClone();
        foreach (var key in new[] { "projectPath", "projectId" })
            if (selectors.TryGetPropertyValue(key, out var value)) publicArguments[key] = value?.DeepClone();
        McpArgumentValidator.ValidateOrThrow(toolName, McpMultiProjectHost.PublicTool(definition).InputSchema.GetRawText(), publicArguments.ToJsonString());
        McpArgumentValidator.ValidateOrThrow(toolName, definition.SchemaJson, business.ToJsonString());
        await host.ConfirmConnectionAsync(context, cancellationToken);
        // Revalidate immediately before allocation, including changes during the connection ping.
        _ = host.Definition(toolName, context) ?? throw new McpProjectRoutingException("PROJECT_TOOL_NOT_FOUND", "The selected project no longer exposes this task tool.");
        return await host.Get(context).Adapter.CreateAsync(taskType, payload, cancellationToken);
    }

    private McpTaskAdapter AdapterFor(string taskId)
    {
        if (_multiProject is null) return _taskAdapter!;
        var route = _multiProject.Index.FindTask(taskId) ?? throw new McpProjectRoutingException("TASK_PROJECT_INDEX_MISSING", "Task ownership is unavailable.");
        return new McpTaskAdapter(new ExternalBridgeClient { ConnectionId = route.ConnectionId }, route, _multiProject.Index);
    }
    public Task<JsonObject> GetTaskAsync(string taskId, CancellationToken cancellationToken) => AdapterFor(taskId).GetAsync(taskId, cancellationToken);
    public Task<JsonObject> GetTaskResultAsync(string taskId, CancellationToken cancellationToken) => AdapterFor(taskId).ResultAsync(taskId, cancellationToken);
    public Task<JsonObject> CancelTaskAsync(string taskId, CancellationToken cancellationToken) => AdapterFor(taskId).CancelAsync(taskId, cancellationToken);

    public ListToolsResult ListTools(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ListToolsResult
        {
            Tools = _multiProject is not null ? _multiProject.Tools().ToList() : McpToolCatalog.GetTools(_diagnostics, _queuePaths, _client, _taskAdapter)
                .Select(definition => definition.ProtocolTool)
                .ToList()
        };
    }

    public async Task<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        if (_multiProject is not null) return await CallRoutedToolAsync(request, cancellationToken);
        var toolName = request.Name ?? string.Empty;
        var commandId = _client.CreateCommandId();
        _stageLogger.Write("mcp.received", commandId, toolName, "ok", "MCP tool request received.");

        var definition = McpToolCatalog.TryGet(toolName, _diagnostics, _queuePaths, _client, _taskAdapter);
        if (definition is null)
        {
            _stageLogger.Write("mcp.validate", commandId, toolName, "invalid_args", "Unknown tool.");
            return CreateErrorResult(commandId, toolName, "invalid_args", $"Unknown tool '{toolName}'.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var argumentsJson = SerializeArguments(request.Arguments);
            McpArgumentValidator.ValidateOrThrow(toolName, definition.SchemaJson, argumentsJson);
            _stageLogger.Write("mcp.validate", commandId, toolName, "ok", "Arguments accepted.");
            _stageLogger.Write("mcp.invoke_core", commandId, toolName, "started", "Invoking ExternalBridgeClientCore.");
            var mappedTaskId = McpTaskExecutionContext.CurrentTaskId;
            string resultJson;
            if (mappedTaskId is not null)
            {
                var pending = McpTaskExecutionContext.ConsumePending();
                if (_taskCoordinator is null || pending is null || !_taskCoordinator.Supports(pending))
                    throw new InvalidOperationException("No execution coordinator is available for the allocated MCP task.");
                await _taskCoordinator.CreateAsync(mappedTaskId, pending, cancellationToken);
                resultJson = (await _taskCoordinator.WaitForTerminalAsync(mappedTaskId, cancellationToken)).ToJsonString();
            }
            else
            {
                resultJson = await definition.InvokeAsync(argumentsJson, cancellationToken);
            }
            var status = ReadStatus(resultJson);
            if (RequiresWaitStage(definition, toolName))
            {
                _stageLogger.Write("mcp.wait_result", commandId, toolName, status, "Core invocation completed.");
            }

            var adapted = AdaptResult(resultJson, _diagnostics);
            _stageLogger.Write("mcp.return_response", commandId, toolName, status, "Returning MCP response.");
            return adapted;
        }
        catch (McpArgumentValidationException exception)
        {
            _stageLogger.Write("mcp.validate", commandId, toolName, "invalid_args", exception.Message);
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = exception.Message
                    }
                ],
                IsError = true
            };
        }
        catch (BridgeCommandValidationException exception)
        {
            _stageLogger.Write("mcp.validate", commandId, toolName, "invalid_args", exception.Message);
            return CreateErrorResult(commandId, toolName, "invalid_args", exception.Message);
        }
        catch (TimeoutException exception)
        {
            _stageLogger.Write("mcp.return_response", commandId, toolName, "timeout", exception.Message);
            return CreateErrorResult(commandId, toolName, "timeout", exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            _stageLogger.Write("mcp.return_response", commandId, toolName, "exception", exception.Message);
            return CreateErrorResult(commandId, toolName, "exception", exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The SDK owns background cancellation. Do not turn a cancelled wait
            // into a completed tool result while its Unity operation is still active.
            if (McpTaskExecutionContext.CurrentTaskId is not null) throw;
            const string message = "The MCP request was cancelled.";
            _stageLogger.Write("mcp.return_response", commandId, toolName, "cancelled", message);
            return CreateErrorResult(commandId, toolName, "cancelled", message);
        }
        catch (Exception exception)
        {
            _stageLogger.Write("mcp.return_response", commandId, toolName, "exception", exception.Message);
            Console.Error.WriteLine($"[unity-agent-bridge:mcp] {toolName}: {exception.Message}");
            return CreateErrorResult(commandId, toolName, "exception", exception.Message);
        }
        finally
        {
            McpTaskExecutionContext.Clear();
        }
    }

    internal static CallToolResult AdaptResult(string rawJson, McpHostDiagnostics diagnostics)
    {
        var structured = JsonNode.Parse(rawJson)?.AsObject() ?? new JsonObject();
        return McpToolResultConverter.Convert(structured, diagnostics);
    }

    private async Task<CallToolResult> CallRoutedToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        var host = _multiProject!;
        var name = request.Name ?? string.Empty;
        var arguments = JsonNode.Parse(SerializeArguments(request.Arguments))!.AsObject();
        try
        {
            if (name == "unity_projects_list")
            {
                McpArgumentValidator.ValidateOrThrow(name, McpMultiProjectHost.ProjectsTool().InputSchema.GetRawText(), arguments.ToJsonString());
                var registry = host.Registry.ReadSnapshot();
                var sessions = host.Discovery.List(registry.Registrations, arguments["includeOffline"]?.GetValue<bool>() ?? false);
                return AdaptResult(Newtonsoft.Json.JsonConvert.SerializeObject(new { success = true, projects = sessions,
                    registrationDiagnostics = registry.Diagnostics, toolDiagnostics = host.CatalogIssues() }), _diagnostics);
            }
            if (name == "unity_report_read")
            {
                McpArgumentValidator.ValidateOrThrow(name, McpMultiProjectHost.RetainedReportTool().InputSchema.GetRawText(), arguments.ToJsonString());
                var path = arguments["projectPath"]?.GetValue<string>();
                if (path is not null) path = Path.GetFullPath(path, host.Router.Options.WorkspaceRoot);
                var projectId = arguments["projectId"]?.GetValue<string>();
                McpTaskRoute? route = null;
                if (arguments["taskId"] is JsonValue task && task.TryGetValue<string>(out var taskId))
                    route = host.Index.FindTask(taskId) ?? throw new McpProjectRoutingException("TASK_PROJECT_INDEX_MISSING", "Persistent task ownership is unavailable.");
                if (arguments["commandId"] is JsonValue command && command.TryGetValue<string>(out var commandId))
                {
                    var commandRoute = host.Index.FindCommand(commandId) ?? throw new McpProjectRoutingException("COMMAND_PROJECT_INDEX_MISSING", "Persistent command ownership is unavailable.");
                    if (route is not null) McpTaskRouteIndex.AssertSelectors(route, commandRoute.ProjectPath, commandRoute.ProjectId);
                    route ??= commandRoute;
                }
                QueuePaths reportPaths;
                if (route is not null) { McpTaskRouteIndex.AssertSelectors(route, path, projectId); reportPaths = route.CreateQueuePaths(); }
                else
                {
                    var resolved = host.Router.ResolvePath(path, projectId);
                    var registered = host.Registry.Find(ProjectIdentityService.GetProjectId(resolved)) ?? throw new McpProjectRoutingException("PROJECT_NOT_REGISTERED", "Offline report reads require retained project registration or a bound handle.");
                    reportPaths = new QueuePaths(registered.projectPath, registered.queueRoot);
                }
                return AdaptResult(McpRetainedReportReader.Read(reportPaths, arguments, route).ToJsonString(), _diagnostics);
            }
            if (McpProjectRouter.IsHostTool(name))
            {
                var local = McpToolCatalog.TryGet(name, _diagnostics, _queuePaths, _client);
                if (local is null) throw new McpProjectRoutingException("TOOL_NOT_FOUND", "Unknown host tool.");
                McpArgumentValidator.ValidateOrThrow(name, local.SchemaJson, arguments.ToJsonString());
                return AdaptResult(await local.InvokeAsync(arguments.ToJsonString(), cancellationToken), _diagnostics);
            }
            if (name == "unity_bridge_wait_result")
            {
                var commandId = arguments["commandId"]?.GetValue<string>() ?? string.Empty;
                var route = host.Index.FindCommand(commandId) ?? throw new McpProjectRoutingException("COMMAND_PROJECT_INDEX_MISSING", "Persistent command ownership is unavailable; no queue was searched.");
                var path = arguments["projectPath"]?.GetValue<string>();
                McpTaskRouteIndex.AssertSelectors(route, path is null ? null : Path.GetFullPath(path, host.Router.Options.WorkspaceRoot), arguments["projectId"]?.GetValue<string>());
                var client = new ExternalBridgeClient { ConnectionId = route.ConnectionId };
                var local = McpToolCatalog.TryGet(name, _diagnostics with { ProjectPath = route.ProjectPath }, route.CreateQueuePaths(), client)!;
                McpArgumentValidator.ValidateOrThrow(name, McpMultiProjectHost.PublicTool(local).InputSchema.GetRawText(), arguments.ToJsonString());
                return AdaptResult(await local.InvokeAsync(McpProjectRouter.BusinessArguments(arguments).ToJsonString(), cancellationToken), _diagnostics);
            }
            var context = McpTaskExecutionContext.ConsumePending()?.ProjectContext ?? host.Resolve(name, arguments);
            var definition = host.Definition(name, context) ?? throw new McpProjectRoutingException("PROJECT_TOOL_NOT_FOUND", "The selected project does not expose this tool.");
            var publicTool = McpMultiProjectHost.PublicTool(definition);
            McpArgumentValidator.ValidateOrThrow(name, publicTool.InputSchema.GetRawText(), arguments.ToJsonString());
            var business = McpProjectRouter.BusinessArguments(arguments);
            McpArgumentValidator.ValidateOrThrow(name, definition.SchemaJson, business.ToJsonString());
            // Bound history does not require the Editor to be online or to retain its old session.
            if (name != "unity_bridge_wait_result") await host.ConfirmConnectionAsync(context, cancellationToken);
            var bucket = host.Get(context);
            var scopedDiagnostics = host.CatalogDiagnostics(context);
            var service = new McpServerService(bucket.Client, scopedDiagnostics, new McpStageLogger(scopedDiagnostics.ServerLogPath, context), bucket.Adapter, bucket.Coordinator, paths: context.QueuePaths);
            var result = await service.CallToolAsync(new CallToolRequestParams { Name = name,
                Arguments = business.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) }, cancellationToken);
            return result;
        }
        catch (McpProjectRoutingException e)
        {
            return AdaptResult(new JsonObject { ["success"] = false, ["errorCode"] = e.Code, ["reason"] = e.Message,
                ["candidates"] = new JsonArray(e.Candidates.Select(p => JsonValue.Create(p)).ToArray()), ["errors"] = new JsonArray(new JsonObject { ["code"] = e.Code, ["message"] = e.Message }) }.ToJsonString(), _diagnostics);
        }
        catch (Exception e) when (e is McpArgumentValidationException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            var prefix = e.Message.Split(':', 2)[0];
            var code = prefix == "TASK_PROJECT_CONFLICT" || prefix.StartsWith("REPORT_", StringComparison.Ordinal) || prefix.StartsWith("AGENTBRIDGE_REPORT_", StringComparison.Ordinal) ? prefix : "MCP_INVALID_ARGS";
            return AdaptResult(new JsonObject { ["success"] = false, ["errorCode"] = code, ["errors"] = new JsonArray(new JsonObject { ["code"] = code, ["message"] = e.Message }) }.ToJsonString(), _diagnostics);
        }
    }

    private static string SerializeArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(
            arguments.ToDictionary(
                pair => pair.Key,
                pair => JsonSerializer.Deserialize<JsonElement>(pair.Value.GetRawText(), JsonOptions)),
            JsonOptions);
    }

    private static string ReadStatus(string rawJson)
    {
        var structured = JsonNode.Parse(rawJson)?.AsObject();
        return structured?["status"]?.GetValue<string>() ?? string.Empty;
    }

    private static bool RequiresWaitStage(McpToolDefinition definition, string toolName)
    {
        return definition.IsForwardedToUnityQueue ||
               string.Equals(toolName, "unity_bridge_wait_result", StringComparison.Ordinal) ||
               string.Equals(toolName, "unity_bridge_submit_only", StringComparison.Ordinal);
    }

    private CallToolResult CreateErrorResult(string commandId, string toolName, string status, string message)
    {
        var payload = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["commandId"] = commandId,
            ["tool"] = toolName,
            ["success"] = false,
            ["status"] = status,
            ["summary"] = message,
            ["errors"] = new JsonArray
            {
                new JsonObject
                {
                    ["code"] = $"MCP_{status.ToUpperInvariant()}",
                    ["message"] = message
                }
            }
        };

        return AdaptResult(payload.ToJsonString(JsonOptions), _diagnostics);
    }
}
