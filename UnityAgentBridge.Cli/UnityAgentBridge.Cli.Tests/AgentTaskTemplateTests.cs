using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentBridge;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentTaskCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class AgentTaskTemplateTests
{
    private string root = null!;
    private string path = null!;
    private FixtureExecutor executor = null!;
    private AgentTaskTemplateExecutorRegistration registration = null!;
    private static readonly string ObjectSchema = "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"message\"],\"additionalProperties\":false}";
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentTaskTemplates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings")); Directory.CreateDirectory(Path.Combine(root, "Assets"));
        UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
        path = Path.Combine(root, "ProjectSettings", "AgentTaskTemplates.json");
        executor = new FixtureExecutor(); registration = AgentTaskTemplateExecutors.Register(executor);
    }
    [TestCleanup]
    public void Cleanup() { registration.Dispose(); Directory.Delete(root, true); }
    private JObject Row(string id = "project.fixture") => new JObject
    {
        ["id"] = id, ["displayName"] = "Fixture", ["description"] = "Independent fixture", ["enabled"] = true,
        ["executorId"] = executor.Id, ["inputSchema"] = JObject.Parse(ObjectSchema), ["outputSchema"] = JObject.Parse(ObjectSchema),
        ["execution"] = new JObject { ["prompt"] = "Return the supplied JSON.", ["timeoutMs"] = 1000 }
    };
    private void Save(params JObject[] rows) => File.WriteAllText(path, new JObject { ["schemaVersion"] = 1, ["templates"] = new JArray(rows) }.ToString());
    private AgentTaskTemplateCatalog Catalog() => AgentTaskTemplateCatalog.Load(root, AgentTaskVerifiedAnalysisExecutionService.TemplateDefinitions());
    private string Args(string message = "hello") => new JObject { ["templateId"] = "project.fixture", ["inputs"] = new JObject { ["message"] = message } }.ToString();
    private AgentTaskControlResponse Run(string? args = null) => AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "create", Payload = args ?? Args(), TaskType = "template_run" });
    private AgentTaskControlResponse Query(string id) => AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "result", AgentTaskId = id });

    [TestMethod]
    public void CombinedDiscoveryIsSortedAndWorksWithoutOptionalProvider()
    {
        Save(Row()); var catalog = Catalog();
        CollectionAssert.AreEqual(new[] { "project.fixture", "unity_asset_problem_analysis", "unity_compile_failure_analysis" }, catalog.Entries.Select(e => e.Definition.Id).ToArray());
        Assert.IsTrue(catalog.Entries[0].Available); Assert.IsTrue(catalog.Entries.Skip(1).All(e => !e.Available));
        Assert.AreEqual(0, executor.Prepares); Assert.AreEqual("configured", catalog.Entries[0].Definition.Source);
        StringAssert.Contains(catalog.ToJson(), "unity.analysis.compile_failure");
    }
    [TestMethod]
    public void InvalidRowDoesNotHideValidRowAndCollisionNeverOverwrites()
    {
        var invalid = Row("project.invalid"); invalid["unknown"] = "secret";
        Save(Row(), invalid, Row("unity_compile_failure_analysis")); var catalog = Catalog();
        Assert.IsTrue(catalog.Entries.Single(e => e.Definition.Id == "project.fixture").Available);
        Assert.IsFalse(catalog.Entries.Single(e => e.Definition.Id == "project.invalid").Available);
        Assert.IsTrue(catalog.Entries.Where(e => e.Definition.Id == "unity_compile_failure_analysis").All(e => !e.Available && e.Diagnostics.Contains("TEMPLATE_ID_CONFLICT")));
        Assert.ThrowsException<ArgumentException>(() => catalog.Admit("{\"templateId\":\"unity_compile_failure_analysis\",\"inputs\":{}}"));
    }
    [TestMethod]
    public void DuplicateConfiguredIdsAreUnavailable()
    { Save(Row(), Row()); Assert.IsTrue(Catalog().Entries.Where(e => e.Definition.Source == "configured").All(e => !e.Available)); Assert.IsFalse(Run().Success); Assert.AreEqual(0, executor.Prepares); }
    [TestMethod]
    public void MissingMalformedRemovedAndUnsupportedVersionNeverUseCachedDefinitions()
    {
        Save(Row()); Assert.IsTrue(Catalog().Entries[0].Available);
        File.WriteAllText(path, "{"); Assert.AreEqual(2, Catalog().Entries.Count); Assert.AreEqual(1, Catalog().SourceDiagnostics.Count); Assert.IsFalse(Run().Success);
        File.WriteAllText(path, "{\"schemaVersion\":2,\"templates\":[]}"); Assert.AreEqual(1, Catalog().SourceDiagnostics.Count);
        File.Delete(path); Assert.AreEqual(2, Catalog().Entries.Count); Assert.AreEqual(0, Catalog().SourceDiagnostics.Count);
    }
    [TestMethod]
    public void DisabledUnknownAndConflictingExecutorsRejectBeforePrepare()
    {
        var row = Row(); row["executorId"] = "missing"; Save(row); Assert.IsFalse(Run().Success);
        row = Row(); row["enabled"] = false; Save(row); Assert.IsFalse(Run().Success);
        Save(Row()); using (AgentTaskTemplateExecutors.Register(new FixtureExecutor())) Assert.IsFalse(Run().Success);
        registration.Dispose(); Assert.IsFalse(Run().Success); Assert.AreEqual(0, executor.Prepares);
    }
    [TestMethod]
    public void SchemaAndAuthorityRejectOverridesAndUnsupportedCapabilities()
    {
        foreach (var property in new[] { "model", "tools", "paths", "credentials", "workingDirectory" })
        { var row = Row(); row["execution"]![property] = "forbidden"; Save(row); Assert.IsFalse(Run().Success, property); }
        var expensive = Row(); expensive["execution"]!["timeoutMs"] = 1001; Save(expensive); Assert.IsFalse(Run().Success);
        var reference = Row(); reference["inputSchema"]!["$ref"] = "https://example.invalid/schema"; Save(reference); Assert.IsFalse(Run().Success);
        Assert.AreEqual(0, executor.Prepares);
    }
    [TestMethod]
    public void StrictInputsAndEnvelopeRejectBeforeAllocationOrExecution()
    {
        Save(Row());
        foreach (var args in new[] { "{}", "{\"templateId\":9,\"inputs\":{}}", "{\"templateId\":\"project.fixture\",\"inputs\":[]}", "{\"templateId\":\"project.fixture\",\"inputs\":{\"message\":1}}", "{\"templateId\":\"project.fixture\",\"inputs\":{\"message\":\"x\",\"prompt\":\"override\"}}", Args("") })
        { var response = Run(args); Assert.IsFalse(response.Success, args); Assert.IsNull(response.AgentTaskId); }
        Assert.AreEqual(0, executor.Prepares);
    }
    [TestMethod]
    public void SchemaDialectSubsetRejectsInvalidShapesAndEnforcesNestedBounds()
    {
        foreach (var schema in new[] { "{\"type\":\"object\"}", "{\"type\":\"string\",\"pattern\":\".*\"}", "{\"type\":\"string\",\"minLength\":2,\"maxLength\":1}", "{\"type\":\"array\"}", "{\"type\":\"string\",\"$schema\":\"2020-12\"}" })
            Assert.ThrowsException<ArgumentException>(() => AgentTaskTemplateSchema.Check(JObject.Parse(schema)));
        var valid = JObject.Parse("{\"type\":\"array\",\"maxItems\":1,\"items\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":2}}");
        AgentTaskTemplateSchema.Check(valid); AgentTaskTemplateSchema.Validate(valid, new JArray(2));
        Assert.ThrowsException<ArgumentException>(() => AgentTaskTemplateSchema.Validate(valid, new JArray(3)));
        Assert.ThrowsException<ArgumentException>(() => AgentTaskTemplateSchema.Validate(valid, new JArray(1, 2)));
    }
    [TestMethod]
    public void ActualConfiguredExecutionPinsPromptInputsAndOutputSchemaAcrossEdits()
    {
        Save(Row()); var started = Run(Args("${model} is data")); Assert.IsTrue(started.Success, started.ErrorMessage);
        var row = Row(); row["execution"]!["prompt"] = "new definition"; row["outputSchema"]!["properties"]!["message"]!["enum"] = new JArray("different"); Save(row);
        UnityEditor.EditorApplication.Tick(); var result = Query(started.AgentTaskId);
        Assert.AreEqual(AgentTaskState.Completed, result.Snapshot.State, result.ErrorMessage);
        StringAssert.Contains(result.ResultPayloadJson, "${model} is data"); StringAssert.Contains(result.ResultPayloadJson, "templateVersion");
        StringAssert.Contains(executor.Prompts[0], "Return the supplied JSON."); Assert.IsFalse(executor.Prompts[0].Contains("new definition"));
        var next = Run(); UnityEditor.EditorApplication.Tick(); Assert.AreEqual(AgentTaskState.Failed, Query(next.AgentTaskId).Snapshot.State);
    }
    [TestMethod]
    public void RemovalAndInvalidTransitionPreserveExistingTaskAndDoNotReplay()
    {
        Save(Row()); var running = Run(); File.Delete(path); Assert.IsFalse(Run().Success);
        UnityEditor.EditorApplication.Tick(); var first = Query(running.AgentTaskId); Assert.AreEqual(AgentTaskState.Completed, first.Snapshot.State);
        File.WriteAllText(path, "broken"); Assert.IsFalse(Run().Success);
        Assert.AreEqual(first.ResultPayloadJson, Query(running.AgentTaskId).ResultPayloadJson); Assert.AreEqual(1, executor.Prepares);
    }
    [TestMethod]
    public void RetirementAndUnsupportedCancellationPreserveDisposition()
    {
        Save(Row()); var running = Run();
        var cancelled = AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "cancel", AgentTaskId = running.AgentTaskId });
        Assert.AreEqual(AgentTaskCancellationDisposition.Unsupported, cancelled.Cancellation);
        registration.Dispose(); UnityEditor.EditorApplication.Tick(); Assert.AreEqual(AgentTaskState.Completed, Query(running.AgentTaskId).Snapshot.State);
        Assert.IsFalse(Run().Success);
    }
    [TestMethod]
    public void GenericAndDedicatedBuiltinsUseSameDefinitionAndValidatedResults()
    {
        using var provider = AgentRunnerProviderRegistry.Register(new AnalysisProvider());
        Directory.CreateDirectory(Path.Combine(root, "Temp/AgentBridge/reports"));
        File.WriteAllText(Path.Combine(root, "Temp/AgentBridge/reports/e.json"), "{\"contractVersion\":\"1.0\",\"diagnostics\":[]}");
        foreach (var asset in new[] { false, true })
        {
            var id = asset ? "unity_asset_problem_analysis" : "unity_compile_failure_analysis";
            var inputs = JObject.Parse("{\"problemId\":\"fixture\",\"evidenceIds\":[\"e.json\"]}");
            var generic = Run(new JObject { ["templateId"] = id, ["inputs"] = inputs }.ToString());
            var dedicated = AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest { Operation = "create", TaskType = asset ? "analysis_asset_problem" : "analysis_compile_failure", Payload = inputs.ToString() });
            Assert.IsTrue(generic.Success, generic.ErrorMessage); Assert.IsTrue(dedicated.Success, dedicated.ErrorMessage);
            UnityEditor.EditorApplication.Tick(); var a = Query(generic.AgentTaskId); var b = Query(dedicated.AgentTaskId);
            Assert.AreEqual(AgentTaskState.Completed, a.Snapshot.State, a.ErrorMessage); Assert.AreEqual(a.ResultPayloadJson, b.ResultPayloadJson);
        }
    }
    [TestMethod]
    public void BuiltinExecutorCannotBeBorrowedByConfiguredDefinitions()
    { var row = Row(); row["executorId"] = "builtin.unity_compile_failure_analysis"; Save(row); Assert.IsFalse(Catalog().Entries[0].Available); Assert.IsFalse(Run().Success); }
    [TestMethod]
    public void PublicCatalogNamingAndTaskMetadataAreStableWithoutPlugins()
    {
        var diagnostics = new McpHostDiagnostics("", "test", Array.Empty<string>(), root, Path.Combine(root, "Temp/AgentBridge"), "", "");
        var list = McpToolCatalog.TryGet("unity_agent_task_template_list", diagnostics);
        var run = McpToolCatalog.TryGet("unity_agent_task_template_run", diagnostics);
        Assert.IsNotNull(list); Assert.IsNotNull(run); Assert.IsFalse(list.SupportsTaskExecution); Assert.IsTrue(run.SupportsTaskExecution);
        Assert.AreEqual("unity.agent_task_template.run", run.BridgeTool);
        Assert.IsNull(McpToolCatalog.TryGet("unity_agent_task_control", diagnostics));
        Assert.IsTrue(new McpTaskExecutionContext.PendingTaskRequest(run.ProtocolTool.Name, Args()).IsAgentTaskOperation);
    }

    private sealed class FixtureExecutor : IAgentTaskTemplateExecutor
    {
        public string Id => "fixture.prompt";
        public string ConfigurationSchemaJson => "{\"type\":\"object\",\"properties\":{\"prompt\":{\"type\":\"string\",\"minLength\":1},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000}},\"required\":[\"prompt\",\"timeoutMs\"],\"additionalProperties\":false}";
        public int Prepares; public List<string> Prompts = new();
        public string Validate(AgentTaskTemplateDefinition definition) => null!;
        public IAgentRunnerFactory Prepare(AgentTaskTemplateAdmission admission) { Prepares++; return new Factory(() => new Runner(admission.InputsJson, Prompts)); }
    }
    private sealed class Factory(Func<IAgentRunner> create) : IAgentRunnerFactory { public IAgentRunner Create(AgentRunRequest request) => create(); }
    private sealed class Runner(string output, List<string>? prompts = null) : IAgentRunner
    {
        public bool CanCancel => false;
        public void Start(AgentRunRequest request, IAgentRunnerContext context) => prompts?.Add(request.Task);
        public void Advance(IAgentRunnerContext context) => context.Complete(AgentRunResult.Succeeded(output));
        public void RequestCancellation(IAgentRunnerContext context) { }
    }
    private sealed class AnalysisProvider : IAgentRunnerProvider
    {
        public string Id => "fixture.analysis";
        public IEnumerable<string> TemplateIds => new[] { "unity_compile_failure_analysis", "unity_asset_problem_analysis" };
        public IAgentRunnerFactory Prepare(AgentRunnerPolicy policy)
        {
            var claim = new JObject { ["text"] = "Fixture fact", ["evidenceIds"] = new JArray("e.json"), ["confidence"] = 0.9 };
            return new Factory(() => new Runner(new JObject { ["facts"] = new JArray(claim), ["hypotheses"] = new JArray(), ["rootCause"] = claim, ["impact"] = new JArray(claim), ["recommendations"] = new JArray(claim), ["verificationPlan"] = new JArray(claim) }.ToString()));
        }
    }
}
