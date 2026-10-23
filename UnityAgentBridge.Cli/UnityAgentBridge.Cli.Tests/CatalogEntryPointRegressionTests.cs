using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class CatalogEntryPointRegressionTests
{
    private string root = null!;
    private string registryRoot = null!;
    private string? previousRegistry;
    private string catalog = null!;
    private McpMultiProjectHost host = null!;
    private McpServerService service = null!;
    private static readonly JsonElement Capabilities = JsonSerializer.SerializeToElement(new { tasks = new { } });

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), nameof(CatalogEntryPointRegressionTests), Guid.NewGuid().ToString("N"));
        registryRoot = Path.Combine(root, "registry");
        previousRegistry = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT");
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", registryRoot);
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.53f1");
        catalog = Path.Combine(root, "Library", "AgentBridge", "plugin-catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);
        var diagnostics = McpHostDiagnostics.ForWorkspace(root);
        host = new(new("multi-project", root), diagnostics);
        service = new(new ExternalBridgeClient(), diagnostics, new McpStageLogger(Path.Combine(root, "host.log")), multiProject: host);
    }

    [TestCleanup]
    public void Cleanup()
    {
        McpTaskExecutionContext.Clear();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", previousRegistry);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private void Publish(string variant)
    {
        var tools = new JsonArray();
        if (variant != "missing")
        {
            tools.Add(new JsonObject { ["pluginId"] = "com.example.fixture", ["pluginVersion"] = "1.0.0", ["assemblyName"] = "Fixture.Tools",
                ["bridgeTool"] = "unity.run_editmode_tests", ["mcpName"] = "unity_tests_run_edit_mode", ["title"] = "Tests", ["description"] = "File fixture",
                ["defaultTimeoutMs"] = 120000, ["allowedRuntimeModes"] = "Edit", ["sideEffect"] = "RunsUserCode", ["mayTriggerDomainReload"] = true,
                ["inputSchemaJson"] = variant == "conflict" ? "{\"type\":\"object\",\"properties\":{\"projectId\":{\"type\":\"integer\"}},\"additionalProperties\":false}" : "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}" });
        }
        File.WriteAllText(catalog, new JsonObject { ["version"] = 1, ["tools"] = tools }.ToJsonString());
        var revision = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(catalog))).ToLowerInvariant();
        using var process = Process.GetCurrentProcess();
        var start = process.StartTime.ToUniversalTime();
        var id = ProjectIdentityService.GetProjectId(root);
        var session = new EditorSessionInfo { projectId = id, editorSessionId = process.Id + ":" + start.Ticks,
            processId = process.Id, processStartTime = start.ToString("O"), heartbeatUtc = DateTime.UtcNow.ToString("O"),
            revision = 1, bridgeEnabled = true, bridgeVersion = "1.4.3", capabilityRevision = revision };
        var status = Path.Combine(root, "Temp", "AgentBridge", "status", "unity_bridge_status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(status)!);
        File.WriteAllText(status, JsonSerializer.Serialize(new { projectPath = root, heartbeatUtc = session.heartbeatUtc, currentStage = "unity.poller.idle", editorSession = session },
            new JsonSerializerOptions { IncludeFields = true }));
        if (variant == "changed") File.AppendAllText(catalog, " ");
    }

    private void AssertNoAdmission()
    {
        var queue = Path.Combine(root, "Temp", "AgentBridge");
        Assert.AreEqual(0, Directory.GetFiles(queue, "*.json", SearchOption.AllDirectories).Count(x => !x.Contains(Path.DirectorySeparatorChar + "status" + Path.DirectorySeparatorChar)), "No ping or task command may precede rejection.");
        foreach (var name in new[] { "mcp-tasks", "mcp-task-mappings", "mcp-task-dispatches" })
        {
            var path = Path.Combine(root, "Library", "AgentBridge", name);
            Assert.IsTrue(!Directory.Exists(path) || Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Length == 0, "No task allocation: " + name);
        }
        var index = Path.Combine(registryRoot, "task-index");
        Assert.IsTrue(!Directory.Exists(index) || Directory.GetFiles(index, "*.json", SearchOption.AllDirectories).Length == 0, "No index allocation.");
    }

    [DataTestMethod]
    [DataRow("changed", "PROJECT_CAPABILITY_MISMATCH")]
    [DataRow("missing", "PROJECT_TOOL_NOT_FOUND")]
    [DataRow("conflict", "TOOL_ROUTING_FIELD_CONFLICT")]
    public async Task OrdinaryToolEntryRejectsUnboundCatalogBeforeAnyDispatch(string variant, string expected)
    {
        Publish(variant);
        var result = await service.CallToolAsync(new CallToolRequestParams { Name = "unity_tests_run_edit_mode" }, CancellationToken.None);
        Assert.IsTrue(result.IsError);
        StringAssert.Contains(JsonSerializer.Serialize(result), expected);
        AssertNoAdmission();
    }

    [DataTestMethod]
    [DataRow("changed", "PROJECT_CAPABILITY_MISMATCH")]
    [DataRow("missing", "PROJECT_TOOL_NOT_FOUND")]
    [DataRow("conflict", "TOOL_ROUTING_FIELD_CONFLICT")]
    public async Task NativeTaskEntryRejectsUnboundCatalogBeforePingOrTaskAllocation(string variant, string expected)
    {
        Publish(variant);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsExceptionAsync<McpProjectRoutingException>(() =>
            service.CreateTaskAsync("run_editmode_tests", "{}", Capabilities, timeout.Token));
        Assert.AreEqual(expected, error.Code);
        AssertNoAdmission();
    }

    [TestMethod]
    public async Task ScopedInnerServiceRejectsChangedBytesInsteadOfReadingUnscopedCatalog()
    {
        Publish("valid");
        var context = host.Discovery.CreateCandidate(root);
        var diagnostics = host.CatalogDiagnostics(context);
        File.AppendAllText(catalog, " ");
        var inner = new McpServerService(new ExternalBridgeClient(), diagnostics, new McpStageLogger(Path.Combine(root, "inner.log")), paths: context.QueuePaths);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var result = await inner.CallToolAsync(new CallToolRequestParams { Name = "unity_tests_run_edit_mode" }, timeout.Token);
            Assert.IsTrue(result.IsError);
            StringAssert.Contains(JsonSerializer.Serialize(result), "PROJECT_CAPABILITY_MISMATCH");
        }
        catch (McpProjectRoutingException error) { Assert.AreEqual("PROJECT_CAPABILITY_MISMATCH", error.Code); }
        AssertNoAdmission();
    }
}
