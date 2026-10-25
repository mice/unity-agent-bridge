using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class SchedulerTransportTests
{
    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_373.md
    [TestMethod, TestCategory("AGBM_373")]
    public void PreflightModeRejectionPreservesEnvelopeDiagnosticInsteadOfMetricsOnly()
    {
        var response = UnityAgentBridge.Mcp.McpTaskAdapter.Unwrap("{\"schemaVersion\":\"1.0\",\"tool\":\"unity.agent_task_control\",\"success\":false,\"status\":\"blocked\",\"summary\":\"Requires Edit Mode.\",\"errors\":[{\"code\":\"AGENTBRIDGE_TOOL_MODE_BLOCKED\",\"message\":\"Requires Edit Mode.\"}],\"metrics\":{\"commandName\":\"unity.agent_task_control\",\"currentMode\":\"Play Mode\",\"allowedModes\":[\"Edit Mode\"]}}");
        Assert.AreEqual("AGENTBRIDGE_TOOL_MODE_BLOCKED", response["errorCode"]!.GetValue<string>());
        Assert.AreEqual("Requires Edit Mode.", response["summary"]!.GetValue<string>());
        Assert.AreEqual("Play Mode", response["metrics"]!["currentMode"]!.GetValue<string>());
        Assert.IsFalse(response["success"]!.GetValue<bool>());
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_374.md
    [TestMethod, TestCategory("AGBM_374")]
    public void TypedControlMetricsStillPreserveStableTaskAndScheduling()
    {
        var response = UnityAgentBridge.Mcp.McpTaskAdapter.Unwrap("{\"success\":true,\"metrics\":{\"version\":1,\"success\":true,\"agentTaskId\":\"original\",\"state\":\"Created\",\"schedulingJson\":\"{\\\"schemaVersion\\\":1,\\\"stage\\\":\\\"queued\\\"}\"}}");
        Assert.AreEqual("original", response["agentTaskId"]!.GetValue<string>());
        Assert.AreEqual("Created", response["state"]!.GetValue<string>());
        Assert.AreEqual("queued", response["scheduling"]!["stage"]!.GetValue<string>());
    }

    // TestRecord: Documentation~/test_records/AGBM_337.md
    [TestMethod, TestCategory("AGBM_337")]
    public void CommandEnvelope_RetainsOriginForEditorOwnedAdmission()
    {
        var raw = AgentCommandEnvelope.Build("dispatch", "unity.compile", 120000, "{}", 1, "project", "editor", "C1");
        var parsed = JsonUtil.ExtractCommand(raw);
        Assert.IsNotNull(parsed.Command);
        Assert.AreEqual("C1", parsed.Command.connectionId);
        Assert.AreEqual("project", parsed.Command.projectId);
        Assert.AreEqual("editor", parsed.Command.editorSessionId);
        using var document = System.Text.Json.JsonDocument.Parse(raw);
        Assert.AreEqual(document.RootElement.GetProperty("createdAt").GetString(), parsed.Command.createdAt);
    }

    // TestRecord: Documentation~/test_records/AGBM_338.md
    [TestMethod, TestCategory("AGBM_338")]
    public void LegacyCommandEnvelope_RemainsReadableWithoutConnectionField()
    {
        var raw = AgentCommandEnvelope.Build("legacy", "unity.ping", 5000, "{}");
        Assert.IsNull(JObject.Parse(raw)["connectionId"]);
        Assert.IsNull(JsonUtil.ExtractCommand(raw).Command.connectionId);
    }

    // TestRecord: Documentation~/test_records/AGBM_339.md
    [TestMethod, TestCategory("AGBM_339")]
    public void NativeCreateCodec_RetainsOriginalSubmissionAndOpaquePayload()
    {
        var codec = new AgentTaskControlCodec();
        const string timestamp = "2026-10-24T12:34:56.1234567+08:00";
        const string payload = "{\"filter\":\"a\\\\b\\\"c\"}";
        var raw = codec.SerializeRequest(new AgentTaskControlRequest
        {
            Operation = "create", TaskType = "compile", ProjectIdentity = "project", Payload = payload,
            TimeoutMs = 120000, SubmittedAtUtc = timestamp
        });
        var parsed = codec.DeserializeRequest(raw);
        Assert.AreEqual(timestamp, parsed.SubmittedAtUtc);
        Assert.AreEqual(payload, parsed.Payload);
        Assert.AreEqual(120000, parsed.TimeoutMs);
        var legacy = codec.DeserializeRequest("{\"version\":1,\"operation\":\"create\",\"taskType\":\"compile\",\"payload\":\"{}\",\"timeoutMs\":1000}");
        Assert.IsTrue(string.IsNullOrEmpty(legacy.SubmittedAtUtc));
    }
}
