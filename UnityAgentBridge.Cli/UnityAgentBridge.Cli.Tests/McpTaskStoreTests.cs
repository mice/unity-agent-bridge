using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Extensions.Tasks;
using System.Text.Json;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpTaskStoreTests
{
    [TestMethod]
    public async Task TaskStateSurvivesStoreRecreationAndStoresCompletion()
    {
        var root = CreateProject();
        try
        {
            var first = new McpTaskStore(root);
            var created = await first.CreateTaskAsync(CancellationToken.None);
            await first.SetCompletedAsync(created.TaskId, JsonDocument.Parse("{\"ok\":true}").RootElement, CancellationToken.None);

            var second = new McpTaskStore(root);
            var restored = await second.GetTaskAsync(created.TaskId, CancellationToken.None);
            Assert.IsNotNull(restored);
            Assert.AreEqual(McpTaskStatus.Completed, restored!.Status);
            Assert.IsTrue(restored.Result.HasValue);
            Assert.AreEqual(true, restored.Result!.Value.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task CancellationAcknowledgementDoesNotInventTerminalState()
    {
        var root = CreateProject();
        try
        {
            var store = new McpTaskStore(root);
            var created = await store.CreateTaskAsync(CancellationToken.None);
            Assert.IsTrue(await store.SetCancelledAsync(created.TaskId, CancellationToken.None));
            var current = await store.GetTaskAsync(created.TaskId, CancellationToken.None);
            Assert.IsNotNull(current);
            Assert.AreEqual(McpTaskStatus.Working, current!.Status);
            StringAssert.Contains(current.StatusMessage, "remains observable");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }
}
