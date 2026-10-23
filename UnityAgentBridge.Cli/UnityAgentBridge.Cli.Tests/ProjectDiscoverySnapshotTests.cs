using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class ProjectDiscoverySnapshotTests
{
    private string _root = null!;
    [TestInitialize] public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "agb-snapshot-" + Guid.NewGuid().ToString("N")));
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    [TestMethod]
    public void RegistrySnapshotReportsBrokenSiblingsWithoutDiscardingHealthyRegistration()
    {
        var registry = new ProjectRegistry(_root);
        var registration = new ProjectRegistration { projectId = "project-good", projectPath = Path.Combine(_root,"project"), queueRoot = Path.Combine(_root,"queue") };
        File.WriteAllText(Path.Combine(_root,"project-good.json"), JsonConvert.SerializeObject(registration));
        File.WriteAllText(Path.Combine(_root,"project-broken.json"), "{");
        File.WriteAllText(Path.Combine(_root,"project-future.json"), "{\"schemaVersion\":2}");
        File.WriteAllText(Path.Combine(_root,"project-missing.json"), "{}");
        var snapshot = registry.ReadSnapshot();
        Assert.AreEqual(1, snapshot.Registrations.Count);
        CollectionAssert.AreEquivalent(new[] { "REGISTRATION_CORRUPT", "REGISTRATION_CORRUPT", "REGISTRATION_UNSUPPORTED" }, snapshot.Diagnostics.Select(x => x.Reason).ToArray());
        Assert.IsTrue(snapshot.Diagnostics.All(x => x.Availability == "unavailable" && !x.Ready));
        Assert.IsTrue(JObject.FromObject(snapshot.Diagnostics[0])["reason"]?.Type == JTokenType.String);
    }
    [DataTestMethod]
    [DataRow("isCompiling", "EDITOR_COMPILING", "recovering", false)]
    [DataRow("isUpdating", "EDITOR_UPDATING", "recovering", false)]
    [DataRow("reload", "EDITOR_RELOADING", "recovering", false)]
    [DataRow("stopping", "EDITOR_STOPPING", "unavailable", false)]
    [DataRow("busy", "BRIDGE_BUSY", "online", true)]
    public void RootSnapshotReadinessIsAggregatedFromSameFreshRead(string flag, string reason, string availability, bool ready)
    {
        var registration = new ProjectRegistration { projectId = "project-test", projectPath = _root, queueRoot = Path.Combine(_root,"queue") };
        var session = new EditorSessionInfo { projectId = registration.projectId, editorSessionId = "test", bridgeEnabled = true, heartbeatUtc = DateTimeOffset.UtcNow.ToString("O") };
        var json = JObject.FromObject(new { editorSession = session });
        if (flag is "isCompiling" or "isUpdating") json[flag] = true;
        else { json["currentStage"] = flag == "busy" ? "unity.tool.start" : flag; json["currentCommandId"] = "command"; }
        var paths = new QueuePaths(_root,registration.queueRoot);
        CommandStore.EnsureQueueDirectories(paths);
        File.WriteAllText(Path.Combine(registration.queueRoot, QueueLayout.StatusDirectoryName, QueueLayout.StatusFileName), json.ToString());
        var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root,"registry")), processAlive: _ => true);
        var observed = discovery.Observe(registration);
        Assert.AreEqual(reason, observed.reason); Assert.AreEqual(availability, observed.availability); Assert.AreEqual(ready, observed.ready);
        json.Remove("isCompiling"); json.Remove("isUpdating"); json["currentStage"] = "idle";
        File.WriteAllText(Path.Combine(registration.queueRoot, QueueLayout.StatusDirectoryName, QueueLayout.StatusFileName), json.ToString());
        Assert.IsTrue(discovery.Observe(registration).ready, "Next invocation must reread root flags rather than cache readiness.");
    }
}
