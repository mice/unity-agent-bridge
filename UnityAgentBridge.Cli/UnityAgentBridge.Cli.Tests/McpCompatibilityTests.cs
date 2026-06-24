using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UnityAgentBridge.Cli;
using UnityAgentBridge.Cli.Commands;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpCompatibilityTests
{
    [TestMethod]
    public void CatalogMatchesFrozenNodeGoldenExactly()
    {
        var repoRoot = ResolveRepoRoot();
        var goldenPath = Path.Combine(repoRoot, "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "tools-list.golden.json");
        var golden = JObject.Parse(File.ReadAllText(goldenPath));
        var expectedTools = ((JArray)golden["tools"]!).Cast<JObject>()
            .OrderBy(tool => tool.Value<string>("name"), StringComparer.Ordinal)
            .ToArray();

        var diagnostics = new McpHostDiagnostics(
            "D:/repo/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe",
            "project-local-runtime",
            Array.Empty<string>(),
            Path.Combine(repoRoot, "UnityMCP"),
            Path.Combine(repoRoot, "UnityMCP", "Temp", "AgentBridge"),
            Path.Combine(repoRoot, "UnityMCP", "Temp", "AgentBridge", "logs"),
            Path.Combine(repoRoot, "UnityMCP", "Temp", "AgentBridge", "logs", "mcp-server.log"));
        var actualTools = McpToolCatalog.GetTools(diagnostics)
            .Select(definition => ToComparableTool(definition.ProtocolTool))
            .OrderBy(tool => tool.Value<string>("name"), StringComparer.Ordinal)
            .ToArray();

        foreach (var expectedTool in expectedTools)
        {
            var toolName = expectedTool.Value<string>("name");
            var actualTool = actualTools.SingleOrDefault(tool => string.Equals(tool.Value<string>("name"), toolName, StringComparison.Ordinal));
            Assert.IsNotNull(actualTool, $"Built-in tool '{toolName}' is missing from the MCP catalog.");
            Assert.IsTrue(JToken.DeepEquals(expectedTool, actualTool),
                $"Built-in tool mismatch for '{toolName}':{Environment.NewLine}Expected: {expectedTool}{Environment.NewLine}Actual:   {actualTool}");
        }
    }

    [TestMethod]
    public async Task StdioProtocolCasesRemainCompatibleAfterNormalization()
    {
        var projectRoot = CreateUnityProject();
        await using var server = await McpServerSession.StartAsync(projectRoot);

        var echoResponse = await server.CallToolAsync(
            "mcp_echo",
            new JObject
            {
                ["value"] = "hello",
                ["payload"] = new JObject
                {
                    ["a"] = 1
                }
            });

        var waitTimeoutResponse = await server.CallToolAsync(
            "unity_bridge_wait_result",
            new JObject
            {
                ["commandId"] = "missing-command",
                ["timeoutMs"] = 1
            });

        var missingCommandIdResponse = await server.CallToolAsync(
            "unity_bridge_wait_result",
            new JObject
            {
                ["timeoutMs"] = 1
            });

        AssertNormalizedCompatibility(
            Path.Combine(ResolveRepoRoot(), "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "mcp-valid-cases", "mcp_echo.success.json"),
            echoResponse);
        AssertNormalizedCompatibility(
            Path.Combine(ResolveRepoRoot(), "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "mcp-response-cases", "unity_bridge_wait_result.timeout.json"),
            waitTimeoutResponse);
        AssertInvalidArgsCompatibility(
            Path.Combine(ResolveRepoRoot(), "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "mcp-invalid-cases", "unity_bridge_wait_result.missing-command-id.json"),
            missingCommandIdResponse);
    }

    [TestMethod]
    public async Task DirectServiceShapesRemainCompatibleForLocalAndBridgeResponses()
    {
        var projectRoot = CreateUnityProject();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        try
        {
            var diagnostics = McpHostDiagnostics.Resolve(projectRoot) with
            {
                ResolvedCliPath = "D:/repo/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe"
            };
            McpToolRuntimeContext.QueuePaths = new ExternalBridgeClientCore.QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
            var service = new McpServerService(new ExternalBridgeClient(), diagnostics, new McpStageLogger(diagnostics.ServerLogPath));

            var echoResponse = await service.CallToolAsync(
                new ModelContextProtocol.Protocol.CallToolRequestParams
                {
                    Name = "mcp_echo",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["value"] = JsonDocument.Parse("\"hello\"").RootElement.Clone(),
                        ["payload"] = JsonDocument.Parse("""{"a":1}""").RootElement.Clone()
                    }
                },
                CancellationToken.None);

            var waitTimeoutResponse = await service.CallToolAsync(
                new ModelContextProtocol.Protocol.CallToolRequestParams
                {
                    Name = "unity_bridge_wait_result",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["commandId"] = JsonDocument.Parse("\"missing-command\"").RootElement.Clone(),
                        ["timeoutMs"] = JsonDocument.Parse("1").RootElement.Clone()
                    }
                },
                CancellationToken.None);
            var missingCommandIdResponse = await service.CallToolAsync(
                new ModelContextProtocol.Protocol.CallToolRequestParams
                {
                    Name = "unity_bridge_wait_result",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["timeoutMs"] = JsonDocument.Parse("1").RootElement.Clone()
                    }
                },
                CancellationToken.None);

            AssertNormalizedCompatibility(
                Path.Combine(ResolveRepoRoot(), "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "mcp-valid-cases", "mcp_echo.success.json"),
                echoResponse);
            AssertNormalizedCompatibility(
                Path.Combine(ResolveRepoRoot(), "Tools", "AgentBridge", "CompatibilityBaseline", "NodeMcp", "mcp-response-cases", "unity_bridge_wait_result.timeout.json"),
                waitTimeoutResponse);
            AssertServiceInvalidArgsCompatibility(missingCommandIdResponse);
        }
        finally
        {
            McpToolRuntimeContext.QueuePaths = null;
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", null);
        }
    }

    [TestMethod]
    public void CliAndMcpBothDependOnExternalBridgeClientCore()
    {
        var cliAssembly = typeof(AgentBridgeCli).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        var mcpAssembly = typeof(McpServerRuntime).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();

        CollectionAssert.Contains(cliAssembly, "UnityAgentBridge.ExternalBridgeClientCore");
        CollectionAssert.Contains(mcpAssembly, "UnityAgentBridge.ExternalBridgeClientCore");
        CollectionAssert.DoesNotContain(mcpAssembly, "UnityAgentBridge.Cli");
    }

    private static JObject ToComparableTool(ModelContextProtocol.Protocol.Tool tool)
    {
        return new JObject
        {
            ["name"] = tool.Name,
            ["title"] = tool.Title,
            ["description"] = tool.Description,
            ["inputSchema"] = JToken.Parse(tool.InputSchema.GetRawText()),
            ["execution"] = new JObject
            {
                ["taskSupport"] = "forbidden"
            }
        };
    }

    private static void AssertNormalizedCompatibility(string baselinePath, ModelContextProtocol.Protocol.CallToolResult response)
    {
        var baseline = JObject.Parse(File.ReadAllText(baselinePath));
        var expected = NormalizeResponse((JObject)baseline["response"]!);
        var actual = NormalizeResponse(ToComparableResponse(response));

        Assert.IsTrue(JToken.DeepEquals(expected, actual),
            $"Normalized MCP response mismatch for '{Path.GetFileName(baselinePath)}'.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual}");
    }

    private static void AssertNormalizedCompatibility(string baselinePath, JObject response)
    {
        var baseline = JObject.Parse(File.ReadAllText(baselinePath));
        var expected = NormalizeResponse((JObject)baseline["response"]!);
        var actual = NormalizeResponse((JObject)response.DeepClone());

        Assert.IsTrue(JToken.DeepEquals(expected, actual),
            $"Normalized MCP response mismatch for '{Path.GetFileName(baselinePath)}'.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual}");
    }

    private static JObject NormalizeResponse(JObject response)
    {
        var clone = (JObject)response.DeepClone();
        NormalizeToken(clone["structuredContent"]);
        if (clone["content"] is JArray content)
        {
            foreach (var item in content.OfType<JObject>())
            {
                if (item["text"] is JValue textValue && textValue.Value is string text)
                {
                    var parsed = JObject.Parse(text);
                    NormalizeToken(parsed);
                    item["text"] = parsed.ToString(Newtonsoft.Json.Formatting.None);
                }
            }
        }

        return clone;
    }

    private static void NormalizeToken(JToken? token)
    {
        if (token is not JObject obj)
        {
            return;
        }

        obj.Remove("commandId");
        obj.Remove("resolvedCliPath");
        obj.Remove("queueRoot");
        obj.Remove("statusPath");
        obj.Remove("heartbeatAgeMs");
    }

    private static JObject ToComparableResponse(ModelContextProtocol.Protocol.CallToolResult response)
    {
        var comparable = new JObject
        {
            ["content"] = JArray.FromObject(response.Content.Select(block => new JObject
            {
                ["type"] = "text",
                ["text"] = ((ModelContextProtocol.Protocol.TextContentBlock)block).Text
            }).ToArray()),
            ["isError"] = response.IsError
        };

        if (response.StructuredContent is { } structuredContent)
        {
            comparable["structuredContent"] = JObject.Parse(structuredContent.GetRawText());
        }

        return comparable;
    }

    private static void AssertInvalidArgsCompatibility(string baselinePath, JObject response)
    {
        var baseline = JObject.Parse(File.ReadAllText(baselinePath));
        var expected = (JObject)baseline["response"]!;
        var actual = new JObject
        {
            ["content"] = response["content"]!.DeepClone(),
            ["isError"] = response["isError"]!.DeepClone()
        };

        if (response["structuredContent"] is not null)
        {
            actual["structuredContent"] = response["structuredContent"]!.DeepClone();
        }

        Assert.IsTrue(JToken.DeepEquals(expected, actual),
            $"Invalid-args MCP response mismatch for '{Path.GetFileName(baselinePath)}'.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual}");
    }

    private static void AssertServiceInvalidArgsCompatibility(ModelContextProtocol.Protocol.CallToolResult response)
    {
        Assert.AreEqual(true, response.IsError);
        Assert.AreEqual(1, response.Content.Count);
        var text = ((ModelContextProtocol.Protocol.TextContentBlock)response.Content[0]).Text;
        StringAssert.Contains(text, "commandId");
        StringAssert.Contains(text, "invalid");
    }

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeMcpCompatTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(root, "Temp", "AgentBridge", "status"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }

    private static string ResolveRepoRoot()
    {
        for (var cursor = Directory.GetCurrentDirectory(); !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (Directory.Exists(Path.Combine(cursor, "UnityAgentBridge.Cli")) &&
                Directory.Exists(Path.Combine(cursor, "Tools")) &&
                Directory.Exists(Path.Combine(cursor, "openspec")))
            {
                return cursor;
            }

            var parent = Directory.GetParent(cursor)?.FullName;
            var workbenchRoot = string.IsNullOrWhiteSpace(parent) ? null : Path.Combine(parent, "unity-agent-bridge-workbench");
            if (Directory.Exists(Path.Combine(cursor, "UnityAgentBridge.Cli")) &&
                !string.IsNullOrWhiteSpace(workbenchRoot) &&
                Directory.Exists(Path.Combine(workbenchRoot, "Tools")) &&
                Directory.Exists(Path.Combine(workbenchRoot, "openspec")))
            {
                return workbenchRoot;
            }
        }

        throw new DirectoryNotFoundException("Repository root could not be resolved.");
    }

    private sealed class McpServerSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamReader _stdout;
        private readonly StreamWriter _stdin;
        private int _nextRequestId = 1;

        private McpServerSession(Process process)
        {
            _process = process;
            _stdout = process.StandardOutput;
            _stdin = process.StandardInput;
        }

        public static async Task<McpServerSession> StartAsync(string projectRoot)
        {
            var assemblyPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "UnityAgentBridge.Cli",
                "bin",
                "Debug",
                "net8.0",
                "win-x64",
                "UnityAgentBridge.Cli.dll"));
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{assemblyPath}\" mcp-server",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.Environment["UNITY_AGENT_BRIDGE_PROJECT_PATH"] = projectRoot;
            process.Start();

            var session = new McpServerSession(process);
            await session.InitializeAsync();
            return session;
        }

        public async Task<JObject> CallToolAsync(string toolName, JObject arguments)
        {
            var response = await SendRequestAsync(
                "tools/call",
                new JObject
                {
                    ["name"] = toolName,
                    ["arguments"] = arguments
                });

            return (JObject?)response["result"] ?? throw new AssertFailedException($"tools/call '{toolName}' did not return a result payload: {response}");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            finally
            {
                _stdin.Dispose();
                _process.Dispose();
            }
        }

        private async Task InitializeAsync()
        {
            var response = await SendRequestAsync(
                "initialize",
                new JObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "mcp-compatibility-tests",
                        ["version"] = "1.0.0"
                    }
                });

            Assert.IsNotNull(response["result"], $"initialize did not return a result payload: {response}");
            await SendNotificationAsync("notifications/initialized", new JObject());
        }

        private async Task<JObject> SendRequestAsync(string method, JObject parameters)
        {
            var requestId = _nextRequestId++;
            await WriteMessageAsync(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = method,
                ["params"] = parameters
            });

            while (true)
            {
                var response = await ReadMessageAsync();
                if (response["id"]?.Value<int>() != requestId)
                {
                    continue;
                }

                if (response["error"] is JObject error)
                {
                    throw new AssertFailedException($"MCP request '{method}' failed: {error}{Environment.NewLine}stderr:{Environment.NewLine}{await ReadAvailableErrorAsync()}");
                }

                return response;
            }
        }

        private Task SendNotificationAsync(string method, JObject parameters)
        {
            return WriteMessageAsync(new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            });
        }

        private async Task WriteMessageAsync(JObject message)
        {
            var json = message.ToString(Newtonsoft.Json.Formatting.None);
            await _stdin.WriteLineAsync(json);
            await _stdin.FlushAsync();
        }

        private async Task<JObject> ReadMessageAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var line = await _stdout.ReadLineAsync(cts.Token);
                if (line is null)
                {
                    var stderr = await ReadAvailableErrorAsync();
                    throw new EndOfStreamException($"Unexpected end of MCP stdio stream.{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    return JObject.Parse(line);
                }
                catch (Exception exception)
                {
                    throw new AssertFailedException($"MCP stdio response line was not valid JSON: {line}{Environment.NewLine}Parse error: {exception.Message}");
                }
            }
        }

        private async Task<string> ReadAvailableErrorAsync()
        {
            await Task.Delay(50);
            var builder = new StringBuilder();
            while (_process.StandardError.Peek() >= 0)
            {
                builder.Append((char)_process.StandardError.Read());
            }

            return builder.ToString();
        }
    }
}
