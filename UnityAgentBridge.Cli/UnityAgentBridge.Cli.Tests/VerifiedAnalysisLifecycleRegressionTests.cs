using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class VerifiedAnalysisLifecycleRegressionTests
{
    [TestMethod]
    public async Task LostBindingRecoversOriginalDispatchWithoutRepeatingUnitySideEffects()
    {
        await using var scope = new QueueFixture();
        var id = "mcp-fault-binding";
        await scope.Adapter.CreateForExternalTaskAsync(id, "compile", "{}", 0, default);
        var persistence = new McpTaskPersistence(scope.Root);
        File.Delete(persistence.PathFor("mcp-task-mappings", scope.Root + "\n" + id));
        var recovered = new McpTaskAdapter(new ExternalBridgeClient(), scope.Paths, scope.Root);
        await recovered.CreateForExternalTaskAsync(id, "compile", "{}", 0, default);
        Assert.IsTrue(recovered.HasBinding(id));
        Assert.AreEqual(1, scope.CreateCount);
        await recovered.CreateForExternalTaskAsync(id, "compile", "{}", 0, default);
        Assert.AreEqual(1, scope.CreateCount);
    }

    [TestMethod]
    public async Task IntentBeforeEnqueueNeverAutomaticallyReplaysAnUnobservableDispatch()
    {
        await using var scope = new QueueFixture();
        var record = new McpTaskDispatch(scope.Root, "mcp-before-enqueue", "original-command", "compile", McpTaskPersistence.Hash("{}\n0"), DateTimeOffset.UtcNow);
        Assert.IsTrue(new McpTaskDispatchStore(scope.Root).TryCreate(record));
        var response = await scope.Adapter.CreateForExternalTaskAsync(record.McpTaskId, "compile", "{}", 0, default);
        Assert.AreEqual("AGENT_TASK_OUTCOME_UNKNOWN", response["errorCode"]!.GetValue<string>());
        Assert.AreEqual("original-command", response["dispatchCommandId"]!.GetValue<string>());
        Assert.AreEqual(0, scope.CreateCount);
        Assert.AreEqual(0, Directory.GetFiles(scope.Paths.InboxDirectory, "*.json").Length);
    }

    [TestMethod]
    public async Task LostCreationResponseRecoversFromOriginalOutbox()
    {
        await using var scope = new QueueFixture();
        const string id = "mcp-lost-response";
        await scope.Adapter.CreateForExternalTaskAsync(id, "compile", "{}", 0, default);
        var journal = new McpTaskDispatchStore(scope.Root);
        var dispatch = journal.Load(scope.Root, id)!;
        var persistence = new McpTaskPersistence(scope.Root);
        File.Delete(persistence.PathFor("mcp-task-mappings", scope.Root + "\n" + id));
        McpTaskPersistence.Write(persistence.PathFor("mcp-task-dispatch", scope.Root + "\n" + id), dispatch with { ResponseJson = null });
        await scope.Adapter.ReconcileAsync(id, default);
        Assert.AreEqual(dispatch.CommandId, new McpTaskBindingStore(scope.Root).Load(scope.Root, id)!.CommandId);
        Assert.AreEqual(1, scope.CreateCount);
    }

    [TestMethod]
    public async Task ObservationFailureRecoversOrEndsAtItsOwnDeadline()
    {
        await using var scope = new QueueFixture(transportFailure: true, hold: true);
        var clock = new McpTaskDurabilityTests.ManualClock();
        var coordinator = new McpTaskExecutionCoordinator(scope.Adapter);
        var store = new McpTaskStore(scope.Root, coordinator, new McpTaskPolicy(), clock);
        var task = await store.CreateTaskAsync(default);
        await coordinator.CreateAsync(task.TaskId, new McpTaskExecutionContext.PendingTaskRequest("unity_project_compile", "{}"), default);
        var unavailable = await store.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Working, unavailable!.Status);
        Assert.IsNull(unavailable.Result);
        scope.TransportFailure = false;
        Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(task.TaskId, default))!.Status);
        scope.TransportFailure = true;
        await store.GetTaskAsync(task.TaskId, default);
        clock.Advance(TimeSpan.FromMinutes(6));
        await store.MaintainAsync(default);
        var terminal = await store.GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(McpTaskStatus.Failed, terminal!.Status);
        StringAssert.Contains(terminal.Error!.Value.GetProperty("message").GetString(), "AGENT_TASK_OUTCOME_UNKNOWN");
        Assert.IsTrue(terminal.Error.Value.GetProperty("data").GetProperty("lastObservation").GetProperty("observationFailure").GetBoolean());
        Assert.AreEqual(1, scope.CreateCount);
    }

    [TestMethod]
    public async Task CallerWaitDeadlineLeavesTheOperationAndBindingActive()
    {
        await using var scope = new QueueFixture(hold: true);
        var created = await scope.Adapter.CreateAsync("compile", "{}", default);
        var id = created["taskId"]!.GetValue<string>();
        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { await scope.Adapter.WaitForTerminalAsync(id, wait.Token); Assert.Fail("Expected a cancelled wait."); }
        catch (OperationCanceledException) { }
        Assert.IsTrue(scope.Adapter.HasBinding(id));
        Assert.AreEqual("working", (await scope.Adapter.GetAsync(id, default))["status"]!.GetValue<string>());
        Assert.AreEqual(0, scope.CancelCount);
    }

    [TestMethod]
    public async Task RestartBeforeTerminalCommitReconstructsTheStandardResult()
    {
        await using var scope = new QueueFixture();
        var coordinator = new McpTaskExecutionCoordinator(scope.Adapter);
        var first = new McpTaskStore(scope.Root, coordinator);
        var task = await first.CreateTaskAsync(default);
        await coordinator.CreateAsync(task.TaskId, new McpTaskExecutionContext.PendingTaskRequest("unity_project_compile", "{}"), default);
        var restarted = new McpTaskStore(scope.Root, new McpTaskExecutionCoordinator(new McpTaskAdapter(new ExternalBridgeClient(), scope.Paths, scope.Root)));
        McpTaskInfo? result;
        do { result = await restarted.GetTaskAsync(task.TaskId, default); } while (result!.Status == McpTaskStatus.Working);
        Assert.AreEqual(McpTaskStatus.Completed, result.Status);
        Assert.IsFalse(result.Result!.Value.GetProperty("isError").GetBoolean());
        Assert.IsNotNull(result.Result.Value.GetProperty("content"));
        Assert.AreEqual(1, scope.CreateCount);
    }

    [TestMethod]
    public async Task PinnedSdkClientReadsStandardResultAndRetainsItAfterHostRestart()
    {
        await using var scope = new QueueFixture();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        string taskId;
        string terminalJson;
        await using (var client = await StartClientAsync(scope.Root, token))
        {
            var created = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = "unity_project_compile" }, token);
            Assert.IsTrue(created.IsTask, "Protocol=" + client.NegotiatedProtocolVersion + "; result=" + JsonSerializer.Serialize(created.Result));
            taskId = created.TaskCreated!.TaskId;
            GetTaskResult terminal;
            do
            {
                await Task.Delay(100, token);
                terminal = await client.GetTaskAsync(taskId, token);
            } while (terminal is WorkingTaskResult);
            Assert.IsInstanceOfType<CompletedTaskResult>(terminal);
            terminalJson = ((CompletedTaskResult)terminal).Result.GetRawText();
            var result = JsonSerializer.Deserialize<CallToolResult>(terminalJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsError);
            Assert.IsTrue(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        }
        // Simulate loss of bridge observation while preserving the project and record.
        File.Delete(Path.Combine(scope.Paths.StatusDirectory, "unity_bridge_status.json"));
        await using var restarted = await StartClientAsync(scope.Root, token);
        var queries = scope.QueryCount;
        for (var i = 0; i < 3; i++)
        {
            var retained = await restarted.GetTaskAsync(taskId, token);
            Assert.IsInstanceOfType<CompletedTaskResult>(retained);
            Assert.AreEqual(terminalJson, ((CompletedTaskResult)retained).Result.GetRawText());
        }
        Assert.AreEqual(queries, scope.QueryCount);
    }

    [DataTestMethod]
    [DataRow("unity_project_compile")]
    [DataRow("unity_tests_run_edit_mode")]
    [DataRow("unity_tests_run_play_mode")]
    public async Task PinnedSdkUnsupportedCancellationLeavesCompilationObservable(string toolName)
    {
        await using var scope = new QueueFixture(hold: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        await using var client = await StartClientAsync(scope.Root, token);
        var created = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = toolName }, token);
        Assert.IsTrue(created.IsTask, "Protocol=" + client.NegotiatedProtocolVersion + "; result=" + JsonSerializer.Serialize(created.Result));
        while (!scope.Adapter.HasBinding(created.TaskCreated!.TaskId)) await Task.Delay(10, token);
        var exception = await Assert.ThrowsExceptionAsync<McpProtocolException>(async () => await client.CancelTaskAsync(created.TaskCreated!.TaskId, token));
        StringAssert.Contains(exception.Message, "AGENT_TASK_CANCEL_UNSUPPORTED");
        Assert.IsInstanceOfType<WorkingTaskResult>(await client.GetTaskAsync(created.TaskCreated!.TaskId, token));
        scope.Hold = false;
        GetTaskResult terminal;
        do
        {
            await Task.Delay(100, token);
            terminal = await client.GetTaskAsync(created.TaskCreated!.TaskId, token);
        } while (terminal is WorkingTaskResult);
        Assert.IsInstanceOfType<CompletedTaskResult>(terminal);
        Assert.IsFalse(((CompletedTaskResult)terminal).Result.GetProperty("isError").GetBoolean());
    }

    [TestMethod]
    public async Task PinnedSdkAcceptedCancellationIsReconciledAfterItsWaitIsCancelled()
    {
        await using var scope = new QueueFixture(hold: true) { SupportsCancellation = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        await using var client = await StartClientAsync(scope.Root, token);
        var created = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = "unity_project_compile" }, token);
        var id = created.TaskCreated!.TaskId;
        while (!scope.Adapter.HasBinding(id)) await Task.Delay(10, token);
        await client.CancelTaskAsync(id, token);
        GetTaskResult terminal;
        do
        {
            await Task.Delay(50, token);
            terminal = await client.GetTaskAsync(id, token);
        } while (terminal is WorkingTaskResult);
        Assert.IsInstanceOfType<CancelledTaskResult>(terminal);
        Assert.AreEqual(1, scope.CancelCount);
        await client.CancelTaskAsync(id, token);
        Assert.AreEqual(1, scope.CancelCount, "A terminal cancellation must not dispatch again.");
    }

    [TestMethod]
    public async Task PinnedSdkInvalidArgumentsAreRejectedBeforeTaskAllocationAndDispatch()
    {
        await using var scope = new QueueFixture();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await StartClientAsync(scope.Root, timeout.Token);
        var result = await client.CallToolAsTaskAsync(new CallToolRequestParams
        {
            Name = "unity_project_compile", Arguments = new Dictionary<string, JsonElement> { ["unexpected"] = JsonSerializer.SerializeToElement(true) }
        }, timeout.Token);
        Assert.IsFalse(result.IsTask);
        Assert.IsTrue(result.Result!.IsError);
        Assert.AreEqual(0, scope.CreateCount);
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(scope.Root, "Library", "AgentBridge", "mcp-tasks"), "*.json").Length);
    }

    private static Task<McpClient> StartClientAsync(string root, CancellationToken token)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assembly = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "UnityAgentBridge.Cli", "bin", configuration, "net8.0", "win-x64", "UnityAgentBridge.Cli.dll"));
        return McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [assembly, "mcp-server"],
            EnvironmentVariables = new Dictionary<string, string?> { ["UNITY_AGENT_BRIDGE_PROJECT_PATH"] = root }
        }), new McpClientOptions { ProtocolVersion = "2026-07-28" }, cancellationToken: token);
    }

    [TestMethod]
    public async Task ProjectedCreationRetainsSdkTaskBinding()
    {
        await using var scope = new QueueFixture();
        var adapter = scope.Adapter;
        McpTaskExecutionContext.Prepare("unity_analysis_compile_failure", "{\"problemId\":\"p\",\"evidenceIds\":[\"e\"]}");
        try
        {
            var coordinator = new McpTaskExecutionCoordinator(adapter);
            var store = new McpTaskStore(scope.Root, coordinator);
            var task = await store.CreateTaskAsync(CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Working, task.Status);
            Assert.IsNull(task.Error);
            Assert.IsFalse(adapter.HasBinding(task.TaskId), "Allocation must not dispatch Unity work.");
            Assert.IsNull(scope.CreatePayload);
            await coordinator.CreateAsync(task.TaskId, McpTaskExecutionContext.ConsumePending()!, CancellationToken.None);
            Assert.IsTrue(adapter.HasBinding(task.TaskId));
        }
        finally { McpTaskExecutionContext.Clear(); }
    }

    [TestMethod]
    public async Task WorkingFalseSuccessRemainsPendingUntilCompleted()
    {
        await using var scope = new QueueFixture();
        var created = await scope.Adapter.CreateAsync("analysis_compile_failure", "{}", CancellationToken.None);
        var id = created["taskId"]!.GetValue<string>();
        var terminal = await scope.Adapter.WaitForTerminalAsync(id, CancellationToken.None);
        Assert.AreEqual("completed", terminal["status"]!.GetValue<string>());
        Assert.IsTrue(terminal["success"]!.GetValue<bool>());
        Assert.AreEqual(4, scope.QueryCount);
    }

    [TestMethod]
    public async Task SynchronousFixedToolUsesStringPayloadAndWaitsForTerminal()
    {
        await using var scope = new QueueFixture();
        var diagnostics = new McpHostDiagnostics("fixture", "fixture", [], scope.Root, scope.Paths.QueueRoot, scope.Paths.LogsDirectory, Path.Combine(scope.Root, "mcp.log"));
        McpToolRuntimeContext.QueuePaths = scope.Paths;
        try
        {
            var tool = McpToolCatalog.TryGet("unity_analysis_compile_failure", diagnostics)!;
            var json = await tool.InvokeAsync("{\"problemId\":\"p\",\"evidenceIds\":[\"e\"]}", CancellationToken.None);
            Assert.IsNotNull(scope.CreatePayload);
            Assert.AreEqual(JsonValueKind.String, scope.CreatePayload!.GetValueKind());
            Assert.AreEqual("p", JsonNode.Parse(scope.CreatePayload.GetValue<string>())!["problemId"]!.GetValue<string>());
            Assert.AreEqual("completed", JsonNode.Parse(json)!["status"]!.GetValue<string>());
            Assert.AreEqual(4, scope.QueryCount);
        }
        finally { McpToolRuntimeContext.QueuePaths = null; }
    }

    [TestMethod]
    public async Task ImmediatelyRejectedNativeTaskStillHasQueryableBinding()
    {
        await using var scope = new QueueFixture(failed: true);
        McpTaskExecutionContext.Prepare("unity_analysis_compile_failure", "{}");
        try
        {
            var coordinator = new McpTaskExecutionCoordinator(scope.Adapter);
            var store = new McpTaskStore(scope.Root, coordinator);
            var created = await store.CreateTaskAsync(CancellationToken.None);
            await coordinator.CreateAsync(created.TaskId, McpTaskExecutionContext.ConsumePending()!, CancellationToken.None);
            Assert.IsTrue(scope.Adapter.HasBinding(created.TaskId));
            var observed = await store.GetTaskAsync(created.TaskId, CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Completed, observed!.Status);
            Assert.IsTrue(observed.Result!.Value.GetProperty("isError").GetBoolean());
            Assert.AreEqual("FORBIDDEN_OVERRIDE", observed.Result!.Value.GetProperty("structuredContent").GetProperty("errors")[0].GetProperty("code").GetString());
        }
        finally { McpTaskExecutionContext.Clear(); }
    }

    [TestMethod]
    public async Task ExplicitTransportFailureDoesNotAppearWorking()
    {
        await using var scope = new QueueFixture(transportFailure: true);
        var created = await scope.Adapter.CreateAsync("analysis_compile_failure", "{}", CancellationToken.None);
        var result = await scope.Adapter.WaitForTerminalAsync(created["taskId"]!.GetValue<string>(), CancellationToken.None);
        Assert.AreEqual("observation_failed", result["status"]!.GetValue<string>());
        Assert.IsTrue(result["observationFailure"]!.GetValue<bool>());
        Assert.IsFalse(result["success"]!.GetValue<bool>());
        Assert.AreEqual("CLI_BRIDGE_LIFECYCLE_BLOCKED", result["errors"]![0]!["code"]!.GetValue<string>());
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        private readonly Task worker;
        private readonly bool failed;
        public bool TransportFailure { get; set; }
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VerifiedAnalysisLifecycle", Guid.NewGuid().ToString("N"));
        public QueuePaths Paths { get; }
        public McpTaskAdapter Adapter { get; }
        public int QueryCount { get; private set; }
        public int CreateCount { get; private set; }
        public int CancelCount { get; private set; }
        public JsonNode? CreatePayload { get; private set; }
        public bool Hold { get; set; }
        public bool SupportsCancellation { get; set; }

        public QueueFixture(bool failed = false, bool transportFailure = false, bool hold = false)
        {
            Hold = hold;
            this.failed = failed;
            TransportFailure = transportFailure;
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.53f1");
            Paths = new QueuePaths(Root, Path.Combine(Root, "Temp", "AgentBridge"));
            CommandStore.EnsureQueueDirectories(Paths);
            File.WriteAllText(Path.Combine(Paths.StatusDirectory, "unity_bridge_status.json"), new JsonObject { ["heartbeatUtc"] = DateTime.UtcNow.ToString("O"), ["projectPath"] = Root, ["currentStage"] = "idle" }.ToJsonString());
            VerifiedAnalysisCatalogFixture.Install(Root);
            var catalogPath = Path.Combine(Root, "Library", "AgentBridge", "plugin-catalog.json");
            var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
            foreach (var mode in new[] { "edit", "play" })
                catalog["tools"]!.AsArray().Add(new JsonObject
                {
                    ["pluginId"] = "com.unitymcp.agent-bridge.tests", ["pluginVersion"] = "1.4.0",
                    ["assemblyName"] = "UnityMcp.BuiltInPlugins.TestRunner", ["allowedRuntimeModes"] = "Edit",
                    ["sideEffect"] = "RunsUserCode", ["mayTriggerDomainReload"] = true,
                    ["bridgeTool"] = "unity.run_" + mode + "mode_tests", ["mcpName"] = "unity_tests_run_" + mode + "_mode",
                    ["title"] = "Test Runner", ["description"] = "Native test task fixture", ["defaultTimeoutMs"] = 120000,
                    ["inputSchemaJson"] = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
                });
            File.WriteAllText(catalogPath, catalog.ToJsonString());
            Adapter = new McpTaskAdapter(new ExternalBridgeClient(), Paths, Root);
            worker = PumpAsync();
        }

        private async Task PumpAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                foreach (var path in Directory.EnumerateFiles(Paths.InboxDirectory, "*.json"))
                {
                    JsonNode command;
                    try
                    {
                        command = JsonNode.Parse(File.ReadAllText(path))!;
                        File.Delete(path);
                    }
                    catch (Exception exception) when (exception is IOException or JsonException)
                    {
                        // The simulated queue can see a command while its writer
                        // still owns it. Retry before counting any native effect.
                        continue;
                    }
                    var args = command["args"]!.AsObject();
                    var operation = args["operation"]!.GetValue<string>();
                    if (operation == "create") { CreatePayload = args["payload"]?.DeepClone(); CreateCount++; }
                    else QueryCount++;
                    if (operation == "cancel") CancelCount++;
                    var completed = operation != "create" && QueryCount >= 4 && !Hold;
                    var projection = new JsonObject {
                        ["success"] = !failed, ["agentTaskId"] = "analysis-fixture",
                        ["state"] = SupportsCancellation && CancelCount > 0 ? "Cancelled" : failed ? "Failed" : completed ? "Completed" : "Running",
                        ["resultKind"] = completed ? "Succeeded" : null,
                        ["errorCode"] = failed ? "FORBIDDEN_OVERRIDE" : null,
                        ["errorMessage"] = failed ? "Fixture rejection" : null,
                        ["cancellation"] = operation == "cancel" ? SupportsCancellation ? "Accepted" : "Unsupported" : null,
                        ["resultPayloadJson"] = completed ? "{\"fixture\":true}" : null
                    };
                    var response = new JsonObject { ["schemaVersion"] = "1.0", ["commandId"] = command["commandId"]!.GetValue<string>(),
                        ["tool"] = "unity.agent_task_control", ["success"] = !failed, ["status"] = failed ? "failed" : "success", ["metricsObjectJson"] = projection.ToJsonString() };
                    if (operation != "create" && TransportFailure)
                    {
                        response.Remove("metricsObjectJson");
                        response["success"] = false;
                        response["status"] = "blocked";
                        response["errors"] = new JsonArray(new JsonObject { ["code"] = "CLI_BRIDGE_LIFECYCLE_BLOCKED" });
                    }
                    var resultPath = Path.Combine(Paths.OutboxDirectory, response["commandId"]!.GetValue<string>() + ".result.json");
                    var temporary = resultPath + ".tmp";
                    File.WriteAllText(temporary, response.ToJsonString());
                    File.Move(temporary, resultPath);
                }
                await Task.Delay(10, cancellation.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            try { await worker; } catch (OperationCanceledException) { }
            cancellation.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
