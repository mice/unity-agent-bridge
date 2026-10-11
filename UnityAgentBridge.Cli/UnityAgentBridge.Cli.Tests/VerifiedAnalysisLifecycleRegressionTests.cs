using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class VerifiedAnalysisLifecycleRegressionTests
{
    [TestMethod]
    public async Task ProjectedCreationRetainsSdkTaskBinding()
    {
        await using var scope = new QueueFixture();
        var adapter = scope.Adapter;
        McpTaskExecutionContext.Prepare("unity_analysis_compile_failure", "{\"problemId\":\"p\",\"evidenceIds\":[\"e\"]}");
        try
        {
            var store = new McpTaskStore(scope.Root, new McpTaskExecutionCoordinator(adapter));
            var task = await store.CreateTaskAsync(CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Working, task.Status);
            Assert.IsNull(task.Error);
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
        Assert.AreEqual(2, scope.QueryCount);
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
            Assert.AreEqual(2, scope.QueryCount);
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
            var store = new McpTaskStore(scope.Root, new McpTaskExecutionCoordinator(scope.Adapter));
            var created = await store.CreateTaskAsync(CancellationToken.None);
            Assert.IsTrue(scope.Adapter.HasBinding(created.TaskId));
            var observed = await store.GetTaskAsync(created.TaskId, CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Failed, observed!.Status);
            Assert.AreEqual("FORBIDDEN_OVERRIDE", observed.Error!.Value.GetProperty("errorCode").GetString());
        }
        finally { McpTaskExecutionContext.Clear(); }
    }

    [TestMethod]
    public async Task ExplicitTransportFailureDoesNotAppearWorking()
    {
        await using var scope = new QueueFixture(transportFailure: true);
        var created = await scope.Adapter.CreateAsync("analysis_compile_failure", "{}", CancellationToken.None);
        var result = await scope.Adapter.WaitForTerminalAsync(created["taskId"]!.GetValue<string>(), CancellationToken.None);
        Assert.AreEqual("failed", result["status"]!.GetValue<string>());
        Assert.IsFalse(result["success"]!.GetValue<bool>());
        Assert.AreEqual("CLI_BRIDGE_LIFECYCLE_BLOCKED", result["errors"]![0]!["code"]!.GetValue<string>());
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        private readonly Task worker;
        private readonly bool failed;
        private readonly bool transportFailure;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VerifiedAnalysisLifecycle", Guid.NewGuid().ToString("N"));
        public QueuePaths Paths { get; }
        public McpTaskAdapter Adapter { get; }
        public int QueryCount { get; private set; }
        public JsonNode? CreatePayload { get; private set; }

        public QueueFixture(bool failed = false, bool transportFailure = false)
        {
            this.failed = failed;
            this.transportFailure = transportFailure;
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.53f1");
            Paths = new QueuePaths(Root, Path.Combine(Root, "Temp", "AgentBridge"));
            CommandStore.EnsureQueueDirectories(Paths);
            File.WriteAllText(Path.Combine(Paths.StatusDirectory, "unity_bridge_status.json"), new JsonObject { ["heartbeatUtc"] = DateTime.UtcNow.ToString("O"), ["projectPath"] = Root, ["currentStage"] = "idle" }.ToJsonString());
            VerifiedAnalysisCatalogFixture.Install(Root);
            Adapter = new McpTaskAdapter(new ExternalBridgeClient(), Paths, Root);
            worker = PumpAsync();
        }

        private async Task PumpAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                foreach (var path in Directory.EnumerateFiles(Paths.InboxDirectory, "*.json"))
                {
                    var command = JsonNode.Parse(File.ReadAllText(path))!;
                    var args = command["args"]!.AsObject();
                    var operation = args["operation"]!.GetValue<string>();
                    if (operation == "create") CreatePayload = args["payload"]?.DeepClone();
                    else QueryCount++;
                    var completed = operation != "create" && QueryCount >= 2;
                    var projection = new JsonObject {
                        ["success"] = !failed, ["agentTaskId"] = "analysis-fixture",
                        ["state"] = failed ? "Failed" : completed ? "Completed" : "Running",
                        ["resultKind"] = completed ? "Succeeded" : null,
                        ["errorCode"] = failed ? "FORBIDDEN_OVERRIDE" : null,
                        ["errorMessage"] = failed ? "Fixture rejection" : null,
                        ["resultPayloadJson"] = completed ? "{\"fixture\":true}" : null
                    };
                    var response = new JsonObject { ["schemaVersion"] = "1.0", ["commandId"] = command["commandId"]!.GetValue<string>(),
                        ["tool"] = "unity.agent_task_control", ["success"] = !failed, ["status"] = failed ? "failed" : "success", ["metricsObjectJson"] = projection.ToJsonString() };
                    if (operation != "create" && transportFailure)
                    {
                        response.Remove("metricsObjectJson");
                        response["success"] = false;
                        response["status"] = "blocked";
                        response["errors"] = new JsonArray(new JsonObject { ["code"] = "CLI_BRIDGE_LIFECYCLE_BLOCKED" });
                    }
                    File.Delete(path);
                    File.WriteAllText(Path.Combine(Paths.OutboxDirectory, response["commandId"]!.GetValue<string>() + ".result.json"), response.ToJsonString());
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
