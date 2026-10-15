using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentBridge;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class AnalysisEvidenceObservationTests
{
    private string root = null!;
    private Provider provider = null!;
    private AgentRunnerProviderRegistration registration = null!;
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AnalysisEvidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        UnityEngine.Application.dataPath = Path.Combine(root, "Assets");
        provider = new Provider(); registration = AgentRunnerProviderRegistry.Register(provider);
    }
    [TestCleanup]
    public void Cleanup() { registration.Dispose(); Directory.Delete(root, true); }
    private void Write(string relative, string? text = null)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text ?? "{\"contractVersion\":\"compile.v1\",\"diagnostics\":[]}");
    }
    private AgentTaskControlResponse Run(string reference, bool dedicated = false)
    {
        provider.Reference = reference;
        var inputs = new JObject { ["problemId"] = "zero-diagnostic-report", ["evidenceIds"] = new JArray(reference) };
        return dedicated
            ? AgentTaskVerifiedAnalysisExecutionService.Control(new AgentTaskControlRequest { Operation = "create", TaskType = "analysis_compile_failure", Payload = inputs.ToString() })
            : AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "create", Payload = new JObject { ["templateId"] = "unity_compile_failure_analysis", ["inputs"] = inputs }.ToString() });
    }

    [DataTestMethod]
    [DataRow("Library/AgentBridge/compile-reports/compile_real.json", "Library/AgentBridge/compile-reports/compile_real.json")]
    [DataRow("Library/AgentBridge/compile-reports/compile_real.json", "compile_real.json")]
    [DataRow("Library/AgentBridge/compile-reports/compile_real.json", "real")]
    [DataRow("Library/AgentBridge/mcp-task-reports/retained.json", "Library/AgentBridge/mcp-task-reports/retained.json")]
    [DataRow("Temp/AgentBridge/reports/compile_real.json", "Temp/AgentBridge/reports/compile_real.json")]
    [DataRow("Temp/AgentBridge/reports/compile_real.json", "compile_real.json")]
    public void GenuineCompleteReportWorksThroughBothEntrypoints(string file, string reference)
    {
        Write(file);
        foreach (var dedicated in new[] { false, true })
        {
            var response = Run(reference, dedicated);
            Assert.IsTrue(response.Success, response.ErrorMessage);
            Assert.IsTrue(response.Snapshot.Execution.ProcessStarted);
            Assert.AreEqual("prompt_submitted", response.Snapshot.Execution.Stage);
            UnityEditor.EditorApplication.Tick();
            var terminal = AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "result", AgentTaskId = response.AgentTaskId });
            Assert.AreEqual(AgentTaskState.Completed, terminal.Snapshot.State, terminal.ErrorMessage);
            Assert.AreEqual("terminal", terminal.Snapshot.Execution.Stage);
            StringAssert.Contains(terminal.ResultPayloadJson, reference);
            var codec = new AgentTaskControlCodec();
            var roundtrip = codec.DeserializeResponse(codec.SerializeResponse(terminal));
            Assert.AreEqual(123, roundtrip.Snapshot.Execution.ProcessId);
            Assert.AreEqual("submitted", roundtrip.Snapshot.Execution.PromptSubmittedAt);
            Assert.IsTrue(roundtrip.Snapshot.Execution.ProcessStarted);
        }
    }

    [DataTestMethod]
    [DataRow("Library/AgentBridge/compile-reports/missing.json")]
    [DataRow("../outside.json")]
    [DataRow("Library/AgentBridge/compile-reports/../compile_real.json")]
    [DataRow("C:/outside.json")]
    [DataRow("Library/AgentBridge/compile-reports/./compile_real.json")]
    [DataRow("Assets/compile_real.json")]
    public void MissingAndUnsafeReferencesNeverStartRunner(string reference)
    {
        Write("Library/AgentBridge/compile-reports/compile_real.json");
        var response = Run(reference);
        Assert.IsFalse(response.Success); Assert.IsNull(response.AgentTaskId);
        Assert.AreEqual("EVIDENCE_INCOMPLETE", response.ErrorCode);
        var wire = JObject.Parse(new AgentTaskControlCodec().SerializeResponse(response));
        Assert.AreEqual("not_started", wire["execution"]!["stage"]!.Value<string>());
        Assert.AreEqual(false, wire["execution"]!["processStarted"]!.Value<bool>());
        Assert.AreEqual(0, provider.Creates);
    }

    [DataTestMethod]
    [DataRow("{")]
    [DataRow("{\"contractVersion\":\"invalid\",\"diagnostics\":[]}")]
    [DataRow("{\"contractVersion\":\"compile.v1\",\"diagnostics\":{}}")]
    public void MalformedReportsFailBeforeExecution(string text)
    {
        Write("Library/AgentBridge/compile-reports/bad.json", text);
        Assert.AreEqual("EVIDENCE_INCOMPLETE", Run("Library/AgentBridge/compile-reports/bad.json").ErrorCode);
        Assert.AreEqual(0, provider.Creates);
    }
    [TestMethod]
    public void AmbiguousBasenameAndOversizedReportFailClosed()
    {
        Write("Library/AgentBridge/compile-reports/compile_real.json"); Write("Temp/AgentBridge/reports/compile_real.json");
        Assert.IsFalse(Run("compile_real.json").Success);
        Write("Library/AgentBridge/compile-reports/large.json", new string('x', 131073));
        Assert.IsFalse(Run("Library/AgentBridge/compile-reports/large.json").Success);
        Assert.AreEqual(0, provider.Creates);
    }
    [TestMethod]
    public void PhysicalLinkEscapeIsRejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "e.json"), "{\"contractVersion\":\"compile.v1\",\"diagnostics\":[]}");
        var link = Path.Combine(root, "Library/AgentBridge/compile-reports/link"); Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                process!.WaitForExit(); Assert.AreEqual(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(link, outside);
            Assert.IsFalse(Run("Library/AgentBridge/compile-reports/link/e.json").Success);
            Assert.AreEqual(0, provider.Creates);
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(outside, true); }
    }
    [TestMethod]
    public void LegacyCodecRecordsRemainUnknown()
    {
        var response = new AgentTaskControlCodec().DeserializeResponse("{\"version\":1,\"success\":false,\"agentTaskId\":null}");
        Assert.AreEqual("unknown", response.Execution.Stage); Assert.IsNull(response.Execution.ProcessStarted);
        var missing = AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "query" });
        Assert.AreEqual("unknown", missing.Execution.Stage); Assert.IsNull(missing.Execution.ProcessStarted);
    }
    private sealed class Provider : IAgentRunnerProvider
    {
        public string Reference = ""; public int Creates;
        public string Id => "fixture.observed";
        public IEnumerable<string> TemplateIds => new[] { "unity_compile_failure_analysis" };
        public IAgentRunnerFactory Prepare(AgentRunnerPolicy policy) => new Factory(this);
    }
    private sealed class Factory(Provider provider) : IAgentRunnerFactory
    {
        public IAgentRunner Create(AgentRunRequest request) { provider.Creates++; return new Runner(provider.Reference); }
    }
    private sealed class Runner(string reference) : IAgentRunner, IAgentExecutionObservable
    {
        public bool CanCancel => true;
        public AgentExecutionObservation Execution { get; private set; } = AgentExecutionObservation.NotStarted;
        public void Start(AgentRunRequest request, IAgentRunnerContext context) => Execution = new("prompt_submitted", "fixture.observed", true, "started", 123, "ready", "submitted");
        public void Advance(IAgentRunnerContext context)
        {
            Execution = Execution.Terminal("finished");
            var claim = new JObject { ["text"] = "No diagnostic errors are reported", ["evidenceIds"] = new JArray(reference), ["confidence"] = 1.0 };
            context.Complete(AgentRunResult.Succeeded(new JObject { ["facts"] = new JArray(claim), ["hypotheses"] = new JArray(), ["rootCause"] = claim, ["impact"] = new JArray(), ["recommendations"] = new JArray(), ["verificationPlan"] = new JArray() }.ToString()));
        }
        public void RequestCancellation(IAgentRunnerContext context) { Execution = Execution.Terminal("cancelled"); context.Cancel(); }
    }
}
