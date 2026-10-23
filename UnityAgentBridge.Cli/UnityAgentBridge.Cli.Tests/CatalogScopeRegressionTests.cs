using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class CatalogScopeRegressionTests
{
    private string root = null!;
    private string catalog = null!;
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), nameof(CatalogScopeRegressionTests), Guid.NewGuid().ToString("N"));
        catalog = Path.Combine(root, "Library", "AgentBridge", "plugin-catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
        Publish("string");
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);
    private void Publish(string type)
    {
        var entry = new JsonObject { ["pluginId"] = "com.example.fixture", ["pluginVersion"] = "1.0.0", ["assemblyName"] = "Fixture.Tools",
            ["bridgeTool"] = "unity.fixture.test", ["mcpName"] = "unity_fixture_test", ["title"] = "Fixture", ["description"] = "Protocol fixture",
            ["defaultTimeoutMs"] = 5000, ["allowedRuntimeModes"] = "EditAndPlay", ["sideEffect"] = "ReadsProject", ["mayTriggerDomainReload"] = false,
            ["inputSchemaJson"] = "{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"" + type + "\"}},\"additionalProperties\":false}" };
        File.WriteAllText(catalog, new JsonObject { ["version"] = 1, ["tools"] = new JsonArray(entry) }.ToJsonString());
    }
    private string Revision() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(catalog))).ToLowerInvariant();
    private ProjectContext Context(string session, string revision) => new(new ProjectRegistration
        { projectId = "project-A", projectPath = root, queueRoot = Path.Combine(root, "queue") },
        new EditorSessionInfo { projectId = "project-A", editorSessionId = session, capabilityRevision = revision },
        new QueuePaths(root, Path.Combine(root, "queue")));
    private static ProjectSessionInfo Current(string session, string revision) => new()
        { ready = true, editorSession = new EditorSessionInfo { projectId = "project-A", editorSessionId = session, capabilityRevision = revision } };
    private McpHostDiagnostics Diagnostics(ProjectContext bound, Func<ProjectSessionInfo> observe) => McpHostDiagnostics.ForWorkspace(root) with
        { ExpectedCapabilityRevision = bound.CapabilityRevision, ValidateCatalogSnapshot = () => McpMultiProjectHost.ValidateCapabilityContext(bound, observe()) };

    [DataTestMethod]
    [DataRow("S2", false)]
    [DataRow("S1", true)]
    public void StaleSessionOrRevisionRejectsCatalogBeforeDispatch(string session, bool changeRevision)
    {
        var bound = Context("S1", Revision());
        var diagnostics = Diagnostics(bound, () => Current(session, changeRevision ? new string('0', 64) : bound.CapabilityRevision));
        var error = Assert.ThrowsException<McpProjectRoutingException>(() => McpToolCatalog.TryGetProject("unity_fixture_test", diagnostics, true));
        Assert.AreEqual("PROJECT_CAPABILITY_MISMATCH", error.Code);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "queue")), "Rejected capability observation must not dispatch or allocate transport storage.");
    }

    [TestMethod]
    public void SameCatalogBytesAreValidForNewAdmittedSessionButNotOldSnapshot()
    {
        var revision = Revision();
        var old = Context("S1", revision);
        var next = Context("S2", revision);
        var current = Current("S2", revision);
        Assert.AreEqual("PROJECT_CAPABILITY_MISMATCH", Assert.ThrowsException<McpProjectRoutingException>(() =>
            McpToolCatalog.TryGetProject("unity_fixture_test", Diagnostics(old, () => current), true)).Code);
        Assert.IsNotNull(McpToolCatalog.TryGetProject("unity_fixture_test", Diagnostics(next, () => current), true));
    }

    [TestMethod]
    public void ChangedCatalogBytesRejectOriginalSnapshotAndNewDigestAllowsNewContract()
    {
        var original = Context("S1", Revision());
        Publish("integer");
        Assert.AreEqual("PROJECT_CAPABILITY_MISMATCH", Assert.ThrowsException<McpProjectRoutingException>(() =>
            McpToolCatalog.TryGetProject("unity_fixture_test", Diagnostics(original, () => Current("S1", original.CapabilityRevision)), true)).Code);
        var revised = Context("S1", Revision());
        var tool = McpToolCatalog.TryGetProject("unity_fixture_test", Diagnostics(revised, () => Current("S1", revised.CapabilityRevision)), true)!;
        StringAssert.Contains(tool.SchemaJson, "integer");
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "queue")));
    }

    [TestMethod]
    public void ValidatedCatalogParsesTheSameBytesItHashedDespiteLaterDiskReplacement()
    {
        var revision = Revision();
        var bound = Context("S1", revision);
        var validations = 0;
        var diagnostics = McpHostDiagnostics.ForWorkspace(root) with
        {
            ExpectedCapabilityRevision = revision,
            ValidateCatalogSnapshot = () => { validations++; McpMultiProjectHost.ValidateCapabilityContext(bound, Current("S1", revision)); Publish("integer"); }
        };
        var tool = McpToolCatalog.TryGetProject("unity_fixture_test", diagnostics, true)!;
        Assert.AreEqual(1, validations);
        var schema = JsonNode.Parse(tool.SchemaJson)!;
        Assert.AreEqual("string", schema["properties"]!["filter"]!["type"]!.GetValue<string>());
        Assert.AreNotEqual(revision, Revision());
    }
}
