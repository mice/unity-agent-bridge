using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Tasks;
using System.Text.Json;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpTaskDurabilityTests
{
    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class Scope : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), nameof(McpTaskDurabilityTests), Guid.NewGuid().ToString("N"));
        internal ManualClock Clock { get; } = new();
        internal McpTaskStore Store(McpTaskPolicy? policy = null) => new(Root, null, policy ?? new(), Clock);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [TestMethod]
    public async Task TerminalResultSurvivesRemovalOfTransportTemp()
    {
        using var scope = new Scope();
        var store = scope.Store();
        var task = await store.CreateTaskAsync(default);
        var result = JsonSerializer.SerializeToElement(new { content = new[] { new { type = "text", text = "retained" } }, isError = false });
        await store.SetCompletedAsync(task.TaskId, result, default);
        var transport = Path.Combine(scope.Root, "Temp", "AgentBridge");
        Directory.CreateDirectory(transport);
        Directory.Delete(transport, true);
        var recovered = await scope.Store().GetTaskAsync(task.TaskId, default);
        Assert.AreEqual(result.GetRawText(), recovered!.Result!.Value.GetRawText());
        Assert.AreEqual(McpTaskStatus.Completed, recovered.Status);
    }

    [TestMethod]
    public async Task SeparateStoreInstancesCannotReplaceATerminalCommit()
    {
        using var scope = new Scope();
        var first = scope.Store();
        var second = scope.Store();
        for (var i = 0; i < 20; i++)
        {
            var task = await first.CreateTaskAsync(default);
            await Task.WhenAll(
                Task.Run(() => first.SetCompletedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { marker = "completed" }), default)),
                Task.Run(() => second.SetFailedAsync(task.TaskId, JsonSerializer.SerializeToElement(new { code = -32603, message = "failed" }), default)));
            var saved = await first.GetTaskAsync(task.TaskId, default);
            var same = await second.GetTaskAsync(task.TaskId, default);
            Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(same));
            Assert.AreNotEqual(saved!.Result.HasValue, saved.Error.HasValue);
        }
    }

    [TestMethod]
    public async Task LegacyImportIsIdempotentAndExpiredRecordsDoNotResurrect()
    {
        using var scope = new Scope();
        var directory = Path.Combine(scope.Root, "Temp", "AgentBridge", "mcp-tasks");
        Directory.CreateDirectory(directory);
        var now = scope.Clock.GetUtcNow();
        var task = new McpTaskInfo("mcp-legacy", McpTaskStatus.Completed, now, now, TimeSpan.FromMinutes(1), 250,
            "legacy", JsonSerializer.SerializeToElement(new { content = Array.Empty<object>(), isError = false }), null, null);
        File.WriteAllText(Path.Combine(directory, McpTaskPersistence.Hash(task.TaskId) + ".json"), JsonSerializer.Serialize(task));
        Assert.AreEqual(McpTaskStatus.Completed, (await scope.Store().GetTaskAsync(task.TaskId, default))!.Status);
        scope.Clock.Advance(TimeSpan.FromMinutes(2));
        await scope.Store().MaintainAsync(default);
        Assert.IsNull(await scope.Store().GetTaskAsync(task.TaskId, default));
    }

    [TestMethod]
    public async Task CorruptAndIncompatibleRecordsFailExplicitly()
    {
        using var scope = new Scope();
        var store = scope.Store();
        var task = await store.CreateTaskAsync(default);
        var path = new McpTaskPersistence(scope.Root).PathFor("mcp-tasks", task.TaskId);
        var good = File.ReadAllText(path);
        File.WriteAllText(path, "{partial");
        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidDataException>(() => store.GetTaskAsync(task.TaskId, default))).Message, "CORRUPT");
        File.WriteAllText(path, good.Replace("\"Version\":2", "\"Version\":999"));
        StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidDataException>(() => store.GetTaskAsync(task.TaskId, default))).Message, "INCOMPATIBLE");
    }

    [TestMethod]
    public async Task CountCleanupPreservesActiveTrackingAndReclaimsOldestTerminal()
    {
        using var scope = new Scope();
        var store = scope.Store(new() { MaxTerminalRecords = 1 });
        var active = await store.CreateTaskAsync(default);
        var first = await store.CreateTaskAsync(default);
        await store.SetCompletedAsync(first.TaskId, JsonSerializer.SerializeToElement(new { ok = true }), default);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await store.CreateTaskAsync(default);
        await store.SetCompletedAsync(second.TaskId, JsonSerializer.SerializeToElement(new { ok = true }), default);
        store.Cleanup();
        Assert.IsNull(await store.GetTaskAsync(first.TaskId, default));
        Assert.AreEqual(McpTaskStatus.Completed, (await store.GetTaskAsync(second.TaskId, default))!.Status);
        Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(active.TaskId, default))!.Status);
    }

    [TestMethod]
    public async Task StartupMaintenanceClosesExpiredActiveTasksWithoutClientPolling()
    {
        using var scope = new Scope();
        var policy = new McpTaskPolicy { DefaultExecutionWindow = TimeSpan.FromSeconds(1) };
        var task = await scope.Store(policy).CreateTaskAsync(default);
        scope.Clock.Advance(TimeSpan.FromSeconds(2));
        var restarted = scope.Store(policy);
        await restarted.MaintainAsync(default);
        var record = McpTaskPersistence.Read<McpStoredTask>(new McpTaskPersistence(scope.Root).PathFor("mcp-tasks", task.TaskId));
        Assert.AreEqual(McpTaskStatus.Failed, record!.Task.Status);
        StringAssert.Contains(record.Task.Error!.Value.GetProperty("message").GetString(), "AGENT_TASK_EXECUTION_DEADLINE");
    }

    [TestMethod]
    public async Task AdmissionLimitPreservesExistingTasks()
    {
        using var scope = new Scope();
        var store = scope.Store(new() { MaxActiveRecords = 1 });
        var first = await store.CreateTaskAsync(default);
        await Assert.ThrowsExceptionAsync<McpProtocolException>(() => store.CreateTaskAsync(default));
        Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(first.TaskId, default))!.Status);
    }

    [TestMethod]
    public async Task OwnedReportSurvivesTransportCleanupAndExpiresWithoutDeletingOriginal()
    {
        using var scope = new Scope();
        var store = scope.Store();
        var task = await store.CreateTaskAsync(default);
        var original = Path.Combine(scope.Root, "shared-report.json");
        File.WriteAllText(original, "{\"diagnostics\":[\"fixture\"]}");
        var result = JsonSerializer.SerializeToElement(new { content = Array.Empty<object>(), isError = true,
            structuredContent = new { success = false, reportPath = original, metrics = new { details = new { reportPath = original } } } });
        await store.SetCompletedAsync(task.TaskId, result, default);
        var saved = await store.GetTaskAsync(task.TaskId, default);
        var payload = saved!.Result!.Value.GetProperty("structuredContent");
        var relativeOwned = payload.GetProperty("reportPath").GetString()!;
        var owned = Path.Combine(scope.Root, relativeOwned);
        Assert.AreNotEqual(original, owned);
        Assert.AreEqual(File.ReadAllText(original), File.ReadAllText(owned));
        Assert.AreEqual(relativeOwned, payload.GetProperty("metrics").GetProperty("details").GetProperty("reportPath").GetString());
        scope.Clock.Advance(TimeSpan.FromDays(8));
        await store.MaintainAsync(default);
        Assert.IsFalse(File.Exists(owned));
        Assert.IsTrue(File.Exists(original));
        Assert.IsNull(await store.GetTaskAsync(task.TaskId, default));
    }

    [TestMethod]
    public async Task OversizedRecordCannotReplaceThePreviousCompleteSnapshot()
    {
        using var scope = new Scope();
        var store = scope.Store();
        var task = await store.CreateTaskAsync(default);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => store.SetCompletedAsync(task.TaskId,
            JsonSerializer.SerializeToElement(new { text = new string('x', (int)McpTaskPersistence.MaxRecordBytes + 1) }), default));
        Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(task.TaskId, default))!.Status);
    }

    [TestMethod]
    public async Task StoragePressureEvictsTerminalOwnershipButNeverActiveTracking()
    {
        using var scope = new Scope();
        var store = scope.Store(new() { MaxStorageBytes = 4096 });
        var active = await store.CreateTaskAsync(default);
        var terminal = await store.CreateTaskAsync(default);
        await store.SetCompletedAsync(terminal.TaskId, JsonSerializer.SerializeToElement(new { text = new string('x', 8192) }), default);
        store.Cleanup();
        Assert.IsNull(await store.GetTaskAsync(terminal.TaskId, default));
        Assert.AreEqual(McpTaskStatus.Working, (await store.GetTaskAsync(active.TaskId, default))!.Status);
    }

    [TestMethod]
    public async Task ExpiredOrphanOwnershipIsCleanedWithoutTouchingSharedFiles()
    {
        using var scope = new Scope();
        var store = scope.Store();
        var persistence = new McpTaskPersistence(scope.Root);
        var mapping = persistence.PathFor("mcp-task-mappings", "orphan");
        var dispatch = persistence.PathFor("mcp-task-dispatch", "orphan");
        var report = persistence.PathFor("mcp-task-reports", "orphan");
        foreach (var path in new[] { mapping, dispatch, report })
        {
            File.WriteAllText(path, "{\"McpTaskId\":\"orphan\"}");
            File.SetLastWriteTimeUtc(path, scope.Clock.GetUtcNow().UtcDateTime.AddDays(-8));
        }
        var shared = Path.Combine(scope.Root, "shared.json");
        File.WriteAllText(shared, "{}");
        await store.MaintainAsync(default);
        Assert.IsFalse(File.Exists(mapping));
        Assert.IsFalse(File.Exists(dispatch));
        Assert.IsFalse(File.Exists(report));
        Assert.IsTrue(File.Exists(shared));
    }
}
