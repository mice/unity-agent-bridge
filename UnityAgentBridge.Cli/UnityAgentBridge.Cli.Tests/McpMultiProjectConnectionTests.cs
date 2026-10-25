using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class McpMultiProjectConnectionTests
{
    private string root = null!;
    private string? previousRegistry;
    private readonly List<ProjectContext> projects = new();
    private readonly ConcurrentDictionary<string, JsonObject> commands = new();
    private CancellationTokenSource lifetime = null!;
    private Task responder = null!;
    private McpHostDiagnostics diagnostics = null!;
    private McpMultiProjectHost host = null!;
    private McpServerService service = null!;
    private volatile bool finishTasks;
    private TaskCompletionSource querySeen = NewSignal();
    private TaskCompletionSource releaseQuery = NewSignal();
    private volatile bool holdQuery;
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), nameof(McpMultiProjectConnectionTests), Guid.NewGuid().ToString("N"));
        previousRegistry = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT");
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", Path.Combine(root, "registry"));
        diagnostics = McpHostDiagnostics.ForWorkspace(root) with { ServerLogPath = Path.Combine(root, "host.log") };
        host = new(new("multi-project", root), diagnostics);
        service = new(new ExternalBridgeClient(), diagnostics, new McpStageLogger(diagnostics.ServerLogPath), multiProject: host);
        foreach (var name in new[] { "A", "B" })
        {
            var path = Path.Combine(root, name);
            // Protocol/file fixture only: no Unity process, package, or imported project cache.
            Directory.CreateDirectory(Path.Combine(path, "Assets"));
            Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
            File.WriteAllText(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.53f1");
            var catalog = Path.Combine(path, "Library", "AgentBridge", "plugin-catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
            var tools = new[] { ("unity_editor_ping", "unity.ping"), ("unity_project_compile", "unity.compile") }.Select(pair => new {
                pluginId = "com.example.connection-fixture", pluginVersion = "1.0.0", assemblyName = "Fixture.Tools",
                bridgeTool = pair.Item2, mcpName = pair.Item1, title = pair.Item1, description = "Protocol fixture",
                defaultTimeoutMs = 5000, allowedRuntimeModes = "Edit", sideEffect = "RunsUserCode", mayTriggerDomainReload = false,
                inputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}" });
            File.WriteAllText(catalog, JsonSerializer.Serialize(new { version = 1, tools }));
            using var process = Process.GetCurrentProcess();
            var start = process.StartTime.ToUniversalTime();
            var id = ProjectIdentityService.GetProjectId(path);
            var session = new EditorSessionInfo { projectId = id, editorSessionId = process.Id + ":" + start.Ticks,
                processId = process.Id, processStartTime = start.ToString("O"), heartbeatUtc = DateTime.UtcNow.ToString("O"),
                revision = 1, bridgeEnabled = true, bridgeVersion = "1.4.3",
                capabilityRevision = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(catalog))).ToLowerInvariant() };
            var paths = new QueuePaths(path, Path.Combine(path, "Temp", "AgentBridge"));
            CommandStore.EnsureQueueDirectories(paths);
            File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), JsonSerializer.Serialize(
                new { projectPath = path, heartbeatUtc = session.heartbeatUtc, currentStage = "unity.poller.idle", editorSession = session },
                new JsonSerializerOptions { IncludeFields = true }));
            projects.Add(host.Discovery.CreateCandidate(path, connectionId: host.ConnectionId));
        }
        lifetime = new(TimeSpan.FromSeconds(30));
        responder = Task.Run(() => RespondAsync(lifetime.Token));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        releaseQuery.TrySetResult();
        lifetime.Cancel();
        try { try { await responder; } catch (OperationCanceledException) { } }
        finally
        {
            lifetime.Dispose();
            McpTaskExecutionContext.Clear();
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", previousRegistry);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private async Task RespondAsync(CancellationToken token)
    {
        var replies = new List<Task>();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                foreach (var project in projects)
                    foreach (var file in Directory.EnumerateFiles(project.QueuePaths.InboxDirectory, "*.json"))
                    {
                        if (commands.ContainsKey(Path.GetFileNameWithoutExtension(file))) continue;
                        JsonObject command;
                        try { command = JsonNode.Parse(File.ReadAllText(file))!.AsObject(); }
                        catch (IOException) { continue; } // A concurrent writer may still own the newly published file.
                        var id = command["commandId"]!.GetValue<string>();
                        if (commands.TryAdd(id, command)) replies.Add(ReplyAsync(project, command, token));
                    }
                await Task.Delay(5, token);
            }
        }
        finally { await Task.WhenAll(replies); }
    }

    private async Task ReplyAsync(ProjectContext project, JsonObject command, CancellationToken token)
    {
        var id = command["commandId"]!.GetValue<string>();
        var response = new JsonObject { ["commandId"] = id, ["success"] = true, ["status"] = "success" };
        if (command["tool"]!.GetValue<string>() == "unity.agent_task_control")
        {
            var operation = command["args"]!["operation"]!.GetValue<string>();
            if (operation == "query" && holdQuery)
            {
                querySeen.TrySetResult();
                await releaseQuery.Task.WaitAsync(token);
            }
            response["metrics"] = new JsonObject { ["agentTaskId"] = command["args"]!["agentTaskId"]?.DeepClone() ?? JsonValue.Create("agent-" + id),
                ["state"] = finishTasks ? "Completed" : "Running", ["resultKind"] = finishTasks ? "Succeeded" : "None" };
        }
        var target = Path.Combine(project.QueuePaths.OutboxDirectory, id + ".result.json");
        File.WriteAllText(target + ".tmp", response.ToJsonString());
        File.Move(target + ".tmp", target);
    }

    private async Task<(string Id, ProjectContext Context, string Original)> SeedAsync(int project, string? connection, bool active = false, string? editorSession = null)
    {
        var context = McpMultiProjectHost.Restore(McpTaskRoute.From(projects[project]) with {
            ConnectionId = connection, EditorSessionId = editorSession ?? projects[project].EditorSessionId });
        // A separate original host prevents prewarming the replacement host's cache.
        var original = new McpMultiProjectHost(new("multi-project", root), diagnostics);
        var bucket = original.Get(context);
        var task = await bucket.Store.CreateTaskAsync(lifetime.Token);
        original.Index.SaveTask(task.TaskId, McpTaskRoute.From(context));
        var result = JsonSerializer.SerializeToElement(new { projectId = context.ProjectId, editorSessionId = context.EditorSessionId,
            connectionId = connection, taskId = task.TaskId, marker = "original" });
        if (active)
            new McpTaskBindingStore(context.ProjectPath).Save(new(context.ProjectPath, task.TaskId, "agent-" + task.TaskId, "original-" + task.TaskId,
                ProjectId: context.ProjectId, EditorSessionId: context.EditorSessionId, ConnectionId: connection));
        else await bucket.Store.SetCompletedAsync(task.TaskId, result, lifetime.Token);
        return (task.TaskId, context, result.GetRawText());
    }

    private async Task<JsonObject> CallAsync(string name, object arguments)
    {
        var result = await service.CallToolAsync(new CallToolRequestParams { Name = name,
            Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments)) }, lifetime.Token);
        Assert.IsFalse(result.IsError == true, JsonSerializer.Serialize(result));
        return JsonNode.Parse(JsonSerializer.Serialize(result.StructuredContent))!.AsObject();
    }

    private async Task<JsonObject> PingAsync(int project)
    {
        var result = await CallAsync("unity_editor_ping", new { projectPath = projects[project].ProjectPath });
        Assert.AreEqual(host.ConnectionId, result["connectionId"]?.GetValue<string>(), "New invocation must use replacement host connection.");
        Assert.AreEqual(projects[project].ProjectId, result["projectId"]?.GetValue<string>());
        var id = result["commandId"]!.GetValue<string>();
        Assert.AreEqual(host.ConnectionId, host.Index.FindCommand(id)!.ConnectionId, "Persisted command origin must agree with response.");
        var logs = File.ReadLines(diagnostics.ForProject(projects[project]).ServerLogPath).Select(line => JsonNode.Parse(line)!).Where(line => line["tool"]?.GetValue<string>() == "unity_editor_ping").ToArray();
        Assert.IsTrue(logs.Length > 0);
        Assert.IsTrue(logs.All(line => line["connectionId"]?.GetValue<string>() == host.ConnectionId));
        return result;
    }

    // AGBM_301: observable service result + durable route regression, both cache creation orders.
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task HistoryAndNewCallsPreserveIndependentOrigins(bool historyFirst)
    {
        var old = await SeedAsync(1, "C1");
        if (historyFirst) Assert.AreEqual(old.Original, (await host.Store.GetTaskAsync(old.Id, lifetime.Token))!.Result!.Value.GetRawText());
        await PingAsync(1);
        Assert.AreEqual(old.Original, (await host.Store.GetTaskAsync(old.Id, lifetime.Token))!.Result!.Value.GetRawText());
        await PingAsync(1);
        Assert.AreEqual("C1", host.Index.FindTask(old.Id)!.ConnectionId);
        Assert.AreEqual(3, commands.Count, "One connection probe and two user pings; no task replay.");
    }

    // AGBM_302: multiple histories, unknown origin, A/B interleaving, bound offline report.
    [TestMethod]
    public async Task InterleavedProjectsAndUnknownHistoryKeepOriginalBindingsAndReports()
    {
        var histories = new[] { await SeedAsync(1, "C0"), await SeedAsync(1, "C1"), await SeedAsync(0, null) };
        foreach (var old in histories)
        {
            Assert.AreEqual(old.Original, (await host.Store.GetTaskAsync(old.Id, lifetime.Token))!.Result!.Value.GetRawText());
            var report = Path.Combine(old.Context.ProjectPath, "Library", "AgentBridge", "command-history", "report-" + old.Id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, old.Original);
            var read = await CallAsync("unity_report_read", new { taskId = old.Id, reportPath = report });
            Assert.AreEqual(old.Context.ConnectionId, read["connectionId"]?.GetValue<string>());
            await PingAsync(old.Context.ProjectId == projects[0].ProjectId ? 0 : 1);
        }
        foreach (var old in histories)
            Assert.AreEqual(old.Original, (await host.Store.GetTaskAsync(old.Id, lifetime.Token))!.Result!.Value.GetRawText());
        Assert.AreEqual(5, commands.Count);
    }

    // AGBM_303: hold a real historical observation across a new service invocation.
    [TestMethod]
    public async Task ConcurrentActiveRecoveryCannotContaminateNewDispatch()
    {
        var old = await SeedAsync(1, "C1", active: true);
        holdQuery = true;
        var recovery = host.Store.GetTaskAsync(old.Id, lifetime.Token);
        await querySeen.Task.WaitAsync(lifetime.Token);
        await PingAsync(1);
        finishTasks = true;
        releaseQuery.SetResult();
        var terminal = await recovery;
        Assert.AreEqual(McpTaskStatus.Completed, terminal!.Status);
        StringAssert.Contains(terminal.Result!.Value.GetRawText(), "C1");
        var count = commands.Count;
        await host.Store.GetTaskAsync(old.Id, lifetime.Token);
        Assert.AreEqual(count, commands.Count, "Terminal reads must remain local.");
        Assert.AreEqual(0, commands.Values.Count(IsCreate));
    }

    // AGBM_304: maintenance must query each active task once using its original Editor/connection.
    [TestMethod]
    public async Task MaintenanceRoutesEachActiveHistoryOnceAcrossConnectionScopes()
    {
        var first = await SeedAsync(1, "C0", active: true);
        var second = await SeedAsync(1, "C1", active: true);
        var exited = await SeedAsync(1, "C-exited", active: true, editorSession: "2147483647:1");
        await host.Store.GetTaskAsync(first.Id, lifetime.Token);
        await host.Store.GetTaskAsync(second.Id, lifetime.Token);
        await PingAsync(1);
        var before = commands.Count;
        await host.MaintainAsync(lifetime.Token);
        Assert.AreEqual(2, commands.Count - before, "One observation per active task, independent of cached connection count.");
        Assert.AreEqual(0, commands.Values.Count(IsCreate));
        Assert.AreEqual(3, Directory.GetFiles(Path.Combine(projects[1].ProjectPath, "Library", "AgentBridge", "mcp-tasks"), "*.json").Length);
        var interrupted = await host.Store.GetTaskAsync(exited.Id, lifetime.Token);
        Assert.AreEqual(McpTaskStatus.Failed, interrupted!.Status);
        StringAssert.Contains(interrupted.Error!.Value.GetRawText(), "AGENT_TASK_INTERRUPTED");
        var exitedRecord = McpTaskPersistence.Read<McpStoredTask>(new McpTaskPersistence(exited.Context.ProjectPath).PathFor("mcp-tasks", exited.Id))!;
        Assert.AreEqual("C-exited", exitedRecord.LastObservation!.Value.GetProperty("connectionId").GetString());
        Assert.AreEqual("2147483647:1", exitedRecord.LastObservation!.Value.GetProperty("editorSessionId").GetString());
        foreach (var old in new[] { first, second })
        {
            var record = McpTaskPersistence.Read<McpStoredTask>(new McpTaskPersistence(old.Context.ProjectPath).PathFor("mcp-tasks", old.Id))!;
            Assert.AreEqual(old.Context.ConnectionId, record.ConnectionId);
            Assert.AreEqual(old.Context.ConnectionId, record.LastObservation!.Value.GetProperty("connectionId").GetString());
        }
    }

    private static bool IsCreate(JsonObject command) => command["tool"]?.GetValue<string>() == "unity.agent_task_control" && command["args"]?["operation"]?.GetValue<string>() == "create";

    // AGBM_305: SDK allocation, adapter dispatch, submit/wait and public persistence agree.
    [TestMethod]
    public async Task NewNativeTaskAndSubmittedCommandUseCurrentConnectionAfterRecovery()
    {
        var old = await SeedAsync(1, "C1");
        await host.Store.GetTaskAsync(old.Id, lifetime.Token);
        var context = host.Resolve("unity_project_compile", new JsonObject { ["projectPath"] = projects[1].ProjectPath });
        McpTaskExecutionContext.Prepare("unity_project_compile", "{}", context);
        string taskId;
        try
        {
            var allocated = await host.Store.CreateTaskAsync(lifetime.Token);
            taskId = allocated.TaskId;
            finishTasks = true;
            var result = await CallAsync("unity_project_compile", new { projectPath = context.ProjectPath });
            Assert.AreEqual(host.ConnectionId, result["connectionId"]?.GetValue<string>());
            Assert.AreEqual(taskId, result["taskId"]?.GetValue<string>());
        }
        finally { McpTaskExecutionContext.Clear(); }
        var binding = new McpTaskBindingStore(context.ProjectPath).Load(context.ProjectPath, taskId)!;
        Assert.AreEqual(host.ConnectionId, binding.ConnectionId);
        Assert.AreEqual(host.ConnectionId, host.Index.FindTask(taskId)!.ConnectionId);
        Assert.AreEqual(host.ConnectionId, host.Index.FindCommand(binding.CommandId!)!.ConnectionId);
        Assert.AreEqual(host.ConnectionId, commands[binding.CommandId!]["connectionId"]?.GetValue<string>(), "The Editor must receive the admitted connection origin, not only a host-side projection.");
        var dispatch = new McpTaskDispatchStore(context.ProjectPath).Load(context.ProjectPath, taskId)!;
        Assert.AreEqual(dispatch.CreatedAt.ToString("O"), commands[binding.CommandId!]["args"]?["submittedAtUtc"]?.GetValue<string>(), "Creation carries the original persisted time origin through the wire.");
        var stored = McpTaskPersistence.Read<McpStoredTask>(new McpTaskPersistence(context.ProjectPath).PathFor("mcp-tasks", taskId))!;
        Assert.AreEqual(host.ConnectionId, stored.ConnectionId);
        Assert.AreEqual(McpTaskStatus.Completed, stored.Task.Status);
        var submitted = await CallAsync("unity_bridge_submit_only", new { projectPath = context.ProjectPath, tool = "unity.ping", args = new { } });
        Assert.AreEqual(host.ConnectionId, submitted["connectionId"]?.GetValue<string>());
        var commandId = submitted["commandId"]!.GetValue<string>();
        var waited = await CallAsync("unity_bridge_wait_result", new { commandId, timeoutMs = 5000 });
        Assert.AreEqual(host.ConnectionId, waited["connectionId"]?.GetValue<string>());
        Assert.AreEqual(host.ConnectionId, host.Index.FindCommand(commandId)!.ConnectionId);
        Assert.AreEqual(host.ConnectionId, commands[commandId]["connectionId"]?.GetValue<string>(), "Submit-only uses the same wire origin as ordinary/native work.");
        Assert.AreEqual(old.Original, (await host.Store.GetTaskAsync(old.Id, lifetime.Token))!.Result!.Value.GetRawText());
        var before = commands.Count;
        await host.MaintainAsync(lifetime.Token);
        Assert.AreEqual(before, commands.Count);
        Assert.AreEqual(1, commands.Values.Count(IsCreate));
        Assert.AreEqual(2, Directory.GetFiles(Path.Combine(context.ProjectPath, "Library", "AgentBridge", "mcp-tasks"), "*.json").Length);
    }
}
