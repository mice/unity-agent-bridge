using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor.TestTools.TestRunner.Api;
using TestStatus = UnityEditor.TestTools.TestRunner.Api.TestStatus;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using UnityMcp.AgentTaskEditor;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class EditModeDiagnosticContractTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_270.md
        [Test, Category("AGBM_270")]
        public void FinalTreeRestoresEarlierLeafAndRetainsAssertionStackAndReport()
        {
            var context = new CaptureContext();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                var task = new RunEditModeTestsTask(api, new Filter { testMode = TestMode.EditMode },
                    (id, payload) => "Library/AgentBridge/editor-test-reports/" + id + ".json");
                task.Reattach(context, "restored-run");
                var beforeReload = new Result { FullName = "Before.Reload", TestStatus = TestStatus.Failed,
                    Message = "AGBMP_EXPECTED_DIAGNOSTIC:project-A", StackTrace = "at Before.Reload() in Assets/Before.cs:9" };
                var afterReload = new Result { FullName = "After.Reload", TestStatus = TestStatus.Passed };
                task.TestFinished(afterReload);
                task.RunFinished(new Result { Children = new ITestResultAdaptor[] { beforeReload, afterReload } });
                var payload = (EditModeTestTaskPayload)context.Completed.Payload;
                Assert.That(context.Completed.Kind, Is.EqualTo(AgentTaskResultKind.DomainFailure));
                Assert.That(payload.tests.Length, Is.EqualTo(2));
                Assert.That(payload.tests.Single(test => test.fullName == "Before.Reload").message,
                    Is.EqualTo(beforeReload.Message));
                Assert.That(payload.tests.Single(test => test.fullName == "Before.Reload").stackTrace,
                    Is.EqualTo(beforeReload.StackTrace));
                Assert.That(payload.errors.Single().message, Does.Contain(beforeReload.Message));
                Assert.That(payload.errors.Single().stackTrace, Is.EqualTo(beforeReload.StackTrace));
                Assert.That(payload.reportPath, Does.StartWith("Library/AgentBridge/editor-test-reports/"));
                Assert.That(context.Error, Is.Null);
            }
            finally { if (api != null) UnityEngine.Object.DestroyImmediate(api); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_271.md
        [Test, Category("AGBM_271")]
        public void ReportWriteFailureDoesNotTurnPassingTestsIntoAssertionFailure()
        {
            var context = new CaptureContext();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                var task = new RunEditModeTestsTask(api, new Filter { testMode = TestMode.EditMode },
                    (id, payload) => { throw new IOException("fixture-owned report store unavailable"); });
                task.Reattach(context, "report-failure-run");
                task.RunFinished(new Result { FullName = "Passing.Test", TestStatus = TestStatus.Passed });
                var payload = (EditModeTestTaskPayload)context.Completed.Payload;
                Assert.That(context.Completed.Kind, Is.EqualTo(AgentTaskResultKind.Succeeded));
                Assert.That(payload.errors, Is.Empty);
                Assert.That(payload.warnings.Single().code, Is.EqualTo("AGENT_TASK_REPORT_UNAVAILABLE"));
                Assert.That(payload.reportError, Does.Contain("report store unavailable"));
                Assert.That(payload.reportPath, Is.Null);
            }
            finally { if (api != null) UnityEngine.Object.DestroyImmediate(api); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_272.md
        [Test, Category("AGBM_272")]
        public void RetainedReportAndLegacyProjectionPreserveDiagnostic()
        {
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            var key = "diagnostic-contract-" + Guid.NewGuid().ToString("N");
            string reportPath = null;
            try
            {
                var payload = new EditModeTestTaskPayload { errors = new[] {
                    new EditModeTestDiagnostic { code = "UNITY_TEST_FAILURE", message = "marker-project-A", stackTrace = "fixture stack" } } };
                reportPath = AgentBridgeReportWriter.WriteReport(settings, key, "editmode_tests", payload, retainInLibrary: true);
                payload.reportPath = reportPath;
                Assert.That(reportPath, Does.StartWith("Library/AgentBridge/editor-test-reports/"));
                var absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, reportPath);
                Assert.That(File.ReadAllText(absolute), Does.Contain("marker-project-A"));
                var manager = new AgentTaskManager(idFactory: () => new AgentTaskId(key));
                var id = manager.Register(new CompletedTask(payload));
                manager.Start(id);
                var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "unity.run_editmode_tests");
                Assert.That(result.reportPath, Is.EqualTo(reportPath));
                Assert.That(result.errors.Single().message, Is.EqualTo("marker-project-A"));
                Assert.That(result.metricsObjectJson, Does.Contain("fixture stack"));
            }
            finally
            {
                if (reportPath != null) File.Delete(Path.Combine(Directory.GetParent(Application.dataPath).FullName, reportPath));
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_274.md
        [Test, Category("AGBM_274")]
        public void LowRawReportLimitPreservesHostOwnedReports()
        {
            var root = Path.Combine(Path.GetTempPath(), "agb-raw-reports-" + Guid.NewGuid().ToString("N"));
            var raw = Path.Combine(root, "editor-test-reports");
            var host = Path.Combine(root, "mcp-task-reports");
            Directory.CreateDirectory(raw);
            Directory.CreateDirectory(host);
            try
            {
                var first = Path.Combine(raw, "editmode_tests_first.json");
                var second = Path.Combine(raw, "editmode_tests_second.json");
                var retained = Path.Combine(host, "host-owned-hash.json");
                File.WriteAllText(first, "first raw");
                File.WriteAllText(second, "second raw");
                File.WriteAllText(retained, "still referenced by retained host task");
                var now = DateTime.UtcNow;
                File.SetLastWriteTimeUtc(first, now.AddMinutes(-2));
                File.SetLastWriteTimeUtc(second, now.AddMinutes(-1));
                File.SetLastWriteTimeUtc(retained, now.AddDays(-30));
                AgentTaskDurableResults.CleanupEditorTestReports(root, maximumReports: 1, utcNow: now);
                Assert.That(File.Exists(first), Is.False);
                Assert.That(File.Exists(second), Is.True);
                Assert.That(File.ReadAllText(retained), Is.EqualTo("still referenced by retained host task"));
            }
            finally { Directory.Delete(root, true); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_275.md
        [Test, Category("AGBM_275")]
        public void MissingFinalRunnerResultIsInfrastructureObservationFailure()
        {
            var context = new CaptureContext();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                var task = new RunEditModeTestsTask(api, new Filter { testMode = TestMode.EditMode });
                task.Reattach(context, "missing-result-run");
                task.RunFinished(null);
                Assert.That(context.Completed, Is.Null);
                Assert.That(context.Error.Code, Is.EqualTo("AGENT_TASK_TEST_RUNNER_RESULT_MISSING"));
            }
            finally { if (api != null) UnityEngine.Object.DestroyImmediate(api); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_276.md
        [Test, Category("AGBM_276")]
        public void CodecRoundTripRestoresOpaqueDiagnosticsWarningsAndReport()
        {
            var payload = new EditModeTestTaskPayload {
                errors = new[] { new EditModeTestDiagnostic { code = "UNITY_TEST_FAILURE", message = "original assertion", stackTrace = "original stack" } },
                warnings = new[] { new EditModeTestDiagnostic { code = "AGENT_TASK_REPORT_UNAVAILABLE", message = "original report warning" } },
                reportPath = "Library/AgentBridge/editor-test-reports/original.json" };
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("codec-diagnostic"));
            var id = manager.Register(new CompletedTask(payload));
            manager.Start(id);
            var codec = new AgentTaskControlCodec();
            var retained = codec.DeserializeResponse(codec.SerializeResponse(new AgentTaskControlResponse {
                Success = true, AgentTaskId = id.Value, Snapshot = manager.Get(id), ResultPayloadJson = JsonUtil.SerializeObject(payload) }));
            var result = AgentTaskEditModeExecutionService.ProjectRetainedResult(retained,
                new AgentCommand { commandId = "original-command", tool = "unity.run_editmode_tests" });
            Assert.That(result.errors.Single().message, Is.EqualTo("original assertion"));
            Assert.That(result.warnings.Single().message, Is.EqualTo("original report warning"));
            Assert.That(result.reportPath, Is.EqualTo(payload.reportPath));
            Assert.That(result.metricsObjectJson, Does.Contain("original stack"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_277.md
        [TestCase("editor-test-reports", "editmode_tests_fixture.json")]
        [TestCase("command-history", "r-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json")]
        [TestCase("command-history", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-report-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json")]
        [Category("AGBM_277")]
        public void SingleProjectReportReaderReadsRawAndOwnedCopiedReport(string directory, string filename)
        {
            var root = Path.Combine(Path.GetTempPath(), "agb-reader-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, "Library", "AgentBridge", directory);
            Directory.CreateDirectory(folder);
            try
            {
                var payload = new EditModeTestTaskPayload { errors = new[] { new EditModeTestDiagnostic { code = "UNITY_TEST_FAILURE", message = "single-mode diagnostic" } } };
                File.WriteAllText(Path.Combine(folder, filename), JsonUtil.SerializeObject(payload));
                var context = new UnityMcp.Plugin.UnityMcpToolContext { ProjectRoot = root, TempRoot = "Temp/AgentBridge", CommandId = "reader-fixture",
                    ToolName = "unity.read_report", RawArgsJson = "{\"reportPath\":\"Library/AgentBridge/" + directory + "/" + filename + "\"}" };
                var result = new UnityMcp.BuiltInPlugins.UnityQueries.UnityReadReportTool().Execute(context, null);
                Assert.That(result.Success, Is.True, result.Summary);
                Assert.That(result.MetricsObjectJson, Does.Contain("single-mode diagnostic"));
                if (directory == "command-history")
                {
                    var metadataName = new string('c', 64) + ".json";
                    File.WriteAllText(Path.Combine(folder, metadataName), JsonUtil.SerializeObject(payload));
                    context.RawArgsJson = "{\"reportPath\":\"Library/AgentBridge/command-history/" + metadataName + "\"}";
                    var rejected = new UnityMcp.BuiltInPlugins.UnityQueries.UnityReadReportTool().Execute(context, null);
                    Assert.That(rejected.Success, Is.False);
                    Assert.That(rejected.Errors.Single().Code, Is.EqualTo("AGENTBRIDGE_REPORT_PATH_OUTSIDE_ROOT"));
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private sealed class CompletedTask : IAgentTask
        {
            private readonly EditModeTestTaskPayload payload;
            public CompletedTask(EditModeTestTaskPayload payload) { this.payload = payload; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) => context.Complete(AgentTaskResult.DomainFailure("TestAssertionsFailed", payload));
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }

        private sealed class CaptureContext : IAgentTaskContext
        {
            public AgentTaskId Id => new AgentTaskId("diagnostic-capture");
            public bool CancellationRequested => false;
            public AgentTaskResult Completed;
            public AgentTaskError Error;
            public void Complete(AgentTaskResult result) { Completed = result; }
            public void Fail(AgentTaskError error) { Error = error; }
            public void Cancel(AgentTaskResult result = null) { }
        }

        private sealed class Result : ITestResultAdaptor
        {
            public ITestAdaptor Test => null;
            public string Name => FullName;
            public string FullName { get; set; } = "Suite";
            public string ResultState => TestStatus.ToString();
            public TestStatus TestStatus { get; set; }
            public double Duration => 0.001;
            public DateTime StartTime => default(DateTime);
            public DateTime EndTime => default(DateTime);
            public string Message { get; set; } = string.Empty;
            public string StackTrace { get; set; } = string.Empty;
            public int AssertCount => 1;
            public int FailCount => HasChildren ? Children.Sum(child => child.FailCount) : TestStatus == TestStatus.Failed ? 1 : 0;
            public int PassCount => HasChildren ? Children.Sum(child => child.PassCount) : TestStatus == TestStatus.Passed ? 1 : 0;
            public int SkipCount => 0;
            public int InconclusiveCount => 0;
            public bool HasChildren => Children.Any();
            public IEnumerable<ITestResultAdaptor> Children { get; set; } = new ITestResultAdaptor[0];
            public string Output => "fixture output";
            public TNode ToXml() => null;
        }
    }
}
