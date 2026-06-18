using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpProbeRunnerTests
{
    [TestMethod]
    public async Task ProbeRunner_EmitsToolNamesAndPingResultShape()
    {
        var projectRoot = CreateUnityProject();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        using var bridgeResponder = StartBridgeResponder(projectRoot);
        try
        {
            var json = await McpProbeRunner.RunAsync(CancellationToken.None);
            var parsed = JObject.Parse(json);

            Assert.AreEqual(20, parsed.Value<int>("listedToolCount"));
            Assert.IsTrue(parsed["toolNames"] is JArray toolNames && toolNames.Count == 20);
            Assert.IsNotNull(parsed["pingResult"]);
            Assert.IsNotNull(parsed["pingResult"]!["structuredContent"]);
            Assert.IsNotNull(parsed["pingResult"]!["isError"]);
            Assert.AreEqual("success", parsed["echoResult"]!["structuredContent"]!.Value<string>("status"));
            Assert.AreEqual("success", parsed["healthResult"]!["structuredContent"]!.Value<string>("status"));
            Assert.AreEqual("success", parsed["projectInfoResult"]!["structuredContent"]!.Value<string>("status"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", null);
        }
    }

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeMcpProbeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(root, "Library", "AgentBridge"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        File.WriteAllText(
            Path.Combine(root, "Library", "AgentBridge", "plugin-catalog.json"),
            """
            {"version":1,"tools":[{"pluginId":"com.unitymcp.builtin.project-info","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.ProjectInfo","bridgeTool":"unity.project.get_info","mcpName":"mcp__unity__project_get_info","title":"Unity Project Info","description":"Report Unity project, scene, and editor state.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}]}
            """);
        return root;
    }

    private static BridgeResponder StartBridgeResponder(string projectRoot)
    {
        var queuePaths = new QueuePaths(projectRoot, QueuePaths.DefaultQueueRoot);
        CommandStore.EnsureQueueDirectories(queuePaths);
        return new BridgeResponder(queuePaths);
    }

    private sealed class BridgeResponder : IDisposable
    {
        private readonly QueuePaths _queuePaths;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        public BridgeResponder(QueuePaths queuePaths)
        {
            _queuePaths = queuePaths;
            _task = Task.Run(RespondAsync);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _task.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }

            _cts.Dispose();
        }

        private async Task RespondAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                var commandPath = Directory.GetFiles(_queuePaths.InboxDirectory, "*.json").FirstOrDefault();
                if (commandPath is not null)
                {
                    var command = JObject.Parse(await File.ReadAllTextAsync(commandPath, _cts.Token));
                    var tool = command.Value<string>("tool") ?? string.Empty;
                    if (tool is "unity.ping" or "unity.project.get_info")
                    {
                        var commandId = command.Value<string>("id") ?? Path.GetFileNameWithoutExtension(commandPath);
                        var resultPath = Path.Combine(_queuePaths.OutboxDirectory, commandId + ".result.json");
                        var result = new JObject
                        {
                            ["schemaVersion"] = "1.0",
                            ["commandId"] = commandId,
                            ["tool"] = tool,
                            ["success"] = true,
                            ["status"] = "success",
                            ["summary"] = tool == "unity.ping" ? "Pong." : "Project info collected.",
                            ["projectPath"] = _queuePaths.ProjectPath
                        };
                        await File.WriteAllTextAsync(resultPath, result.ToString(Newtonsoft.Json.Formatting.None), _cts.Token);
                        File.Delete(commandPath);
                    }
                }

                await Task.Delay(50, _cts.Token);
            }
        }
    }
}
