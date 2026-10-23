using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;
using UnityMcp.AgentBridge;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpProjectRoutingTests
{
    private string fixtureRoot = null!;
    [TestInitialize]
    public void PrepareFileFixtures()
    {
        fixtureRoot = Path.Combine(Path.GetTempPath(), "routing-protocol-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
    }
    [TestCleanup]
    public void RemoveFileFixtures() { if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true); }
    private string Workspace(string name) { var path = Path.Combine(fixtureRoot, name); Directory.CreateDirectory(path); return path; }
    private static string MarkerFixture(string path)
    {
        // Protocol-only markers: no Packages, Library, Unity process or prepared validation project.
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
        File.WriteAllText(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"), "protocol fixture");
        return path;
    }
    private sealed class Registry(params ProjectRegistration[] entries) : IProjectRegistry
    {
        public IReadOnlyList<ProjectRegistration> List() => entries;
        public ProjectRegistration? Find(string id) => entries.FirstOrDefault(x => x.projectId == id);
        public void RegisterSuccessfulConnection(ProjectRegistration registration, EditorSessionInfo session) => throw new AssertFailedException("Routing must not register before a successful connection.");
    }
    private static ProjectRegistration Registration(string path) { MarkerFixture(path); return new() { projectId = ProjectIdentityService.GetProjectId(path), projectPath = ProjectIdentityService.Canonicalize(path), queueRoot = Path.Combine(path, "Temp", "AgentBridge") }; }
    private static ProjectContext Context(string path) { var r = Registration(path); return new(r, new EditorSessionInfo { projectId = r.projectId, editorSessionId = "session-" + r.projectId }, new QueuePaths(path, r.queueRoot), "connection"); }
    [TestMethod]
    public void RegisteredDescendants_AreAmbiguousBeforeAvailabilityFiltering()
    {
        var root = Workspace("workspace");
        var router = new McpProjectRouter(new("multi-project", root), new Registry(Registration(Path.Combine(root, "A")), Registration(Path.Combine(root, "B"))), _ => throw new AssertFailedException("Availability must not choose the default."));
        var error = Assert.ThrowsException<McpProjectRoutingException>(() => router.Resolve(null, null));
        Assert.AreEqual("PROJECT_AMBIGUOUS", error.Code); Assert.AreEqual(2, error.Candidates.Count);
    }
    [TestMethod]
    public void OfflineDefault_DoesNotFallbackToAnotherProject()
    {
        var root = Workspace("offline-default");
        var selected = Path.Combine(root, "A");
        var router = new McpProjectRouter(new("multi-project", root), new Registry(Registration(selected)), path => throw new InvalidOperationException("EDITOR_EXITED"));
        var error = Assert.ThrowsException<McpProjectRoutingException>(() => router.Resolve(null, null));
        Assert.AreEqual("PROJECT_UNAVAILABLE", error.Code); Assert.AreEqual("EDITOR_EXITED", error.Message);
    }
    [TestMethod]
    public void RelativeExplicitPath_UsesCapturedWorkspace_AndIdIsAssertion()
    {
        var root = Workspace("relative");
        MarkerFixture(Path.Combine(root, "B"));
        var router = new McpProjectRouter(new("multi-project", root), new Registry(), Context);
        var selected = router.Resolve("B", null);
        Assert.AreEqual(ProjectIdentityService.Canonicalize(Path.Combine(root, "B")), selected.ProjectPath);
        var error = Assert.ThrowsException<McpProjectRoutingException>(() => router.Resolve("B", "wrong-id"));
        Assert.AreEqual("PROJECT_ID_MISMATCH", error.Code);
    }
    [TestMethod]
    public void IdAlone_CannotSelectAnotherRegisteredProject()
    {
        var root = Workspace("id-only");
        var project = Registration(Path.Combine(root, "A"));
        var router = new McpProjectRouter(new("multi-project", root), new Registry(project), Context);
        var error = Assert.ThrowsException<McpProjectRoutingException>(() => router.Resolve(null, "another-project"));
        Assert.AreEqual("PROJECT_PATH_REQUIRED", error.Code);
    }
    [TestMethod]
    public void EmptyWorkspace_HasExplicitResolutionError()
    {
        var router = new McpProjectRouter(new("multi-project", Path.GetTempPath()), new Registry(), Context);
        Assert.AreEqual("PROJECT_NOT_RESOLVED", Assert.ThrowsException<McpProjectRoutingException>(() => router.Resolve(null, null)).Code);
    }
    [TestMethod]
    public void NearestAncestorProject_WinsOverRegisteredSibling()
    {
        var parent = MarkerFixture(Workspace("parent"));
        var child = MarkerFixture(Path.Combine(parent, "child"));
        var cwd = Path.Combine(child, "Assets", "Nested"); Directory.CreateDirectory(cwd);
        var router = new McpProjectRouter(new("multi-project", cwd), new Registry(Registration(parent)), Context);
        Assert.AreEqual(ProjectIdentityService.GetProjectId(child), router.Resolve(null, null).ProjectId);
    }
    [TestMethod]
    public async Task MultiProjectHost_EmptyWorkspaceSupportsDiscoveryWithoutProjectStorage()
    {
        var root = Workspace("empty-host");
        var oldRegistry = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT");
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", Path.Combine(fixtureRoot, "registry"));
        try
        {
            var diagnostics = McpHostDiagnostics.ForWorkspace(root);
            var host = new McpMultiProjectHost(new("multi-project", root), diagnostics);
            var service = new McpServerService(new ExternalBridgeClient(), diagnostics, new McpStageLogger(Path.Combine(fixtureRoot, "host.log")), multiProject: host);
            Assert.IsTrue(service.ListTools(default).Tools.Any(t => t.Name == "unity_projects_list"));
            var result = await service.CallToolAsync(new() { Name = "unity_projects_list" }, default);
            Assert.IsFalse(result.IsError == true);
            var unresolved = await service.CallToolAsync(new() { Name = "unity_project_compile" }, default);
            Assert.IsTrue(unresolved.IsError == true);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Library")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Temp")));
        }
        finally { Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", oldRegistry); }
    }
    [TestMethod]
    public void PublicSchema_RejectsEmptySelectorsAndPreservesStrictOutputWithIdentity()
    {
        const string business = """{"type":"object","properties":{},"additionalProperties":false}""";
        var definition = new McpToolDefinition { SchemaJson = business,
            ProtocolTool = new Tool { Name = "unity_fixture_probe", InputSchema = JsonDocument.Parse(business).RootElement.Clone(), OutputSchema = JsonDocument.Parse(business).RootElement.Clone() },
            InvokeAsync = (_, _) => Task.FromResult("{}") };
        var tool = McpMultiProjectHost.PublicTool(definition);
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow(tool.Name, tool.InputSchema.GetRawText(), """{"projectPath":""}"""));
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow(tool.Name, tool.InputSchema.GetRawText(), """{"projectId":""}"""));
        McpArgumentValidator.ValidateOrThrow(tool.Name, tool.OutputSchema!.Value.GetRawText(), """{"projectId":"A","editorSessionId":"session","connectionId":"connection","projectPath":"/A"}""");
        Assert.IsFalse(tool.OutputSchema.Value.GetProperty("additionalProperties").GetBoolean());
        Assert.IsFalse(definition.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("projectPath", out _), "Public augmentation must not mutate the selected business schema.");
    }
    [TestMethod]
    public void ReservedBusinessRoutingField_IsRejected()
    {
        const string business = """{"type":"object","properties":{"projectId":{"type":"number"}}}""";
        var definition = new McpToolDefinition { SchemaJson = business, ProtocolTool = new Tool { Name = "unity_fixture_reserved", InputSchema = JsonDocument.Parse(business).RootElement.Clone() }, InvokeAsync = (_, _) => Task.FromResult("{}") };
        Assert.AreEqual("TOOL_ROUTING_FIELD_CONFLICT", Assert.ThrowsException<McpProjectRoutingException>(() => McpMultiProjectHost.PublicTool(definition)).Code);
    }
    [TestMethod]
    public void MultiProjectHostLease_PublishesProcessStartIdentityAndRemovesOwnedRecord()
    {
        var oldRegistry = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT");
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", Path.Combine(fixtureRoot, "lease-registry"));
        try
        {
            var path = Path.Combine(McpHostLease.Root, Environment.ProcessId + ".json");
            using (McpHostLease.Create("test-connection"))
            {
                var json = JsonNode.Parse(File.ReadAllText(path))!;
                Assert.AreEqual(Environment.ProcessId, json["processId"]!.GetValue<int>());
                Assert.AreEqual("multi-project", json["routingMode"]!.GetValue<string>());
                Assert.AreEqual("test-connection", json["connectionId"]!.GetValue<string>());
                Assert.IsTrue(DateTimeOffset.TryParse(json["processStartTime"]!.GetValue<string>(), out _));
            }
            Assert.IsFalse(File.Exists(path));
        }
        finally { Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT", oldRegistry); }
    }
    [TestMethod]
    public void RetainedReportRead_FailedBusinessReportIsSuccessfulOfflineReadWithOriginalIdentity()
    {
        var project = MarkerFixture(Workspace("offline-report"));
        var context = Context(project);
        const string relative = "Library/AgentBridge/mcp-task-reports/failed.json";
        var path = Path.Combine(project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"success":false,"items":["first","second","third"]}""");
        var read = McpRetainedReportReader.Read(context.QueuePaths, new JsonObject { ["reportPath"] = relative }, McpTaskRoute.From(context));
        var result = McpServerService.AdaptResult(read.ToJsonString(), McpHostDiagnostics.ForWorkspace(fixtureRoot));
        Assert.IsFalse(result.IsError == true, "A failed business result must remain a successful read.");
        Assert.IsFalse(read["metrics"]!["value"]!["success"]!.GetValue<bool>());
        Assert.AreEqual(context.ProjectId, read["projectId"]!.GetValue<string>());
        Assert.AreEqual(context.EditorSessionId, read["editorSessionId"]!.GetValue<string>());
        var slice = McpRetainedReportReader.Read(context.QueuePaths, new JsonObject { ["reportPath"] = relative, ["jsonPointer"] = "/items", ["offset"] = 1, ["limit"] = 1 }, McpTaskRoute.From(context));
        Assert.AreEqual("second", slice["metrics"]!["items"]![0]!.GetValue<string>());
        Assert.AreEqual(2, slice["metrics"]!["nextOffset"]!.GetValue<int>());
        Assert.IsFalse(Directory.Exists(Path.Combine(project, "Temp")), "Offline report reading must not create a transport queue.");
    }
    private static void WriteCatalog(string project, string mcpName, string bridgeTool, string schema)
    {
        var directory = Path.Combine(project, "Library", "AgentBridge"); Directory.CreateDirectory(directory);
        var entry = new JsonObject { ["pluginId"] = "com.example.fixture", ["pluginVersion"] = "1.0.0", ["assemblyName"] = "Fixture.Tools",
            ["bridgeTool"] = bridgeTool, ["mcpName"] = mcpName, ["title"] = "Fixture", ["description"] = "Protocol fixture", ["defaultTimeoutMs"] = 5000,
            ["allowedRuntimeModes"] = "EditAndPlay", ["sideEffect"] = "ReadsProject", ["mayTriggerDomainReload"] = false, ["inputSchemaJson"] = schema };
        File.WriteAllText(Path.Combine(directory, "plugin-catalog.json"), new JsonObject { ["version"] = 1, ["tools"] = new JsonArray(entry) }.ToJsonString());
    }
    [TestMethod]
    public void MultiCatalog_MissingMigratedPluginDoesNotInheritLegacyBuiltinAvailability()
    {
        var project = MarkerFixture(Workspace("missing-plugin"));
        var diagnostics = McpHostDiagnostics.ForWorkspace(project);
        Assert.IsNotNull(McpToolCatalog.TryGet("unity_editor_ping", diagnostics), "Single-project legacy behavior stays compatible.");
        Assert.IsNull(McpToolCatalog.TryGetProject("unity_editor_ping", diagnostics, true), "Migrated plugin tools need target catalog evidence.");
        Assert.IsNotNull(McpToolCatalog.TryGetProject("unity_project_compile", diagnostics, true), "Compile remains a core AgentTool.");
        Assert.AreEqual("PROJECT_PROTOCOL_UNSUPPORTED", Assert.ThrowsException<McpProjectRoutingException>(() => McpToolCatalog.TryGetProject("unity_project_compile", diagnostics, false)).Code);
    }
    [TestMethod]
    public void MultiCatalog_BuiltinPluginBusinessConflictIsRejectedInsteadOfSilentlySkipped()
    {
        var project = MarkerFixture(Workspace("builtin-conflict"));
        WriteCatalog(project, "unity_editor_ping", "unity.ping", """{"type":"object","properties":{"unexpected":{"type":"string"}},"additionalProperties":false}""");
        var diagnostics = McpHostDiagnostics.ForWorkspace(project);
        Assert.AreEqual("PROJECT_TOOL_CONTRACT_CONFLICT", Assert.ThrowsException<McpProjectRoutingException>(() => McpToolCatalog.TryGetProject("unity_editor_ping", diagnostics, true)).Code);
        Assert.IsFalse(McpToolCatalog.GetProjectTools(diagnostics, true).Any(tool => tool.ProtocolTool.Name == "unity_editor_ping"));
        Assert.IsNotNull(McpToolCatalog.TryGet("unity_editor_ping", diagnostics), "Legacy catalog merge remains compatible.");
    }
    [TestMethod]
    public void MultiCatalog_RereadsChangedCapabilitiesAndUsesEachTargetsSchema()
    {
        var a = MarkerFixture(Workspace("catalog-A")); var b = MarkerFixture(Workspace("catalog-B"));
        const string textSchema = """{"type":"object","properties":{"filter":{"type":"string"}},"additionalProperties":false}""";
        const string numberSchema = """{"type":"object","properties":{"filter":{"type":"integer"}},"additionalProperties":false}""";
        WriteCatalog(a, "unity_fixture_test", "unity.fixture.test", textSchema);
        WriteCatalog(b, "unity_fixture_test", "unity.fixture.test", numberSchema);
        var first = McpToolCatalog.TryGetProject("unity_fixture_test", McpHostDiagnostics.ForWorkspace(a), true)!;
        var other = McpToolCatalog.TryGetProject("unity_fixture_test", McpHostDiagnostics.ForWorkspace(b), true)!;
        McpArgumentValidator.ValidateOrThrow("unity_fixture_test", first.SchemaJson, """{"filter":"A"}""");
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow("unity_fixture_test", other.SchemaJson, """{"filter":"A"}"""));
        WriteCatalog(a, "unity_fixture_test", "unity.fixture.test", numberSchema);
        var revised = McpToolCatalog.TryGetProject("unity_fixture_test", McpHostDiagnostics.ForWorkspace(a), true)!;
        Assert.AreEqual(numberSchema, revised.SchemaJson, "A new capability publication must replace the prior contract without host restart.");
        Assert.AreEqual(textSchema, first.SchemaJson, "Existing request snapshots remain immutable.");
    }
}
