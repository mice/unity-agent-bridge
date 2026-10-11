using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpTaskResultContractTests
{
    [TestMethod]
    public void StageLogSharingViolationDoesNotFailExecutionAndLoggingResumes()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mcp.log");
            var logger = new McpStageLogger(path);
            using (var locked = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                logger.Write("mcp.received", "locked", "mcp_echo", "ok", "request");
            logger.Write("mcp.return_response", "available", "mcp_echo", "ok", "response");
            using var result = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual("available", result.RootElement.GetProperty("commandId").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SuccessfulTaskProjectionIsNotAToolError()
    {
        var result = McpServerService.AdaptResult("""{"taskId":"mcp-1","state":"Completed","status":"completed","success":true,"resultKind":"Succeeded"}""", Diagnostics());
        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void CompileErrorsAreDirectlyAvailableInTheStandardResult()
    {
        var result = McpServerService.AdaptResult("""
            {"taskId":"mcp-1","state":"Completed","status":"completed","success":false,"resultKind":"DomainFailure",
             "metrics":{"success":false,"status":"failed","errors":[{"code":"CS1002","message":"; expected","file":"Assets/Broken.cs","line":4,"column":9}],
                        "metricsObjectJson":"{\"errorCount\":1,\"warningCount\":0,\"targetEpoch\":12}"}}
            """, Diagnostics());
        Assert.IsTrue(result.IsError);
        var payload = result.StructuredContent!.Value;
        Assert.AreEqual("CS1002", payload.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.AreEqual(1, payload.GetProperty("metrics").GetProperty("errorCount").GetInt32());
        Assert.AreEqual("mcp-1", payload.GetProperty("taskId").GetString());
    }

    [TestMethod]
    public async Task LateFailureCannotReplaceCompletedResult()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new McpTaskStore(root);
            var task = await store.CreateTaskAsync(CancellationToken.None);
            var original = JsonSerializer.SerializeToElement(new { content = new[] { new { type = "text", text = "original" } }, isError = false });
            await store.SetCompletedAsync(task.TaskId, original, CancellationToken.None);
            await store.SetFailedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { code = -32603, message = "late" }), CancellationToken.None);
            var observed = await store.GetTaskAsync(task.TaskId, CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Completed, observed!.Status);
            Assert.AreEqual(original.GetRawText(), observed.Result!.Value.GetRawText());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task LateCompletionAndCancellationCannotReplaceProtocolFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new McpTaskStore(root);
            var task = await store.CreateTaskAsync(CancellationToken.None);
            var error = JsonSerializer.SerializeToElement(new { code = -32603, message = "original" });
            await store.SetFailedAsync(task.TaskId, error, CancellationToken.None);
            await store.SetCompletedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { isError = false }), CancellationToken.None);
            Assert.IsFalse(await store.SetCancelledAsync(task.TaskId, CancellationToken.None));
            var observed = await new McpTaskStore(root).GetTaskAsync(task.TaskId, CancellationToken.None);
            Assert.AreEqual(McpTaskStatus.Failed, observed!.Status);
            Assert.IsNull(observed.Result);
            Assert.AreEqual(error.GetRawText(), observed.Error!.Value.GetRawText());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ConcurrentTerminalWritersCommitOneCompletePayload()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new McpTaskStore(root);
            for (var iteration = 0; iteration < 30; iteration++)
            {
                var task = await store.CreateTaskAsync(CancellationToken.None);
                var result = JsonSerializer.SerializeToElement(new { content = new[] { new { type = "text", text = "complete" } }, isError = false });
                var error = JsonSerializer.SerializeToElement(new { code = -32603, message = "failure" });
                await Task.WhenAll(
                    Task.Run(() => store.SetCompletedAsync(task.TaskId, result, CancellationToken.None)),
                    Task.Run(() => store.SetFailedAsync(task.TaskId, error, CancellationToken.None)));
                var observed = await store.GetTaskAsync(task.TaskId, CancellationToken.None);
                if (observed!.Status == McpTaskStatus.Completed)
                {
                    Assert.IsNull(observed.Error);
                    Assert.AreEqual(result.GetRawText(), observed.Result!.Value.GetRawText());
                }
                else
                {
                    Assert.AreEqual(McpTaskStatus.Failed, observed.Status);
                    Assert.IsNull(observed.Result);
                    Assert.AreEqual(error.GetRawText(), observed.Error!.Value.GetRawText());
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task InvalidCompileTaskDoesNotDispatchBeforeOrAfterValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new QueuePaths(root, Path.Combine(root, "Temp", "AgentBridge"));
            CommandStore.EnsureQueueDirectories(paths);
            var client = new ExternalBridgeClient();
            var adapter = new McpTaskAdapter(client, paths, root);
            var coordinator = new McpTaskExecutionCoordinator(adapter);
            var store = new McpTaskStore(root, coordinator);
            var diagnostics = new McpHostDiagnostics("fixture", "fixture", [], root, paths.QueueRoot, paths.LogsDirectory, Path.Combine(root, "mcp.log"));
            var service = new McpServerService(client, diagnostics, new McpStageLogger(diagnostics.ServerLogPath), adapter, coordinator);
            McpTaskExecutionContext.Prepare("unity_project_compile", "{\"unexpected\":true}");
            var task = await store.CreateTaskAsync(CancellationToken.None);
            Assert.AreEqual(task.TaskId, McpTaskExecutionContext.CurrentTaskId);
            Assert.IsFalse(adapter.HasBinding(task.TaskId));
            var result = await service.CallToolAsync(new CallToolRequestParams
            {
                Name = "unity_project_compile",
                Arguments = new Dictionary<string, JsonElement> { ["unexpected"] = JsonSerializer.SerializeToElement(true) }
            }, CancellationToken.None);
            Assert.IsTrue(result.IsError);
            Assert.IsFalse(adapter.HasBinding(task.TaskId));
            Assert.AreEqual(0, Directory.GetFiles(paths.InboxDirectory, "*.json").Length);
            Assert.AreEqual(0, Directory.GetFiles(paths.OutboxDirectory, "*.json").Length);
        }
        finally
        {
            McpTaskExecutionContext.Clear();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task UnsupportedInputDoesNotLeaveAnInputRequiredTask()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(McpTaskResultContractTests), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new McpTaskStore(root);
            var task = await store.CreateTaskAsync(CancellationToken.None);
            await Assert.ThrowsExceptionAsync<NotSupportedException>(() => store.SetInputRequestsAsync(task.TaskId, new Dictionary<string, InputRequest>(), CancellationToken.None));
            await Assert.ThrowsExceptionAsync<NotSupportedException>(() => store.ResolveInputRequestsAsync(task.TaskId, new Dictionary<string, InputResponse>(), CancellationToken.None));
            Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(task.TaskId, CancellationToken.None))!.Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static McpHostDiagnostics Diagnostics() => new("fixture", "fixture", [], "fixture", "fixture", "fixture", "fixture");
}
