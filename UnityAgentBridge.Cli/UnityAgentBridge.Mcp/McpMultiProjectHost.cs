using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Mcp;

public sealed class McpMultiProjectHost
{
    private readonly ConcurrentDictionary<string, Bucket> buckets = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> connectionGates = new();
    private readonly ConcurrentDictionary<string, bool> connectedSessions = new();
    private readonly McpHostDiagnostics diagnostics;
    public McpMultiProjectHost(McpRoutingOptions options, McpHostDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        Registry = new ProjectRegistry(); Discovery = new ProjectDiscoveryService(Registry);
        Index.RepairRegisteredProjects(Registry.List());
        Router = new(options, Registry, path => Discovery.CreateCandidate(path, connectionId: ConnectionId));
        Store = new McpProjectTaskStore(Index, context => Get(context).Store,
            route => string.IsNullOrWhiteSpace(route.EditorSessionId) ? new McpTaskStore(route) : Get(Restore(route)).Store,
            () => McpTaskExecutionContext.ConsumePending()?.ProjectContext);
    }
    public string ConnectionId { get; } = Guid.NewGuid().ToString("N");
    public ProjectRegistry Registry { get; }
    public ProjectDiscoveryService Discovery { get; }
    public McpProjectRouter Router { get; }
    public McpTaskRouteIndex Index { get; } = new();
    public McpProjectTaskStore Store { get; }
    public sealed record Bucket(ProjectContext Context, ExternalBridgeClient Client, McpTaskAdapter Adapter, McpTaskExecutionCoordinator Coordinator, McpTaskStore Store);
    public Bucket Get(ProjectContext context) => buckets.GetOrAdd(context.ProjectId + "/" + context.EditorSessionId, _ =>
    {
        var client = new ExternalBridgeClient { ConnectionId = context.ConnectionId, CommandDispatching = (paths, command) => Index.SaveCommand(command, McpTaskRoute.From(context)) };
        var adapter = new McpTaskAdapter(client, context, Index);
        var coordinator = new McpTaskExecutionCoordinator(adapter);
        return new(context, client, adapter, coordinator, new McpTaskStore(context, coordinator));
    });
    public static ProjectContext Restore(McpTaskRoute route) => new(new ProjectRegistration { projectId = route.ProjectId, projectPath = route.ProjectPath, queueRoot = route.QueueRoot },
        new EditorSessionInfo { projectId = route.ProjectId, editorSessionId = route.EditorSessionId ?? string.Empty }, route.CreateQueuePaths(), route.ConnectionId);
    public ProjectContext Resolve(string tool, JsonObject arguments)
    {
        var path = arguments["projectPath"]?.GetValue<string>();
        if (path is not null) path = Path.GetFullPath(path, Router.Options.WorkspaceRoot);
        var id = arguments["projectId"]?.GetValue<string>();
        McpTaskRoute? historical = null;
        if (arguments["taskId"] is JsonValue task && task.TryGetValue<string>(out var taskId)) historical = Index.FindTask(taskId);
        if (arguments["commandId"] is JsonValue command && command.TryGetValue<string>(out var commandId)) historical ??= Index.FindCommand(commandId);
        if (historical is not null) { McpTaskRouteIndex.AssertSelectors(historical, path, id); return Restore(historical); }
        if (tool == "unity_bridge_wait_result") throw new McpProjectRoutingException("COMMAND_PROJECT_INDEX_MISSING", "Persistent command ownership is unavailable; no queue was searched.");
        return Router.Resolve(path, id);
    }
    public McpToolDefinition? Definition(string name, ProjectContext context)
    {
        var bucket = Get(context);
        var definition = McpToolCatalog.TryGetProject(name, CatalogDiagnostics(context), CoreProtocolCompatible(context), context.QueuePaths, bucket.Client, bucket.Adapter);
        if (definition is null) return null;
        foreach (var registered in Registry.List())
        {
            McpToolDefinition? other;
            try { var otherContext = CurrentCatalogContext(registered); other = McpToolCatalog.TryGetProject(name, CatalogDiagnostics(otherContext), CoreProtocolCompatible(otherContext)); }
            catch (McpProjectRoutingException e) when (e.Code == "PROJECT_CAPABILITY_MISMATCH") { continue; }
            if (other is not null && !McpToolCatalog.BusinessSchemasEqual(other.SchemaJson, definition.SchemaJson))
                throw new McpProjectRoutingException("PROJECT_TOOL_CONTRACT_CONFLICT", "Registered projects expose incompatible contracts for " + name);
        }
        return definition;
    }
    private bool CoreProtocolCompatible(ProjectContext context)
    {
        var observed = Discovery.Observe(new ProjectRegistration { projectId = context.ProjectId, projectPath = context.ProjectPath, queueRoot = context.QueuePaths.QueueRoot });
        var session = observed.editorSession;
        // The core whitelist is the AgentTool baseline shipped with 1.4.3; schema 1 adds instance binding.
        return session?.schemaVersion == 1 && session.editorSessionId == context.EditorSessionId &&
            Version.TryParse(session.bridgeVersion?.Split('-')[0], out var version) && version.Major == 1 && version >= new Version(1, 4, 3);
    }
    public IReadOnlyList<Tool> Tools()
    {
        var groups = new Dictionary<string, List<McpToolDefinition>>(StringComparer.Ordinal);
        foreach (var tool in McpToolCatalog.GetBuiltInTools()) groups[tool.ProtocolTool.Name] = new() { tool };
        foreach (var registered in Registry.List())
        {
            try
            {
                var context = CurrentCatalogContext(registered);
                foreach (var tool in McpToolCatalog.GetProjectTools(CatalogDiagnostics(context), CoreProtocolCompatible(context)))
                {
                    if (!groups.TryGetValue(tool.ProtocolTool.Name, out var list)) groups[tool.ProtocolTool.Name] = list = new();
                    list.Add(tool);
                }
            }
            catch (McpProjectRoutingException) { /* Reported by CatalogIssues; other projects remain available. */ }
        }
        var result = new List<Tool>();
        foreach (var group in groups.Values)
        {
            if (group.Any(t => !McpToolCatalog.BusinessSchemasEqual(t.SchemaJson, group[0].SchemaJson))) continue;
            try { result.Add(PublicTool(group[0])); }
            catch (McpProjectRoutingException) { /* The incompatible tool remains rejected at call time. */ }
        }
        result.Add(ProjectsTool());
        result.RemoveAll(tool => tool.Name == "unity_report_read");
        result.Add(RetainedReportTool());
        foreach (var project in Registry.List())
        {
            try
            {
                var incompatible = McpToolCatalog.GetProjectCatalogIssues(CatalogDiagnostics(CurrentCatalogContext(project)));
                result.RemoveAll(tool => incompatible.ContainsKey(tool.Name));
            }
            catch (McpProjectRoutingException) { }
        }
        return result;
    }
    private ProjectContext CurrentCatalogContext(ProjectRegistration registration)
    {
        var observed = Discovery.Observe(registration);
        if (!observed.ready || observed.editorSession is null)
            throw new McpProjectRoutingException("PROJECT_CAPABILITY_MISMATCH", "Project has no ready capability session.");
        return new ProjectContext(registration, observed.editorSession, new QueuePaths(registration.projectPath, registration.queueRoot), ConnectionId);
    }
    internal McpHostDiagnostics CatalogDiagnostics(ProjectContext context)
    {
        void Validate()
        {
            var current = Discovery.Observe(new ProjectRegistration { projectId = context.ProjectId, projectPath = context.ProjectPath, queueRoot = context.QueuePaths.QueueRoot });
            ValidateCapabilityContext(context, current);
        }
        Validate();
        return diagnostics.ForProject(context) with { ExpectedCapabilityRevision = context.CapabilityRevision, ValidateCatalogSnapshot = Validate };
    }
    internal static void ValidateCapabilityContext(ProjectContext context, ProjectSessionInfo current)
    {
        if (!current.ready || current.editorSession is not { } session ||
            session.projectId != context.ProjectId || session.editorSessionId != context.EditorSessionId ||
            string.IsNullOrWhiteSpace(context.CapabilityRevision) || session.capabilityRevision != context.CapabilityRevision)
            throw new McpProjectRoutingException("PROJECT_CAPABILITY_MISMATCH",
                $"The bound Editor session or capability revision is no longer ready/current. availability={current.availability}; ready={current.ready}; reason={current.reason}; " +
                $"expectedSession={context.EditorSessionId}; observedSession={current.editorSession?.editorSessionId ?? "<missing>"}; " +
                $"expectedCapability={context.CapabilityRevision ?? "<missing>"}; observedCapability={current.editorSession?.capabilityRevision ?? "<missing>"}.");
    }
    public IReadOnlyList<object> CatalogIssues()
    {
        var entries = new List<(string Path, McpToolDefinition Tool)>();
        var issues = new List<object>();
        foreach (var project in Registry.List())
        {
            try
            {
                var context = CurrentCatalogContext(project);
                var scoped = CatalogDiagnostics(context);
                entries.AddRange(McpToolCatalog.GetProjectTools(scoped, CoreProtocolCompatible(context)).Select(tool => (project.projectPath, tool)));
                foreach (var issue in McpToolCatalog.GetProjectCatalogIssues(scoped))
                    issues.Add(new { tool = issue.Key, reason = issue.Value, projectPaths = new[] { project.projectPath } });
            }
            catch (McpProjectRoutingException e) { issues.Add(new { tool = "*", reason = e.Code, projectPaths = new[] { project.projectPath } }); }
        }
        foreach (var group in entries.GroupBy(x => x.Tool.ProtocolTool.Name))
        {
            if (group.Any(x => !McpToolCatalog.BusinessSchemasEqual(x.Tool.SchemaJson, group.First().Tool.SchemaJson)))
                issues.Add(new { tool = group.Key, reason = "PROJECT_TOOL_CONTRACT_CONFLICT", projectPaths = group.Select(x => x.Path).ToArray() });
            else
                try { PublicTool(group.First().Tool); }
                catch (McpProjectRoutingException e) { issues.Add(new { tool = group.Key, reason = e.Code, projectPaths = group.Select(x => x.Path).ToArray() }); }
        }
        return issues;
    }
    internal static Tool PublicTool(McpToolDefinition definition)
    {
        var tool = definition.ProtocolTool;
        if (McpProjectRouter.IsHostTool(tool.Name)) return tool;
        var schema = JsonNode.Parse(definition.SchemaJson)!.AsObject();
        var properties = schema["properties"] as JsonObject ?? new JsonObject();
        if (schema["properties"] is null) schema["properties"] = properties;
        if (properties.ContainsKey("projectPath") || properties.ContainsKey("projectId")) throw new McpProjectRoutingException("TOOL_ROUTING_FIELD_CONFLICT", "Tool schema uses a reserved routing field: " + tool.Name);
        properties["projectPath"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 };
        properties["projectId"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 };
        JsonElement? outputSchema = tool.OutputSchema;
        if (outputSchema is { } output)
        {
            var outputNode = JsonNode.Parse(output.GetRawText())!;
            AddOutputIdentity(outputNode);
            outputSchema = JsonSerializer.SerializeToElement(outputNode);
        }
        return new Tool { Name = tool.Name, Title = tool.Title, Description = tool.Description, InputSchema = JsonSerializer.SerializeToElement(schema), OutputSchema = outputSchema, Meta = tool.Meta };
    }
    private static void AddOutputIdentity(JsonNode node)
    {
        if (node is not JsonObject obj) return;
        if (obj["properties"] is JsonObject || obj["type"]?.ToString() == "object")
        {
            var properties = obj["properties"] as JsonObject ?? new JsonObject();
            if (obj["properties"] is null) obj["properties"] = properties;
            foreach (var name in new[] { "projectId", "projectPath", "editorSessionId", "connectionId" })
                if (!properties.ContainsKey(name)) properties[name] = new JsonObject { ["type"] = new JsonArray("string", "null") };
        }
        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
            if (obj[combinator] is JsonArray branches) foreach (var branch in branches) if (branch is not null) AddOutputIdentity(branch);
    }
    internal static Tool ProjectsTool() => new() { Name = "unity_projects_list", Title = "Unity Projects", Description = "List registered project sessions; includeOffline retains historical registrations.",
        InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"includeOffline":{"type":"boolean"}},"additionalProperties":false}""").RootElement.Clone() };
    internal static Tool RetainedReportTool() => new() { Name = "unity_report_read", Title = "Read Retained Unity Report", Description = "Read a retained report from its original project's local history, including while the Editor is offline.",
        InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"reportPath":{"type":"string","minLength":1},"jsonPointer":{"type":"string"},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":500},"maxBytes":{"type":"integer","minimum":1,"maximum":262144},"projectPath":{"type":"string","minLength":1},"projectId":{"type":"string","minLength":1},"taskId":{"type":"string","minLength":1},"commandId":{"type":"string","minLength":1}},"required":["reportPath"],"additionalProperties":false}""").RootElement.Clone() };
    public async Task ConfirmConnectionAsync(ProjectContext context, CancellationToken cancellationToken)
    {
        var key = context.ProjectId + "/" + context.EditorSessionId;
        var gate = connectionGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (connectedSessions.ContainsKey(key)) return;
            var bucket = Get(context);
            var result = await bucket.Client.ExecuteAsync(context.QueuePaths, bucket.Client.CreateCommandId(), new BridgeCommandSpec("unity.ping", 5000, "{}"), cancellationToken);
            if (JsonNode.Parse(result.RawJson)?["success"]?.GetValue<bool>() != true) throw new McpProjectRoutingException("PROJECT_CONNECTION_FAILED", "The benign connection probe did not succeed.");
            var candidate = Discovery.CreateCandidate(context.ProjectPath, context.QueuePaths.QueueRoot, ConnectionId);
            if (candidate.EditorSessionId != context.EditorSessionId) throw new McpProjectRoutingException("EDITOR_SESSION_MISMATCH", "Editor restarted during connection.");
            Registry.RegisterSuccessfulConnection(new ProjectRegistration { projectId = context.ProjectId, projectPath = context.ProjectPath, queueRoot = context.QueuePaths.QueueRoot },
                new EditorSessionInfo { projectId = context.ProjectId, editorSessionId = context.EditorSessionId, bridgeEnabled = true });
            Discovery.PersistVerifiedObservation(new ProjectRegistration { projectId = context.ProjectId, projectPath = context.ProjectPath, queueRoot = context.QueuePaths.QueueRoot }, context.EditorSessionId);
            connectedSessions[key] = true;
        }
        finally { gate.Release(); }
    }
    internal async Task MaintainAsync(CancellationToken cancellationToken)
    {
        foreach (var bucket in buckets.Values)
        {
            try { await bucket.Store.MaintainAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e) { Console.Error.WriteLine("MCP project maintenance " + bucket.Context.ProjectId + ": " + e.Message); }
        }
        Index.Cleanup();
    }
}
