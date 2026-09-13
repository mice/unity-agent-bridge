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
    private readonly McpTaskAdapter _taskAdapter;
    private readonly McpTaskExecutionCoordinator? _taskCoordinator;

    public McpServerService(ExternalBridgeClient client, McpHostDiagnostics diagnostics, McpStageLogger stageLogger, McpTaskAdapter? taskAdapter = null, McpTaskExecutionCoordinator? taskCoordinator = null)
    {
        _client = client;
        _diagnostics = diagnostics;
        _stageLogger = stageLogger;
        var queuePaths = McpToolRuntimeContext.QueuePaths ?? new QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
        _taskAdapter = taskAdapter ?? new McpTaskAdapter(client, queuePaths, diagnostics.ProjectPath);
        _taskCoordinator = taskCoordinator;
    }

    public bool SupportsTasks(JsonElement? clientCapabilities) => _taskAdapter.Supports(clientCapabilities);

    public Task<JsonObject> CreateTaskAsync(string taskType, string payload, JsonElement? clientCapabilities, CancellationToken cancellationToken)
    {
        if (!SupportsTasks(clientCapabilities))
            return Task.FromResult(new JsonObject { ["success"] = false, ["errorCode"] = "TASK_CAPABILITY_REQUIRED", ["errorMessage"] = "The client did not advertise MCP task support." });
        return _taskAdapter.CreateAsync(taskType, payload, cancellationToken);
    }

    public Task<JsonObject> GetTaskAsync(string taskId, CancellationToken cancellationToken) => _taskAdapter.GetAsync(taskId, cancellationToken);
    public Task<JsonObject> GetTaskResultAsync(string taskId, CancellationToken cancellationToken) => _taskAdapter.ResultAsync(taskId, cancellationToken);
    public Task<JsonObject> CancelTaskAsync(string taskId, CancellationToken cancellationToken) => _taskAdapter.CancelAsync(taskId, cancellationToken);

    public ListToolsResult ListTools(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ListToolsResult
        {
            Tools = McpToolCatalog.GetTools(_diagnostics)
                .Select(definition => definition.ProtocolTool)
                .ToList()
        };
    }

    public async Task<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        var toolName = request.Name ?? string.Empty;
        var commandId = _client.CreateCommandId();
        _stageLogger.Write("mcp.received", commandId, toolName, "ok", "MCP tool request received.");

        var definition = McpToolCatalog.TryGet(toolName, _diagnostics);
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
            var resultJson = mappedTaskId is not null && _taskCoordinator is not null && _taskCoordinator.IsMapped(mappedTaskId)
                ? (await _taskCoordinator.WaitForTerminalAsync(mappedTaskId, cancellationToken)).ToJsonString()
                : await definition.InvokeAsync(argumentsJson, cancellationToken);
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
            if (McpTaskExecutionContext.CurrentTaskId is not null)
                McpTaskExecutionContext.Clear();
        }
    }

    internal static CallToolResult AdaptResult(string rawJson, McpHostDiagnostics diagnostics)
    {
        var structured = JsonNode.Parse(rawJson)?.AsObject() ?? new JsonObject();
        InjectDiagnostics(structured, diagnostics);
        var normalizedJson = structured.ToJsonString(JsonOptions);
        var status = structured["status"]?.GetValue<string>() ?? string.Empty;

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = normalizedJson
                }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(structured, JsonOptions),
            IsError = status != "success"
        };
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

    private static void InjectDiagnostics(JsonObject structured, McpHostDiagnostics diagnostics)
    {
        structured["resolvedCliPath"] = diagnostics.ResolvedCliPath;
        structured["cliMode"] = diagnostics.CliMode;
        structured["cliWarnings"] = new JsonArray(diagnostics.CliWarnings.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray());
        structured["packageVersion"] = RuntimeIdentity.PackageVersion;
        structured["runtimeVersion"] = RuntimeIdentity.RuntimeVersion;
        structured["protocolVersion"] = RuntimeIdentity.ProtocolVersion;
        structured["runtimeMode"] = RuntimeIdentity.RuntimeMode;
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
