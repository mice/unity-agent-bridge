using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpMultiProjectHistoryTests
{
    private sealed class Scope : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), nameof(McpMultiProjectHistoryTests), Guid.NewGuid().ToString("N"));
        internal string Project(string name) => Path.Combine(Root, name);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    [TestMethod]
    public void RestartedIndexPreservesOriginalOwnerAndRejectsConflicts()
    {
        using var scope = new Scope();
        var root = Path.Combine(scope.Root, "index");
        var route = new McpTaskRoute("A", scope.Project("A"), Path.Combine(scope.Project("A"), "queue"), "session-A", "connection-1", DateTimeOffset.UtcNow);
        var first = new McpTaskRouteIndex(root);
        first.SaveTask("task", route);
        first.SaveCommand("command", route);
        var restarted = new McpTaskRouteIndex(root);
        Assert.AreEqual(route, restarted.FindTask("task"));
        Assert.AreEqual(route, restarted.FindCommand("command"));
        Assert.ThrowsException<InvalidOperationException>(() => restarted.SaveTask("task", route with { ProjectId = "B" }));
        Assert.ThrowsException<InvalidOperationException>(() => McpTaskRouteIndex.AssertSelectors(route, scope.Project("B"), null));
        Assert.AreEqual(0, Directory.EnumerateDirectories(scope.Root).Count(p => Path.GetFileName(p) != "index"));
    }
    [TestMethod]
    public async Task FacadeReadsCompletedOriginalProjectAfterReconnectWithoutCurrentContext()
    {
        using var scope = new Scope();
        var index = new McpTaskRouteIndex(Path.Combine(scope.Root, "index"));
        var store = new McpTaskStore(scope.Project("A"));
        var task = await store.CreateTaskAsync(default);
        await store.SetCompletedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { marker = "A" }), default);
        index.SaveTask(task.TaskId, new("A", scope.Project("A"), Path.Combine(scope.Project("A"), "queue"), "old-session", "old-connection", DateTimeOffset.UtcNow));
        var facade = new McpProjectTaskStore(new McpTaskRouteIndex(Path.Combine(scope.Root, "index")),
            _ => throw new AssertFailedException("History must not allocate."), route => new McpTaskStore(route.ProjectPath), () => null);
        var retained = await facade.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Completed, retained!.Status);
        Assert.AreEqual("A", retained.Result!.Value.GetProperty("marker").GetString());
        Assert.IsFalse(await facade.SetCancelledAsync(task.TaskId, default));
        Assert.IsFalse(Directory.Exists(scope.Project("B")));
    }
    [TestMethod]
    public void SameNamedReportsRemainProjectLocalAfterTransportRemoval()
    {
        using var scope = new Scope();
        foreach (var name in new[] { "A", "B" })
        {
            var project = scope.Project(name);
            Directory.CreateDirectory(Path.Combine(project, "Temp"));
            File.WriteAllText(Path.Combine(project, "Temp", "report.json"), "{\"marker\":\"" + name + "\"}");
            var paths = new QueuePaths(project, Path.Combine(project, "queue"));
            var history = new BridgeResultHistory(paths);
            history.Save("same-command", new("{\"success\":true,\"reportPath\":\"Temp/report.json\"}", "completed", false));
            Directory.Delete(Path.Combine(project, "Temp"), true);
            var result = JsonDocument.Parse(new BridgeResultHistory(paths).Read("same-command")!.RawJson);
            var retainedPath = Path.GetFullPath(result.RootElement.GetProperty("reportPath").GetString()!, project);
            Assert.AreEqual(name, JsonDocument.Parse(File.ReadAllText(retainedPath)).RootElement.GetProperty("marker").GetString());
        }
    }
    [TestMethod]
    public async Task AdapterTerminalReadAndCancelRemainLocalAfterRestart()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"), "A", "old-session");
        new BridgeResultHistory(paths).Save("task-mcp-done", new("{\"status\":\"completed\",\"success\":true,\"marker\":\"saved\"}", "completed", false));
        var adapter = new McpTaskAdapter(new ExternalBridgeClient(), new McpTaskRoute("A", project, paths.QueueRoot, null, "origin", DateTimeOffset.UtcNow));
        Assert.AreEqual("saved", (await adapter.GetAsync("mcp-done", default))["marker"]!.GetValue<string>());
        Assert.AreEqual("AlreadyTerminal", (await adapter.CancelAsync("mcp-done", default))["disposition"]!.GetValue<string>());
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
    }
    [TestMethod]
    public async Task AllocationPublishesPersistentMappingBeforeReturningHandle()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = "session-A" }, paths, "connection-A");
        var index = new McpTaskRouteIndex(Path.Combine(scope.Root, "index"));
        var facade = new McpProjectTaskStore(index, c => new McpTaskStore(c), r => new McpTaskStore(r.ProjectPath), () => context);
        var task = await facade.CreateTaskAsync(default);
        Assert.AreEqual("session-A", new McpTaskRouteIndex(Path.Combine(scope.Root, "index")).FindTask(task.TaskId)!.EditorSessionId);
        Assert.AreEqual("connection-A", index.FindTask(task.TaskId)!.ConnectionId);
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
    }
    [TestMethod]
    public async Task RegisteredLocalRecordsRepairIndexWithoutQueueSearch()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "custom-queue"));
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = "old-session" }, paths, "old-connection");
        var task = await new McpTaskStore(context).CreateTaskAsync(default);
        new BridgeResultHistory(context.QueuePaths, context.ConnectionId).Save("command", new("{\"success\":true}", "completed", false));
        var index = new McpTaskRouteIndex(Path.Combine(scope.Root, "index"));
        index.RepairRegisteredProjects(new[] { new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = "changed-queue" } });
        Assert.AreEqual("old-session", index.FindTask(task.TaskId)!.EditorSessionId);
        Assert.AreEqual(paths.QueueRoot, index.FindTask(task.TaskId)!.QueueRoot);
        Assert.AreEqual("old-connection", index.FindCommand("command")!.ConnectionId);
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
    }
    [TestMethod]
    public async Task IndexCleanupReclaimsOldOrphansAndPreservesActiveTaskOwnership()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var store = new McpTaskStore(project);
        var task = await store.CreateTaskAsync(default);
        var root = Path.Combine(scope.Root, "index");
        var index = new McpTaskRouteIndex(root);
        var route = new McpTaskRoute("A", project, Path.Combine(project, "queue"), "session", "connection", DateTimeOffset.UtcNow);
        index.SaveTask(task.TaskId, route);
        index.SaveTask("orphan", route);
        index.SaveCommand("pending-command", route);
        foreach (var file in Directory.EnumerateFiles(root)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));
        index.Cleanup();
        Assert.IsNotNull(index.FindTask(task.TaskId));
        Assert.IsNull(index.FindTask("orphan"));
        Assert.IsNotNull(index.FindCommand("pending-command"));
    }
    [TestMethod]
    public void FullIndexRejectsNewOwnershipWithoutDiscardingPendingCommands()
    {
        using var scope = new Scope();
        var index = new McpTaskRouteIndex(Path.Combine(scope.Root, "index"), maxRecords: 1);
        var route = new McpTaskRoute("A", scope.Project("A"), Path.Combine(scope.Project("A"), "queue"), "session", "connection", DateTimeOffset.UtcNow);
        index.SaveCommand("pending", route);
        StringAssert.Contains(Assert.ThrowsException<IOException>(() => index.SaveCommand("next", route)).Message, "CAPACITY");
        Assert.AreEqual(route, index.FindCommand("pending"));
        Assert.IsNull(index.FindCommand("next"));
    }
    [TestMethod]
    public async Task ReplacementHeartbeatDoesNotDispatchToNewInstanceOrInventInterruption()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var original = process.Id + ":" + process.StartTime.ToUniversalTime().Ticks;
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = original }, paths, "connection");
        new McpTaskBindingStore(project).Save(new(project, "mcp-active", "agent", "command", ProjectId: "A", EditorSessionId: original, ConnectionId: "origin"));
        Directory.CreateDirectory(paths.StatusDirectory);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), "{\"editorSession\":{\"editorSessionId\":\"replacement\"}}");
        var result = await new McpTaskAdapter(new ExternalBridgeClient(), context).GetAsync("mcp-active", default);
        Assert.AreEqual("AGENT_TASK_OUTCOME_UNKNOWN", result["errorCode"]!.GetValue<string>());
        Assert.AreEqual(original, result["editorSessionId"]!.GetValue<string>());
        Assert.AreEqual("origin", result["connectionId"]!.GetValue<string>());
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
    }
    [TestMethod]
    public async Task LegacyActiveHistoryIsExplicitlyUnconfirmedWithoutDispatch()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var route = new McpTaskRoute("A", project, Path.Combine(project, "queue"), null, "origin", DateTimeOffset.UtcNow);
        var task = await new McpTaskStore(project).CreateTaskAsync(default);
        var restored = await new McpTaskStore(route).GetTaskAsync(task.TaskId, default);
        StringAssert.Contains(restored!.StatusMessage!, "LEGACY_UNCONFIRMED");
        new McpTaskBindingStore(project).Save(new(project, task.TaskId, "agent", "command"));
        var projection = await new McpTaskAdapter(new ExternalBridgeClient(), route).GetAsync(task.TaskId, default);
        Assert.AreEqual("AGENT_TASK_LEGACY_UNCONFIRMED", projection["errorCode"]!.GetValue<string>());
        Assert.IsFalse(Directory.Exists(route.CreateQueuePaths().InboxDirectory));
    }
    [TestMethod]
    public void CompilePayloadCannotOverrideOriginalRouteIdentity()
    {
        var source = new System.Text.Json.Nodes.JsonObject
        {
            ["resultKind"] = "Succeeded", ["projectId"] = "A", ["projectPath"] = "original-A", ["editorSessionId"] = "session-A", ["connectionId"] = "origin-A",
            ["metrics"] = new System.Text.Json.Nodes.JsonObject { ["success"] = true, ["status"] = "success", ["projectId"] = "forged-B", ["connectionId"] = "forged-B" }
        };
        var result = McpToolResultConverter.Serialize(source).GetProperty("structuredContent");
        Assert.AreEqual("A", result.GetProperty("projectId").GetString());
        Assert.AreEqual("original-A", result.GetProperty("projectPath").GetString());
        Assert.AreEqual("session-A", result.GetProperty("editorSessionId").GetString());
        Assert.AreEqual("origin-A", result.GetProperty("connectionId").GetString());
    }
    [TestMethod]
    public void OfflineRetainedReportReadRejectsAnotherProjectsPath()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        Directory.CreateDirectory(Path.Combine(project, "Temp"));
        File.WriteAllText(Path.Combine(project, "Temp", "report.json"), "{\"marker\":\"A\"}");
        var history = new BridgeResultHistory(new QueuePaths(project, Path.Combine(project, "queue")));
        var saved = history.Save("command", new("{\"reportPath\":\"Temp/report.json\"}", "completed", false));
        var report = JsonDocument.Parse(saved.RawJson).RootElement.GetProperty("reportPath").GetString()!;
        Directory.Delete(Path.Combine(project, "Temp"), true);
        Assert.AreEqual("A", JsonDocument.Parse(history.ReadRetainedReport(report)).RootElement.GetProperty("marker").GetString());
        var other = new BridgeResultHistory(new QueuePaths(scope.Project("B"), Path.Combine(scope.Project("B"), "queue")));
        Assert.ThrowsException<InvalidOperationException>(() => other.ReadRetainedReport(Path.GetFullPath(report, project)));
    }
    [TestMethod]
    public async Task LegacyTerminalImportRetainsReportBeforeTransportCleanup()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var legacy = Path.Combine(project, "Temp", "AgentBridge", "mcp-tasks");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(project, "Temp", "report.json"), "{\"marker\":\"legacy-A\"}");
        File.SetLastWriteTimeUtc(Path.Combine(project, "Temp", "report.json"), DateTime.UtcNow.AddDays(-10));
        var now = DateTimeOffset.UtcNow;
        var result = McpToolResultConverter.Serialize(new System.Text.Json.Nodes.JsonObject { ["success"] = true, ["status"] = "completed", ["reportPath"] = "Temp/report.json" });
        var task = new McpTaskInfo("legacy-terminal", McpTaskStatus.Completed, now, now, TimeSpan.FromHours(1), 250, "legacy", result, null, null);
        File.WriteAllText(Path.Combine(legacy, McpTaskPersistence.Hash(task.TaskId) + ".json"), JsonSerializer.Serialize(task));
        var imported = await new McpTaskStore(project).GetTaskAsync(task.TaskId, default);
        var retainedPath = imported!.Result!.Value.GetProperty("structuredContent").GetProperty("reportPath").GetString()!;
        Directory.Delete(Path.Combine(project, "Temp"), true);
        var restored = await new McpTaskStore(new McpTaskRoute("A", project, Path.Combine(project, "queue"), null, null, now)).GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Completed, restored!.Status);
        var history = new BridgeResultHistory(new QueuePaths(project, Path.Combine(project, "queue")));
        Assert.AreEqual("legacy-A", JsonDocument.Parse(history.ReadRetainedReport(retainedPath)).RootElement.GetProperty("marker").GetString());
    }
    [TestMethod]
    public void LegacyUntargetedResultsPreserveTheirOriginalShape()
    {
        using var scope = new Scope();
        const string raw = "{\"success\":true,\"status\":\"success\",\"marker\":\"legacy\"}";
        var history = new BridgeResultHistory(new QueuePaths(scope.Project("A"), Path.Combine(scope.Project("A"), "queue")));
        var saved = history.Save("legacy", new(raw, "success", false));
        Assert.IsTrue(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(raw), System.Text.Json.Nodes.JsonNode.Parse(saved.RawJson)));
        var normalized = McpToolResultConverter.Serialize(System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject()).GetProperty("structuredContent");
        Assert.IsFalse(normalized.TryGetProperty("projectId", out _));
        Assert.IsFalse(normalized.TryGetProperty("projectPath", out _));
        Assert.IsFalse(normalized.TryGetProperty("dispatchCommandId", out _));
    }
    [TestMethod]
    public async Task InternalTaskPollDoesNotAllocatePublicCommandIndexOrHistory()
    {
        using var scope = new Scope();
        var paths = new QueuePaths(scope.Project("A"), Path.Combine(scope.Project("A"), "queue"), "A", "session");
        CommandStore.EnsureQueueDirectories(paths);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), JsonSerializer.Serialize(new { heartbeatUtc = DateTime.UtcNow.ToString("O"), currentStage = "unity.poller.idle" }));
        File.WriteAllText(Path.Combine(paths.OutboxDirectory, "poll.result.json"), "{\"status\":\"success\",\"success\":true}");
        var indexed = 0;
        var client = new ExternalBridgeClient { CommandDispatching = (_, _) => indexed++ };
        await client.ExecuteAsync(paths, "poll", new("unity.agent_task_control", 1000, "{\"operation\":\"query\"}"), default);
        Assert.AreEqual(0, indexed);
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.ProjectPath, "Library", "AgentBridge", "command-history")));
    }
    [TestMethod]
    public async Task InternalTaskCreateKeepsDurableTaskAndCommandOwnershipBeforeTransportWait()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = "session" }, paths, "origin");
        CommandStore.EnsureQueueDirectories(context.QueuePaths);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), JsonSerializer.Serialize(new { heartbeatUtc = DateTime.UtcNow.ToString("O"), currentStage = "unity.poller.idle" }));
        var index = new McpTaskRouteIndex(Path.Combine(scope.Root, "index"));
        var adapter = new McpTaskAdapter(new ExternalBridgeClient(), context, index);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => adapter.CreateForExternalTaskAsync("mcp-create", "compile", "{}", 1000, cancelled.Token));
        Assert.AreEqual("session", index.FindTask("mcp-create")!.EditorSessionId);
        var command = JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(paths.InboxDirectory, "*.json").Single())).RootElement.GetProperty("commandId").GetString()!;
        Assert.AreEqual("session", index.FindCommand(command)!.EditorSessionId);
    }
    [TestMethod]
    public void RetainedHistoryPreservesTimestampStringPrecisionAndZone()
    {
        using var scope = new Scope();
        const string timestamp = "2026-10-22T16:35:33.1234567Z";
        var paths = new QueuePaths(scope.Project("A"), Path.Combine(scope.Project("A"), "queue"), "A", "session");
        var history = new BridgeResultHistory(paths);
        var raw = JsonSerializer.Serialize(new { success = true, processStartTime = timestamp, metrics = new { observedAt = timestamp } });
        var saved = JsonDocument.Parse(history.Save("command", new(raw, "success", false)).RawJson);
        Assert.AreEqual(timestamp, saved.RootElement.GetProperty("processStartTime").GetString());
        var restored = JsonDocument.Parse(new BridgeResultHistory(paths).Read("command")!.RawJson);
        Assert.AreEqual(timestamp, restored.RootElement.GetProperty("processStartTime").GetString());
        Assert.AreEqual(timestamp, restored.RootElement.GetProperty("metrics").GetProperty("observedAt").GetString());
    }
    [TestMethod]
    public void SubmitHelperPreservesBusinessTimestampStrings()
    {
        using var scope = new Scope();
        const string timestamp = "2026-10-22T16:35:33.1234567Z";
        var paths = new QueuePaths(scope.Project("A"), Path.Combine(scope.Project("A"), "queue"));
        CommandStore.EnsureQueueDirectories(paths);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), JsonSerializer.Serialize(new { heartbeatUtc = DateTime.UtcNow.ToString("O"), currentStage = "unity.poller.idle" }));
        var args = JsonSerializer.Serialize(new { tool = "fixture.timestamp", args = new { processStartTime = timestamp } });
        Assert.IsTrue(new BridgeHealthClient().TryHandleLocalCommand(paths, new("unity.bridge_submit_only", 1000, args), "command", out _));
        var command = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.InboxDirectory, "command.json")));
        Assert.AreEqual(timestamp, command.RootElement.GetProperty("args").GetProperty("processStartTime").GetString());
    }
    [TestMethod]
    public async Task ExitedOriginalProcessWithSameSessionHeartbeatImmediatelyCommitsInterruption()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        const string admittedSession = "2147483647:1";
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = admittedSession }, paths, "origin");
        var adapter = new McpTaskAdapter(new ExternalBridgeClient(), context);
        var coordinator = new McpTaskExecutionCoordinator(adapter);
        var store = new McpTaskStore(context, coordinator);
        McpTaskExecutionContext.Clear();
        var task = await store.CreateTaskAsync(default);
        new McpTaskBindingStore(project).Save(new(project, task.TaskId, "original-agent", "original-command",
            ProjectId: "A", EditorSessionId: admittedSession, ConnectionId: "origin"));
        Directory.CreateDirectory(paths.StatusDirectory);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), JsonSerializer.Serialize(new {
            heartbeatUtc = DateTime.UtcNow.AddMinutes(-1).ToString("O"), editorSession = new { editorSessionId = admittedSession } }));
        var observed = await store.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Failed, observed!.Status);
        StringAssert.Contains(observed.Error!.Value.GetProperty("message").GetString(), "AGENT_TASK_INTERRUPTED");
        var record = McpTaskPersistence.Read<McpStoredTask>(new McpTaskPersistence(project).PathFor("mcp-tasks", task.TaskId))!;
        Assert.IsTrue(record.ExecutionDeadline > observed.LastUpdatedAt, "Confirmed exit must not wait for the execution deadline.");
        Assert.IsNull(record.ObservationFailedAt, "Confirmed exit must not enter inconclusive recovery.");
        Assert.IsTrue(record.LastObservation!.Value.GetProperty("originalInstanceExited").GetBoolean());
        Assert.AreEqual(admittedSession, record.LastObservation.Value.GetProperty("editorSessionId").GetString());
        Assert.AreEqual("origin", record.LastObservation.Value.GetProperty("connectionId").GetString());
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
        await store.SetCompletedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { marker = "late fabricated result" }), default);
        Assert.AreEqual(observed.Error.Value.GetRawText(), (await store.GetTaskAsync(task.TaskId, default))!.Error!.Value.GetRawText());
    }
    [TestMethod]
    public async Task InconclusiveOriginalProcessRetainsBoundedRecoveryInsteadOfInventingInterruption()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        const string unknownSession = "legacy-unconfirmed-token";
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var context = new ProjectContext(new UnityMcp.AgentBridge.ProjectRegistration { projectId = "A", projectPath = project, queueRoot = paths.QueueRoot },
            new UnityMcp.AgentBridge.EditorSessionInfo { projectId = "A", editorSessionId = unknownSession }, paths, "origin");
        var clock = new McpTaskDurabilityTests.ManualClock();
        var adapter = new McpTaskAdapter(new ExternalBridgeClient(), context);
        var coordinator = new McpTaskExecutionCoordinator(adapter);
        var policy = new McpTaskPolicy { RecoveryWindow = TimeSpan.FromSeconds(10) };
        var store = new McpTaskStore(project, coordinator, policy, clock);
        McpTaskExecutionContext.Clear();
        var task = await store.CreateTaskAsync(default);
        new McpTaskBindingStore(project).Save(new(project, task.TaskId, "original-agent", "original-command",
            ProjectId: "A", EditorSessionId: unknownSession, ConnectionId: "origin"));
        var first = await store.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Working, first!.Status);
        StringAssert.Contains(first.StatusMessage!, "AGENT_TASK_OUTCOME_UNKNOWN");
        clock.Advance(TimeSpan.FromSeconds(11));
        var expired = await store.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Failed, expired!.Status);
        StringAssert.Contains(expired.Error!.Value.GetProperty("message").GetString(), "AGENT_TASK_OUTCOME_UNKNOWN");
        Assert.IsFalse(expired.Error.Value.GetRawText().Contains("AGENT_TASK_INTERRUPTED", StringComparison.Ordinal));
        Assert.IsFalse(Directory.Exists(paths.InboxDirectory));
    }
    [TestMethod]
    public void RetainedReportsRemainReadableWithinMonoPathBudgetAndIsolatedAcrossCommandsAndSources()
    {
        using var scope = new Scope();
        // Match the real fixed-B project's normalized root length without creating a Unity project.
        const int projectLength = 113;
        var prefix = Path.Combine(scope.Root, "files");
        var project = prefix.Length < projectLength ? Path.Combine(prefix, new string('p', projectLength - prefix.Length - 1)) : prefix;
        Directory.CreateDirectory(Path.Combine(project, "Temp", "first"));
        Directory.CreateDirectory(Path.Combine(project, "Temp", "second"));
        File.WriteAllText(Path.Combine(project, "Temp", "first", "report.json"), "{\"marker\":\"first\"}");
        File.WriteAllText(Path.Combine(project, "Temp", "second", "report.json"), "{\"marker\":\"second\"}");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var history = new BridgeResultHistory(paths);
        var raw = "{\"success\":true,\"reportPath\":\"Temp/first/report.json\",\"details\":{\"reportPath\":\"Temp/second/report.json\"}}";
        var savedA = JsonDocument.Parse(history.Save("command-A", new(raw, "success", false)).RawJson);
        var savedB = JsonDocument.Parse(history.Save("command-B", new(raw, "success", false)).RawJson);
        var reportPaths = new[] {
            savedA.RootElement.GetProperty("reportPath").GetString()!,
            savedA.RootElement.GetProperty("details").GetProperty("reportPath").GetString()!,
            savedB.RootElement.GetProperty("reportPath").GetString()!,
            savedB.RootElement.GetProperty("details").GetProperty("reportPath").GetString()!
        };
        Assert.AreEqual(4, reportPaths.Distinct(StringComparer.Ordinal).Count(), "Commands and source paths must retain independent reports.");
        foreach (var path in reportPaths)
            Assert.IsTrue(Path.GetFullPath(path, project).Length < 260, "Retained report exceeds Unity Mono's legacy path budget: " + path);
        Directory.Delete(Path.Combine(project, "Temp"), true);
        var restarted = new BridgeResultHistory(paths);
        for (var index = 0; index < reportPaths.Length; index++)
        {
            var report = JsonDocument.Parse(restarted.ReadRetainedReport(reportPaths[index]));
            Assert.AreEqual(index % 2 == 0 ? "first" : "second", report.RootElement.GetProperty("marker").GetString());
        }
        Assert.AreEqual(reportPaths[0], JsonDocument.Parse(restarted.Read("command-A")!.RawJson).RootElement.GetProperty("reportPath").GetString());
    }

    [TestMethod]
    public void PreviouslyRetainedLongReportReferencesRemainImmutableAndReadableLocally()
    {
        using var scope = new Scope();
        var project = scope.Project("A");
        var paths = new QueuePaths(project, Path.Combine(project, "queue"));
        var root = Path.Combine(project, "Library", "AgentBridge", "command-history");
        Directory.CreateDirectory(root);
        var legacyReport = Path.Combine(root, new string('a', 64) + "-report-" + new string('b', 64) + ".json");
        File.WriteAllText(legacyReport, "{\"marker\":\"legacy\"}");
        var relative = Path.GetRelativePath(project, legacyReport).Replace('\\', '/');
        var record = new { version = 1, commandId = "legacy-command", projectPath = project, queueRoot = paths.QueueRoot,
            expiresAt = DateTimeOffset.UtcNow.AddDays(1), status = "success", isUnknownStatus = false,
            payload = new { success = true, reportPath = relative } };
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("legacy-command"))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(root, key + ".json"), JsonSerializer.Serialize(record));
        var history = new BridgeResultHistory(paths);
        var retained = history.Save("legacy-command", new("{\"success\":false}", "failed", false));
        Assert.AreEqual(relative, JsonDocument.Parse(retained.RawJson).RootElement.GetProperty("reportPath").GetString());
        Assert.AreEqual("legacy", JsonDocument.Parse(history.ReadRetainedReport(relative)).RootElement.GetProperty("marker").GetString());
        Assert.AreEqual(2, Directory.EnumerateFiles(root, "*.json").Count(), "Reading an old terminal must not rewrite or duplicate its report.");
    }
    [TestMethod]
    public void CorruptAndExpiredHistoryHaveDistinctDiagnostics()
    {
        using var scope = new Scope();
        var paths = new QueuePaths(scope.Project("A"), Path.Combine(scope.Project("A"), "queue"));
        var history = new BridgeResultHistory(paths);
        history.Save("command", new("{\"success\":true}", "completed", false));
        var file = Directory.EnumerateFiles(Path.Combine(paths.ProjectPath, "Library", "AgentBridge", "command-history"), "*.json").Single();
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
        json["expiresAt"] = DateTimeOffset.UtcNow.AddDays(-1);
        File.WriteAllText(file, json.ToString());
        StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => history.Read("command")).Message, "EXPIRED");
        File.WriteAllText(file, "{bad");
        StringAssert.Contains(Assert.ThrowsException<InvalidDataException>(() => history.Read("command")).Message, "CORRUPT");
    }
}
