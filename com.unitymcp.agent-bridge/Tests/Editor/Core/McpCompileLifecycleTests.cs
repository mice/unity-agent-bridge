using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using UnityMcp.Plugin;
using UnityMcp.BuiltInPlugins.UnityQueries;
using UnityEditor;
using UnityEngine.TestTools;
using System.Collections;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class McpCompileLifecycleTests
    {
        private static readonly string DomainMarker = Guid.NewGuid().ToString("N");
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_350.md
        [UnityTest, Category("AGB_350")]
        public IEnumerator EditModeRunnerContinuesAfterDomainReload()
        {
            const string key = "AGB_350.reloadRequested";
            if (!SessionState.GetBool(key, false))
            {
                SessionState.SetBool(key, true);
                SessionState.SetString(key + ".domain", DomainMarker);
                EditorUtility.RequestScriptReload();
            }
            yield return new WaitForDomainReload();
            try { Assert.That(DomainMarker, Is.Not.EqualTo(SessionState.GetString(key + ".domain", string.Empty))); }
            finally { SessionState.EraseBool(key); SessionState.EraseString(key + ".domain"); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_351.md
        [Test, Category("AGB_351")]
        public void EditModeRecoveryDoesNotAttachToAnotherActiveRunner()
        {
            Assert.That(AgentTaskEditModeExecutionService.CanReattachRun("unrelated-" + Guid.NewGuid().ToString("N")), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_339.md
        [Test, Category("AGB_339")]
        public void CompilerLocationsRemainDirectlyReadable()
        {
            var state = ErrorState(1);
            var result = Build(state);
            Assert.That(result.success, Is.False);
            Assert.That(result.errors.Single().code, Is.EqualTo("CS1002"));
            Assert.That(result.errors[0].file, Is.EqualTo("Assets/Broken.cs"));
            Assert.That(result.errors[0].line, Is.EqualTo(4));
            Assert.That(result.errors[0].column, Is.EqualTo(9));
            Assert.That(JObject.Parse(result.metricsObjectJson).Value<int>("targetEpoch"), Is.EqualTo(12));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_340.md
        [Test, Category("AGB_340")]
        public void WarningsOnlyCompilationSucceedsAndKeepsWarningsSeparate()
        {
            var state = ErrorState(0);
            state.warningCount = 1;
            state.compilerWarnings.Add(new CompilerWarningRecord { code = "CS0168", message = "unused", file = "Assets/Warning.cs", line = 2 });
            var result = Build(state);
            Assert.That(result.success, Is.True);
            Assert.That(result.errors, Is.Empty);
            Assert.That(result.warnings.Single().code, Is.EqualTo("CS0168"));
            Assert.That(JObject.Parse(result.metricsObjectJson)["diagnosticSamples"][0].Value<string>("severity"), Is.EqualTo("warning"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_341.md
        [Test, Category("AGB_341")]
        public void DiagnosticOverflowReportsExactCountsAndTruncation()
        {
            var result = Build(ErrorState(25));
            var metrics = JObject.Parse(result.metricsObjectJson);
            Assert.That(result.errors.Count, Is.EqualTo(20));
            Assert.That(metrics.Value<int>("errorCount"), Is.EqualTo(25));
            Assert.That(metrics.Value<bool>("diagnosticsTruncated"), Is.True);
            Assert.That(metrics.Value<bool>("diagnosticsComplete"), Is.True);
            Assert.That(metrics.Value<int>("inlineErrorCount"), Is.EqualTo(20));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_342.md
        [Test, Category("AGB_342")]
        public void CompileTimeoutRetainsPartialCompilerDiagnostics()
        {
            var state = ErrorState(1);
            state.timeoutReason = "compile_finish_timeout";
            var result = (ToolResult)typeof(UnityCompileOperationManager).GetMethod("BuildTimeoutResult", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { state });
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Timeout));
            Assert.That(result.errors.Single().code, Is.EqualTo("CS1002"));
            var metrics = JObject.Parse(result.metricsObjectJson);
            Assert.That(metrics.Value<int>("errorCount"), Is.EqualTo(1));
            Assert.That(metrics.Value<bool>("diagnosticsComplete"), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_343.md
        [Test, Category("AGB_343")]
        public void HistoricalConsoleEntriesDoNotContaminateCleanCompilation()
        {
            var state = ErrorState(0);
            state.consoleFallbackEntries.Add(new ConsoleFallbackEntry { condition = "Assets/Old.cs(1,1): error CS1002: stale console error" });
            var result = Build(state);
            Assert.That(result.success, Is.True);
            Assert.That(result.errors, Is.Empty);
            Assert.That(JObject.Parse(result.metricsObjectJson)["followUp"].Value<bool>("recommended"), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_344.md
        [Test, Category("AGB_344")]
        public void ReobservingACompletedCommandReturnsItsResultWithoutAnotherOperation()
        {
            var id = "fixture-compile-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(AgentTaskDurableResults.Root, "compile-results", id + ".json");
            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            try
            {
                AgentTaskDurableResults.WriteAtomic(path, JsonUtil.SerializeObject(new ToolResult { commandId = id, tool = "unity.compile", success = true, status = ToolResultStatus.Success, summary = "original" }));
                var result = UnityCompileOperationManager.StartOrResume(new AgentCommand { commandId = id, tool = "unity.compile", timeoutMs = 1000 }, settings);
                Assert.That(result.summary, Is.EqualTo("original"));
                Assert.That(UnityCompileOperationManager.HasOperation(id), Is.False);
            }
            finally { File.Delete(path); UnityEngine.Object.DestroyImmediate(settings); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_345.md
        [Test, Category("AGB_345")]
        public void TimedOutAgentTaskPreservesItsPartialToolPayload()
        {
            var payload = Build(ErrorState(1));
            payload.status = ToolResultStatus.Timeout;
            var manager = new AgentTaskManager();
            var id = manager.Register(new CompletingTask(AgentTaskResult.TimedOut("deadline", payload)));
            manager.Start(id);
            var result = AgentTaskToolResultAdapter.FromSnapshot(manager.Get(id), null, "unity.compile");
            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Timeout));
            Assert.That(result.errors.Single().code, Is.EqualTo("CS1002"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_346.md
        [Test, Category("AGB_346")]
        public void RetainedCompileReportIsReadableThroughTheReportTool()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var relative = "Library/AgentBridge/mcp-task-reports/fixture-" + Guid.NewGuid().ToString("N") + ".json";
            var path = Path.Combine(root, relative);
            try
            {
                AgentTaskDurableResults.WriteAtomic(path, JsonUtil.SerializeObject(new CompileReport { diagnostics = new[] { new ToolError { code = "CS1002", message = "fixture" } } }));
                var result = new UnityReadReportTool().Execute(new UnityMcpToolContext
                { ProjectRoot = root, TempRoot = "Temp/AgentBridge", RawArgsJson = new JObject { ["reportPath"] = relative, ["jsonPointer"] = "/diagnostics" }.ToString() }, null);
                Assert.That(result.Success, Is.True, result.Summary);
                Assert.That(JObject.Parse(result.MetricsObjectJson)["items"][0].Value<string>("code"), Is.EqualTo("CS1002"));
            }
            finally { File.Delete(path); }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_347.md
        [Test, Category("AGB_347")]
        public void NativeTerminalResultRetainsDiagnosticsAndRejectsLateCancellation()
        {
            var manager = new AgentTaskManager(idFactory: () => new AgentTaskId("fixture-native-" + Guid.NewGuid().ToString("N")));
            var payload = Build(ErrorState(1));
            var id = manager.Register(new CompletingTask(AgentTaskResult.DomainFailure("compiler errors", payload)));
            manager.Start(id);
            var path = Path.Combine(AgentTaskDurableResults.Root, "agent-task-results", id.Value + ".json");
            try
            {
                AgentTaskDurableResults.Save(new AgentTaskControlResponse { Success = true, AgentTaskId = id.Value, Snapshot = manager.Get(id), ResultPayloadJson = JsonUtil.SerializeObject(payload) });
                var restored = AgentTaskDurableResults.Read(id.Value, true);
                Assert.That(restored.State, Is.EqualTo("Completed"));
                Assert.That(restored.Cancellation, Is.EqualTo(AgentTaskCancellationDisposition.AlreadyTerminal));
                Assert.That(JObject.Parse(restored.ResultPayloadJson)["errors"][0].Value<string>("code"), Is.EqualTo("CS1002"));
            }
            finally { File.Delete(path); }
        }

        private static CompileOperationState ErrorState(int count)
        {
            var state = new CompileOperationState { commandId = "fixture", targetEpoch = 12, completedEpoch = 12 };
            for (var i = 0; i < count; i++) state.compilerErrors.Add(new CompilerErrorRecord { code = "CS1002", message = "; expected", file = "Assets/Broken.cs", line = 4, column = 9 });
            return state;
        }

        private static ToolResult Build(CompileOperationState state) => (ToolResult)typeof(UnityCompileOperationManager)
            .GetMethod("BuildCompileResult", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { state, new CompileLifecycleState { compileEpoch = 12 } });

        private sealed class CompletingTask : IAgentTask
        {
            private readonly AgentTaskResult result;
            public CompletingTask(AgentTaskResult result) { this.result = result; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) => context.Complete(result);
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }
    }
}
