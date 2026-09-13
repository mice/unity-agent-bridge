using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpTaskAdapterTests
{
    [TestMethod]
    public void SupportsTasksRequiresAdvertisedCapability()
    {
        var adapter = CreateAdapter();
        Assert.IsFalse(adapter.Supports(null));
        Assert.IsFalse(adapter.Supports(JsonDocument.Parse("{}").RootElement));
        Assert.IsTrue(adapter.Supports(JsonDocument.Parse("{\"extensions\":{\"io.modelcontextprotocol/tasks\":{}}}").RootElement));
    }

    [TestMethod]
    public void BindingStoreIsProjectScopedAndPersistent()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskAdapterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        try
        {
            var first = new McpTaskBindingStore(root);
            first.Save(new McpTaskBinding(root, "mcp-1", "agent-1", "cmd-1"));
            var second = new McpTaskBindingStore(root);
            Assert.AreEqual("agent-1", second.Load(root, "mcp-1")!.AgentTaskId);
            Assert.IsNull(second.Load(root + "-other", "mcp-1"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CorruptBindingIsTreatedAsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskAdapterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        try
        {
            var store = new McpTaskBindingStore(root);
            store.Save(new McpTaskBinding(root, "mcp-corrupt", "agent-1", null));
            var mappingPath = Directory.EnumerateFiles(Path.Combine(root, "Temp", "AgentBridge", "mcp-task-mappings"), "*.json").Single();
            File.WriteAllText(mappingPath, "{not-json");
            Assert.IsNull(store.Load(root, "mcp-corrupt"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void MismatchedBindingIdentityIsTreatedAsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskAdapterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        try
        {
            var store = new McpTaskBindingStore(root);
            store.Save(new McpTaskBinding(root, "mcp-mismatch", "agent-1", null));
            var mappingPath = Directory.EnumerateFiles(Path.Combine(root, "Temp", "AgentBridge", "mcp-task-mappings"), "*.json").Single();
            File.WriteAllText(mappingPath, JsonSerializer.Serialize(new McpTaskBinding("different-project", "mcp-mismatch", "agent-1", null)));
            Assert.IsNull(store.Load(root, "mcp-mismatch"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task MissingBindingReturnsExplicitStaleFailure()
    {
        var result = await CreateAdapter().GetAsync("missing", CancellationToken.None);
        Assert.AreEqual("STALE_BINDING", result["errorCode"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task BindingFromAnotherProjectReturnsIdentityConflictWithoutBridgeDispatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskAdapterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        try
        {
            var store = new McpTaskBindingStore(root);
            store.Save(new McpTaskBinding("other-project", "mcp-shared", "agent-other", null));
            var paths = new QueuePaths(root, Path.Combine(root, "Temp", "AgentBridge"));
            CommandStore.EnsureQueueDirectories(paths);
            var adapter = new McpTaskAdapter(new ExternalBridgeClient(), paths, "current-project");

            var result = await adapter.GetAsync("mcp-shared", CancellationToken.None);

            Assert.AreEqual("IDENTITY_CONFLICT", result["errorCode"]!.GetValue<string>());
            Assert.AreEqual(0, Directory.EnumerateFiles(paths.InboxDirectory).Count());
        }
        finally { Directory.Delete(root, true); }
    }

    private static McpTaskAdapter CreateAdapter()
    {
        var root = Path.Combine(Path.GetTempPath(), "McpTaskAdapterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        var paths = new QueuePaths(root, Path.Combine(root, "Temp", "AgentBridge"));
        CommandStore.EnsureQueueDirectories(paths);
        return new McpTaskAdapter(new ExternalBridgeClient(), paths, root);
    }
}
