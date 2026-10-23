using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpTestDiagnosticReportTests
{
    // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_273.md
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void OrdinaryAndTaskResultsRetainDiagnosticsAndOfflineReport(bool succeeded)
    {
        var root = Path.Combine(Path.GetTempPath(), "agb-test-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = Path.Combine(root, "Library", "AgentBridge", "editor-test-reports", "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            const string marker = "AGBMP_EXPECTED_DIAGNOSTIC:project-A";
            const string stack = "at Fixture.DiagnosticFailure() in Assets/Fixture.cs:42";
            var business = new JsonObject
            {
                ["tests"] = new JsonArray(new JsonObject { ["fullName"] = "Fixture.DiagnosticFailure", ["outcome"] = succeeded ? "Passed" : "Failed",
                    ["message"] = succeeded ? "" : marker, ["stackTrace"] = succeeded ? "" : stack }),
                ["errors"] = succeeded ? new JsonArray() : new JsonArray(new JsonObject { ["code"] = "UNITY_TEST_FAILURE", ["message"] = marker, ["stackTrace"] = stack }),
                ["reportPath"] = Path.GetRelativePath(root, report).Replace('\\', '/')
            };
            File.WriteAllText(report, business.ToJsonString());
            var response = new JsonObject
            {
                ["taskId"] = "mcp-diagnostic", ["state"] = "Completed", ["resultKind"] = succeeded ? "Succeeded" : "DomainFailure",
                ["success"] = succeeded, ["projectId"] = "project-A", ["projectPath"] = root, ["editorSessionId"] = "old-editor", ["connectionId"] = "original-connection",
                ["metrics"] = business.DeepClone()
            };
            var paths = new QueuePaths(root, "queue", "project-A", "old-editor");
            var retained = new BridgeResultHistory(paths).Save("task-diagnostic", new ToolResultEnvelope(response.ToJsonString(), "completed", false));
            File.Delete(report);
            // A replacement Editor does not invalidate the original terminal payload/report.
            var offline = new BridgeResultHistory(new QueuePaths(root, "queue", "project-A", "new-editor")).Read("task-diagnostic")!;
            var taskResult = McpToolResultConverter.Convert(JsonNode.Parse(offline.RawJson)!.AsObject());
            var task = taskResult.StructuredContent!.Value;
            Assert.AreEqual(!succeeded, taskResult.IsError);
            Assert.AreEqual("project-A", task.GetProperty("projectId").GetString());
            Assert.AreEqual("old-editor", task.GetProperty("editorSessionId").GetString());
            var retainedPath = task.GetProperty("reportPath").GetString()!;
            Assert.IsTrue(File.Exists(Path.GetFullPath(retainedPath, root)));
            Assert.AreEqual(business.ToJsonString(), new BridgeResultHistory(paths).ReadRetainedReport(retainedPath));
            if (!succeeded)
            {
                Assert.AreEqual(marker, task.GetProperty("errors")[0].GetProperty("message").GetString());
                Assert.AreEqual(stack, task.GetProperty("errors")[0].GetProperty("stackTrace").GetString());
            }
            // The ordinary legacy envelope exposes the same generic fields without task-specific parsing.
            var ordinary = McpToolResultConverter.Convert(new JsonObject { ["success"] = succeeded, ["status"] = succeeded ? "success" : "failed",
                ["metricsObjectJson"] = business.ToJsonString(), ["projectId"] = "project-A" }).StructuredContent!.Value;
            Assert.AreEqual("project-A", ordinary.GetProperty("projectId").GetString());
            Assert.AreEqual(business["reportPath"]!.GetValue<string>(), ordinary.GetProperty("reportPath").GetString());
            if (!succeeded) Assert.AreEqual(marker, ordinary.GetProperty("errors")[0].GetProperty("message").GetString());
        }
        finally { Directory.Delete(root, true); }
    }
}
