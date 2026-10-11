using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class BridgeHeartbeatReadTests
{
    private string _root = null!;
    private QueuePaths _paths = null!;
    private string _status = null!;

    [TestInitialize]
    public void Initialize()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file-sharing acceptance.");
        _root = Path.Combine(Path.GetTempPath(), "BridgeHeartbeatRead-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
        _paths = new QueuePaths(_root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(_paths);
        _status = Path.Combine(_paths.StatusDirectory, "unity_bridge_status.json");
        File.WriteAllText(_status, new JObject
        {
            ["heartbeatUtc"] = DateTime.UtcNow.ToString("O"),
            ["currentStage"] = "unity.poller.idle",
            ["projectPath"] = _root.Replace('\\', '/')
        }.ToString());
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // TestRecord: Documentation~/AgentBridge/test_records/AGB_293.md
    [TestMethod, TestCategory("AGB_293")]
    public void FreshSnapshotRemainsReadableWithPublishingHandle()
    {
        using var publishing = new FileStream(_status, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        VerifyClassification("Allowed");
    }

    // TestRecord: Documentation~/AgentBridge/test_records/AGB_294.md
    [TestMethod, TestCategory("AGB_294")]
    public void ExclusivelyLockedSnapshotReturnsBlockedHealthWithoutThrowing()
    {
        using var publishing = new FileStream(_status, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        VerifyClassification("BlockedBeforeDispatch");
    }

    // TestRecord: Documentation~/AgentBridge/test_records/AGB_295.md
    [TestMethod, TestCategory("AGB_295")]
    public void MalformedSnapshotReturnsBlockedHealthWithoutThrowing()
    {
        File.WriteAllText(_status, "{ partial JSON");
        VerifyClassification("BlockedBeforeDispatch");
    }

    private void VerifyClassification(string expected)
    {
        var client = new BridgeHealthClient(new EmptyDiscovery());
        Assert.AreEqual(expected, client.EvaluateLifecycle(_paths).ToolExecution);
        Assert.IsTrue(client.TryHandleLocalCommand(_paths,
            new BridgeCommandSpec("unity.bridge_health", 5000, "{}"), "health", out var health));
        var payload = JObject.Parse(health.RawJson);
        Assert.AreEqual(expected, payload.Value<string>("toolExecution"));
        Assert.IsTrue(client.TryHandleLocalCommand(_paths,
            new BridgeCommandSpec("unity.editor_list", 5000, "{\"includeBridgeHealth\":true}"), "list", out var editors));
        var list = JObject.Parse(editors.RawJson);
        Assert.AreEqual(payload.Value<string>("healthReason"), list["bridgeHealth"]?.Value<string>("healthReason"));
    }

    private sealed class EmptyDiscovery : IUnityEditorProcessDiscovery
    {
        public IReadOnlyList<UnityEditorInstance> Discover() => Array.Empty<UnityEditorInstance>();
    }
}
