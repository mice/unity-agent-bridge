using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class AgentTaskDurablePayloadRegressionTests
{
    private const string Evidence = "C:\\runs\\new\\tools\\ \"quoted\" 中文 😀\r\n\t\b\f";

    private static string Payload() => JsonSerializer.Serialize(new
    {
        success = false, status = "failed", summary = "Compiler failure",
        errors = new[] { new { code = "CS1029", message = Evidence } },
        metricsObjectJson = JsonSerializer.Serialize(new { contractVersion = "compile.v1", errorCount = 1, executableIdentity = Evidence })
    });

    private static string SaveAndReadDurable(string root)
    {
        var codec = new AgentTaskControlCodec();
        var terminal = new AgentTaskControlResponse
        {
            Success = false, AgentTaskId = "compile-fixture", State = "Completed",
            ResultKind = "DomainFailure", ResultReason = Evidence, ResultPayloadJson = Payload()
        };
        var path = Path.Combine(root, "retained.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, response = codec.SerializeResponse(terminal) }));
        using var disk = JsonDocument.Parse(File.ReadAllText(path));
        var restored = codec.DeserializeResponse(disk.RootElement.GetProperty("response").GetString()!);
        Assert.AreEqual(terminal.ResultReason, restored.ResultReason, "Terminal reason must preserve literal paths and controls.");
        Assert.AreEqual(terminal.ResultPayloadJson, restored.ResultPayloadJson, "Opaque nested JSON must survive durable codec recovery exactly.");
        using var business = JsonDocument.Parse(restored.ResultPayloadJson);
        using var metrics = JsonDocument.Parse(business.RootElement.GetProperty("metricsObjectJson").GetString()!);
        Assert.AreEqual(Evidence, metrics.RootElement.GetProperty("executableIdentity").GetString());
        return codec.SerializeResponse(restored);
    }

    [TestMethod]
    public void DurableCompilePayloadPreservesNestedJsonWindowsPathsAndUnicodeExactly()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(AgentTaskDurablePayloadRegressionTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SaveAndReadDurable(root);
            var codec = new AgentTaskControlCodec();
            foreach (var value in new[] { "C:\\runs\\new\\tools\\", "quoted ending\"", "中文 😀\r\n\t\b\f", "literal \\u4e2d" })
            {
                var request = new AgentTaskControlRequest { Operation = "create", Payload = value, TaskType = "compile" };
                var restored = codec.DeserializeRequest(codec.SerializeRequest(request));
                Assert.AreEqual(value, restored.Payload, "Request payload must retain trailing slash/quote and all escape forms.");
                Assert.AreEqual("compile", restored.TaskType, "An escaped closing quote must not consume the following field.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task RecoveredCompilePayloadReachesAdapterAndStandardResultWithoutDiagnosticLoss()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(AgentTaskDurablePayloadRegressionTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task? worker = null;
        try
        {
            var wire = SaveAndReadDurable(root);
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.53f1");
            var paths = new QueuePaths(root, Path.Combine(root, "queue"));
            CommandStore.EnsureQueueDirectories(paths);
            File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"),
                JsonSerializer.Serialize(new { heartbeatUtc = DateTime.UtcNow.ToString("O"), projectPath = root, currentStage = "idle" }));
            new McpTaskBindingStore(root).Save(new(root, "mcp-fixture", "compile-fixture", "original-command"));
            worker = Task.Run(async () =>
            {
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var commandFile = Directory.GetFiles(paths.InboxDirectory, "*.json").FirstOrDefault();
                    if (commandFile is null) { await Task.Delay(10, cancellation.Token); continue; }
                    JsonObject command;
                    try { command = JsonNode.Parse(File.ReadAllText(commandFile))!.AsObject(); }
                    catch (IOException) { await Task.Delay(10, cancellation.Token); continue; }
                    var id = command["commandId"]!.GetValue<string>();
                    var envelope = new JsonObject { ["success"] = true, ["status"] = "success", ["commandId"] = id,
                        ["tool"] = "unity.agent_task_control", ["metricsObjectJson"] = wire };
                    var destination = Path.Combine(paths.OutboxDirectory, id + ".result.json");
                    File.WriteAllText(destination + ".tmp", envelope.ToJsonString());
                    File.Move(destination + ".tmp", destination);
                    return;
                }
            }, cancellation.Token);
            var adapter = new McpTaskAdapter(new ExternalBridgeClient(), paths, root);
            var projection = await adapter.GetAsync("mcp-fixture", cancellation.Token);
            Assert.AreEqual(Payload(), projection["resultPayloadJson"]!.GetValue<string>());
            var result = McpToolResultConverter.Convert(projection);
            var content = result.StructuredContent!.Value;
            Assert.IsTrue(result.IsError);
            Assert.AreEqual("CS1029", content.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.AreEqual(Evidence, content.GetProperty("errors")[0].GetProperty("message").GetString());
            Assert.AreEqual("compile.v1", content.GetProperty("metrics").GetProperty("contractVersion").GetString());
            Assert.AreEqual(Evidence, content.GetProperty("metrics").GetProperty("executableIdentity").GetString());
            Assert.AreEqual("mcp-fixture", content.GetProperty("taskId").GetString());
            await worker;
        }
        finally
        {
            cancellation.Cancel();
            if (worker is not null) { try { await worker; } catch (OperationCanceledException) { } }
            Directory.Delete(root, true);
        }
    }
}
