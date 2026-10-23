using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class McpProbeRunnerTests
{
    [TestMethod]
    public async Task ProbeRunner_EmitsToolNamesAndPingResultShape()
    {
        var projectRoot = CreateUnityProject();
        var previousProjectPath = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH");
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        var bridgeResponder = StartBridgeResponder(projectRoot);
        try
        {
            var json = await McpProbeRunner.RunAsync(CancellationToken.None);
            var parsed = JObject.Parse(json);

            var listedToolCount = parsed.Value<int>("listedToolCount");
            var toolNames = parsed["toolNames"] as JArray;
            Assert.IsNotNull(toolNames);
            Assert.AreEqual(listedToolCount, toolNames.Count);
            Assert.IsTrue(listedToolCount > 0);
            Assert.IsNotNull(parsed["pingResult"]);
            Assert.IsNotNull(parsed["pingResult"]!["structuredContent"]);
            Assert.IsNotNull(parsed["pingResult"]!["isError"]);
            Assert.AreEqual("success", parsed["echoResult"]!["structuredContent"]!.Value<string>("status"));
            Assert.AreEqual("success", parsed["healthResult"]!["structuredContent"]!.Value<string>("status"));
            Assert.AreEqual("success", parsed["consoleResult"]!["structuredContent"]!.Value<string>("status"));
            Assert.AreEqual("success", parsed["projectInfoResult"]!["structuredContent"]!.Value<string>("status"));
        }
        finally
        {
            try { bridgeResponder.Dispose(); }
            finally
            {
                Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", previousProjectPath);
                Directory.Delete(projectRoot, true);
            }
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
            {"version":1,"tools":[{"pluginId":"com.unitymcp.builtin.project-info","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.ProjectInfo","bridgeTool":"unity.project.get_info","mcpName":"unity_project_get_info","title":"Unity Project Info","description":"Report Unity project, scene, and editor state.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}]}
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
        private DateTime _lastHeartbeatUtc;
        private DateTime? _heartbeatPublishBlockedSince;

        public BridgeResponder(QueuePaths queuePaths)
        {
            _queuePaths = queuePaths;
            WriteFreshStatus();
            _task = Task.Run(RespondAsync);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
            finally { _cts.Dispose(); }
        }

        private async Task RespondAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                if (DateTime.UtcNow - _lastHeartbeatUtc >= TimeSpan.FromMilliseconds(250)) WriteFreshStatus();
                var commandPath = Directory.GetFiles(_queuePaths.InboxDirectory, "*.json").FirstOrDefault();
                if (commandPath is not null)
                {
                    JObject command;
                    try { command = JObject.Parse(await File.ReadAllTextAsync(commandPath, _cts.Token)); }
                    catch (Exception exception) when (exception is IOException or Newtonsoft.Json.JsonReaderException)
                    {
                        await Task.Delay(50, _cts.Token);
                        continue; // Retry a writer-owned or incomplete command before applying any effect.
                    }
                    var tool = command.Value<string>("tool") ?? string.Empty;
                    if (tool is "unity.ping" or "unity.get_console" or "unity.project.get_info")
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
                            ["summary"] = tool == "unity.ping" ? "Pong." : tool == "unity.get_console" ? "Console entries collected." : "Project info collected.",
                            ["projectPath"] = _queuePaths.ProjectPath
                        };
                        await File.WriteAllTextAsync(resultPath + ".fixture.tmp", result.ToString(Newtonsoft.Json.Formatting.None), _cts.Token);
                        File.Move(resultPath + ".fixture.tmp", resultPath);
                        File.Delete(commandPath);
                    }
                }

                await Task.Delay(50, _cts.Token);
            }
        }

        private void WriteFreshStatus()
        {
            Directory.CreateDirectory(_queuePaths.StatusDirectory);
            var statusPath = Path.Combine(_queuePaths.StatusDirectory, "unity_bridge_status.json");
            var status = new JObject
            {
                ["heartbeatUtc"] = DateTime.UtcNow.ToString("O"),
                ["currentStage"] = "unity.poller.idle",
                ["projectPath"] = _queuePaths.ProjectPath.Replace("\\", "/"),
                ["staleProjectBindingKind"] = "bound",
                ["staleDetectedProjectPath"] = _queuePaths.ProjectPath.Replace("\\", "/")
            };
            var temporary = statusPath + ".fixture.tmp";
            try
            {
                File.WriteAllText(temporary, status.ToString(Newtonsoft.Json.Formatting.None));
                try { File.Move(temporary, statusPath, overwrite: true); }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException &&
                    OperatingSystem.IsWindows() && (exception.HResult & 0xffff) is 5 or 32 or 33 &&
                    File.Exists(statusPath) && File.Exists(temporary))
                {
                    _heartbeatPublishBlockedSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - _heartbeatPublishBlockedSince.Value >= TimeSpan.FromSeconds(10)) throw;
                    return;
                }
                _lastHeartbeatUtc = DateTime.UtcNow;
                _heartbeatPublishBlockedSince = null;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
