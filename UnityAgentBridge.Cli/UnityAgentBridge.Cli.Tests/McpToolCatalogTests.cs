using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;
using System.Text.Json;
using UnityMcp.AgentBridge;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class McpToolCatalogTests
{
    [TestMethod]
    public void CatalogMatchesFrozenToolNamesAndCount()
    {
        var expectedNames = new[]
        {
            "unity_project_compile",
            "unity_console_get",
            "unity_editor_get_state",
            "unity_scene_open",
            "unity_editor_ping",
            "unity_diagnostic_run",
            "unity_static_method_run",
            "mcp_echo",
            "unity_editor_list",
            "unity_editor_open",
            "unity_bridge_health",
            "unity_bridge_submit_only",
            "unity_bridge_wait_result"
        };

        var diagnostics = CreateDiagnostics(CreateUnityProject());
        var actualNames = McpToolCatalog.GetTools(diagnostics)
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actualNames);
        Assert.AreEqual(13, actualNames.Length);
        Assert.IsFalse(actualNames.Any(name => name.Contains("__", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CatalogProvidesStrictSchemasForBridgeWaitResult()
    {
        var tool = McpToolCatalog.TryGet("unity_bridge_wait_result", CreateDiagnostics(CreateUnityProject()));
        Assert.IsNotNull(tool);

        var schema = tool.ProtocolTool.InputSchema;
        Assert.AreEqual("object", schema.GetProperty("type").GetString());
        Assert.IsTrue(schema.GetProperty("additionalProperties").ValueKind is JsonValueKind.False);
        var required = schema.GetProperty("required").EnumerateArray().Select(element => element.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "commandId", "timeoutMs" }, required);
    }

    [TestMethod]
    public void CatalogProvidesStrictSchemasForUnityEditorOpen()
    {
        var tool = McpToolCatalog.TryGet("unity_editor_open", CreateDiagnostics(CreateUnityProject()));
        Assert.IsNotNull(tool);

        var schema = tool.ProtocolTool.InputSchema;
        Assert.AreEqual("object", schema.GetProperty("type").GetString());
        Assert.IsTrue(schema.GetProperty("additionalProperties").ValueKind is JsonValueKind.False);
        var required = schema.GetProperty("required").EnumerateArray().Select(element => element.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "projectPath" }, required);
        Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("allowVersionFallback", out _));
        Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("waitForBridge", out _));
        Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("bridgeReadyTimeoutMs", out _));
        Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("bridgePollIntervalMs", out _));
        Assert.IsTrue(schema.GetProperty("properties").TryGetProperty("maxRunningUnityEditors", out _));
    }

    [TestMethod]
    public void CatalogAddsDynamicPluginToolsWhenProjectCatalogExists()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(
            Path.Combine(catalogDirectory, "plugin-catalog.json"),
            """
            {"version":1,"tools":[{"pluginId":"UnityMcp.Sample","pluginVersion":"1.0.0","assemblyName":"UnityMcp.Sample","bridgeTool":"unity.fbx.scan.import_issues","mcpName":"unity_fbx_scan_import_issues","title":"Unity FBX Scan Import Issues","description":"Plugin tool.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}]}
            """);

        var diagnostics = CreateDiagnostics(projectRoot);
        var toolNames = McpToolCatalog.GetTools(diagnostics).Select(tool => tool.ProtocolTool.Name).ToArray();

        CollectionAssert.Contains(toolNames, "unity_fbx_scan_import_issues");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_info");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_get_info");
    }

    [TestMethod]
    public void CatalogAddsProjectInfoOnlyFromPluginCatalog()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(
            Path.Combine(catalogDirectory, "plugin-catalog.json"),
            """
            {"version":1,"tools":[{"pluginId":"com.unitymcp.builtin.project-info","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.ProjectInfo","bridgeTool":"unity.project.get_info","mcpName":"mcp__unity__project_get_info","title":"Unity Project Info","description":"Report Unity project, scene, and editor state.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}]}
            """);

        var diagnostics = CreateDiagnostics(projectRoot);
        var toolNames = McpToolCatalog.GetTools(diagnostics).Select(tool => tool.ProtocolTool.Name).ToArray();
        var tool = McpToolCatalog.TryGet("unity_project_get_info", diagnostics);

        CollectionAssert.Contains(toolNames, "unity_project_get_info");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_info");
        Assert.IsNotNull(tool);
        Assert.AreEqual("unity.project.get_info", tool.BridgeTool);
        Assert.IsNull(McpToolCatalog.TryGet("mcp__unity__project_get_info", diagnostics));
    }

    [TestMethod]
    public void CatalogAddsUnityQueriesOnlyFromPluginCatalogAndKeepsOpenSceneStatic()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(
            Path.Combine(catalogDirectory, "plugin-catalog.json"),
            """
            {"version":1,"tools":[
            {"pluginId":"com.unitymcp.builtin.unity-queries","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.UnityQueries","bridgeTool":"unity.assetdatabase_search","mcpName":"mcp__unity__assetdatabase_search","title":"Unity AssetDatabase Search","description":"Search assets.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.unity-queries","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.UnityQueries","bridgeTool":"unity.get_hierarchy","mcpName":"mcp__unity__get_hierarchy","title":"Unity Get Hierarchy","description":"Read hierarchy.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.unity-queries","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.UnityQueries","bridgeTool":"unity.get_gameobject_component_info","mcpName":"mcp__unity__get_gameobject_component_info","title":"Unity GameObject Component Info","description":"Read component info.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.unity-queries","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.UnityQueries","bridgeTool":"unity.get_selection_info","mcpName":"mcp__unity__get_selection_info","title":"Unity Selection Info","description":"Read selection.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.unity-queries","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.UnityQueries","bridgeTool":"unity.read_report","mcpName":"mcp__unity__read_report","title":"Unity Read Report","description":"Read report.","defaultTimeoutMs":10000,"allowedRuntimeModes":"EditAndPlay","sideEffect":"ReadsProject","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{\"reportPath\":{\"type\":\"string\"}},\"required\":[\"reportPath\"],\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}
            ]}
            """);

        var diagnostics = CreateDiagnostics(projectRoot);
        var toolNames = McpToolCatalog.GetTools(diagnostics).Select(tool => tool.ProtocolTool.Name).ToArray();
        var assetSearch = McpToolCatalog.TryGet("unity_asset_database_search", diagnostics);
        var openScene = McpToolCatalog.TryGet("unity_scene_open", diagnostics);

        CollectionAssert.Contains(toolNames, "unity_asset_database_search");
        CollectionAssert.Contains(toolNames, "unity_hierarchy_get");
        Assert.AreEqual(1, toolNames.Count(name => name == "unity_hierarchy_get"));
        Assert.IsNull(McpToolCatalog.TryGet("mcp__unity__get_hierarchy", diagnostics));
        CollectionAssert.Contains(toolNames, "unity_gameobject_component_get_info");
        CollectionAssert.Contains(toolNames, "unity_selection_get_info");
        CollectionAssert.Contains(toolNames, "unity_report_read");
        Assert.IsNotNull(assetSearch);
        Assert.AreEqual("unity.assetdatabase_search", assetSearch.BridgeTool);
        Assert.IsNotNull(openScene);
        Assert.AreEqual("unity.open_scene", openScene.BridgeTool);
    }

    [TestMethod]
    public void CatalogAddsTestRunnerOnlyFromPluginCatalog()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(
            Path.Combine(catalogDirectory, "plugin-catalog.json"),
            """
            {"version":1,"tools":[
            {"pluginId":"com.unitymcp.builtin.test-runner","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.TestRunner","bridgeTool":"unity.run_editmode_tests","mcpName":"mcp__unity__run_editmode_tests","title":"Unity Run EditMode Tests","description":"Call unity.run_editmode_tests through the Unity Agent Bridge CLI.","defaultTimeoutMs":120000,"allowedRuntimeModes":"Edit","sideEffect":"RunsUserCode","mayTriggerDomainReload":false,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"string\",\"minLength\":1}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.test-runner","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.TestRunner","bridgeTool":"unity.run_playmode_tests","mcpName":"mcp__unity__run_playmode_tests","title":"Unity Run PlayMode Tests","description":"Call unity.run_playmode_tests through the Unity Agent Bridge CLI.","defaultTimeoutMs":180000,"allowedRuntimeModes":"Edit","sideEffect":"RunsUserCode","mayTriggerDomainReload":true,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"string\",\"minLength\":1}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"},
            {"pluginId":"com.unitymcp.builtin.test-runner","pluginVersion":"1.0.0","assemblyName":"UnityMcp.BuiltInPlugins.TestRunner","bridgeTool":"unity.agent_bridge_self_test","mcpName":"mcp__unity__agent_bridge_self_test","title":"Unity Agent Bridge Self-Test","description":"Run the Agent Bridge self-test suite through the Unity Agent Bridge CLI.","defaultTimeoutMs":120000,"allowedRuntimeModes":"Edit","sideEffect":"RunsUserCode","mayTriggerDomainReload":true,"inputSchemaJson":"{\"type\":\"object\",\"properties\":{\"continueOnFailure\":{\"type\":\"boolean\"}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"}
            ]}
            """);

        var diagnostics = CreateDiagnostics(projectRoot);
        var toolNames = McpToolCatalog.GetTools(diagnostics).Select(tool => tool.ProtocolTool.Name).ToArray();
        var editMode = McpToolCatalog.TryGet("unity_tests_run_edit_mode", diagnostics);
        var playMode = McpToolCatalog.TryGet("unity_tests_run_play_mode", diagnostics);
        var selfTest = McpToolCatalog.TryGet("unity_agent_bridge_run_self_test", diagnostics);

        CollectionAssert.Contains(toolNames, "unity_tests_run_edit_mode");
        CollectionAssert.Contains(toolNames, "unity_tests_run_play_mode");
        CollectionAssert.Contains(toolNames, "unity_agent_bridge_run_self_test");
        Assert.IsNotNull(editMode);
        Assert.IsNotNull(playMode);
        Assert.IsNotNull(selfTest);
        Assert.AreEqual("unity.run_editmode_tests", editMode.BridgeTool);
        Assert.AreEqual("unity.run_playmode_tests", playMode.BridgeTool);
        Assert.AreEqual("unity.agent_bridge_self_test", selfTest.BridgeTool);
    }

    [TestMethod]
    public void CatalogPreservesBuiltInToolsWhenPluginCatalogIsInvalid()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(Path.Combine(catalogDirectory, "plugin-catalog.json"), "{not-json");

        var toolNames = McpToolCatalog.GetTools(CreateDiagnostics(projectRoot))
            .Select(tool => tool.ProtocolTool.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_info");
        CollectionAssert.Contains(toolNames, "unity_editor_ping");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_get_info");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__run_editmode_tests");
    }

    [TestMethod]
    public void CatalogDoesNotExposePluginToolsWhenProjectCatalogIsEmpty()
    {
        var projectRoot = CreateUnityProject();
        var catalogDirectory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(Path.Combine(catalogDirectory, "plugin-catalog.json"), """{"version":1,"tools":[]}""");

        var toolNames = McpToolCatalog.GetTools(CreateDiagnostics(projectRoot))
            .Select(tool => tool.ProtocolTool.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_info");
        CollectionAssert.Contains(toolNames, "unity_editor_ping");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__project_get_info");
        CollectionAssert.DoesNotContain(toolNames, "mcp__unity__run_editmode_tests");
    }

    [TestMethod]
    public void CanonicalNameMapper_UsesShippedMappingsAndValidatesFutureTools()
    {
        Assert.AreEqual("unity_editor_ping", McpToolNameMapper.ToCanonicalMcpName("unity.ping"));
        Assert.AreEqual("unity_project_get_info", McpToolNameMapper.ToCanonicalMcpName("unity.project.get_info"));
        Assert.AreEqual("unity_lua_compile", McpToolNameMapper.ToCanonicalMcpName("unity.lua.compile"));
        Assert.AreEqual("unity_fbx_scan_import_issues", McpToolNameMapper.ToCanonicalMcpName("unity.fbx.scan.import_issues"));
        Assert.IsFalse(McpToolNameMapper.TryToCanonicalMcpName("unity.sample.status", out _));
    }

    [TestMethod]
    public void ToolResultAdaptationMapsContentStructuredContentAndIsError()
    {
        var rawJson = """{"schemaVersion":"1.0","status":"timeout","success":false,"summary":"Timed out waiting."}""";
        var adapted = InvokeAdaptResult(rawJson);

        Assert.AreEqual(true, adapted.IsError);
        Assert.AreEqual(1, adapted.Content.Count);
        var text = ((TextContentBlock)adapted.Content[0]).Text;
        StringAssert.Contains(text, "\"resolvedCliPath\":\"D:/repo/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe\"");
        StringAssert.Contains(text, "\"cliMode\":\"project-local-runtime\"");
        Assert.AreEqual("timeout", adapted.StructuredContent.GetValueOrDefault().GetProperty("status").GetString());
        Assert.AreEqual("project-local-runtime", adapted.StructuredContent.GetValueOrDefault().GetProperty("cliMode").GetString());
    }

    [TestMethod]
    public async Task McpServerService_LocalEcho_WritesStageLogAndDiagnostics()
    {
        var projectRoot = CreateUnityProject();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        try
        {
            var diagnostics = McpHostDiagnostics.Resolve(projectRoot) with
            {
                ResolvedCliPath = "D:/repo/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe"
            };
            McpToolRuntimeContext.QueuePaths = new UnityAgentBridge.ExternalBridgeClientCore.QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
            var service = new McpServerService(
                new UnityAgentBridge.ExternalBridgeClientCore.ExternalBridgeClient(),
                diagnostics,
                new McpStageLogger(diagnostics.ServerLogPath));

            var result = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "mcp_echo",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["value"] = JsonDocument.Parse("\"hello\"").RootElement.Clone(),
                        ["payload"] = JsonDocument.Parse("""{"a":1}""").RootElement.Clone()
                    }
                },
                CancellationToken.None);

            Assert.AreEqual(false, result.IsError);
            var structured = JsonObject.Create(result.StructuredContent.GetValueOrDefault())!;
            Assert.AreEqual("project-local-runtime", structured["cliMode"]!.GetValue<string>());
            Assert.AreEqual("mcp.echo", structured["tool"]!.GetValue<string>());
            Assert.AreEqual(projectRoot, McpToolRuntimeContext.QueuePaths!.ProjectPath);
            StringAssert.StartsWith(McpToolRuntimeContext.QueuePaths.QueueRoot, Path.Combine(projectRoot, "Temp", "AgentBridge"));

            var logText = File.ReadAllText(diagnostics.ServerLogPath);
            StringAssert.Contains(logText, "\"stage\":\"mcp.received\"");
            StringAssert.Contains(logText, "\"stage\":\"mcp.validate\"");
            StringAssert.Contains(logText, "\"stage\":\"mcp.invoke_core\"");
            StringAssert.Contains(logText, "\"stage\":\"mcp.return_response\"");
        }
        finally
        {
            McpToolRuntimeContext.QueuePaths = null;
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", null);
        }
    }

    [TestMethod]
    public async Task McpServerService_CancelledRequest_ReturnsCancelledToolResult()
    {
        var projectRoot = CreateUnityProject();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        try
        {
            var diagnostics = McpHostDiagnostics.Resolve(projectRoot);
            McpToolRuntimeContext.QueuePaths = new UnityAgentBridge.ExternalBridgeClientCore.QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
            var service = new McpServerService(
                new UnityAgentBridge.ExternalBridgeClientCore.ExternalBridgeClient(),
                diagnostics,
                new McpStageLogger(diagnostics.ServerLogPath));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "mcp_echo",
                    Arguments = new Dictionary<string, JsonElement>()
                },
                cts.Token);

            Assert.AreEqual(true, result.IsError);
            Assert.AreEqual("cancelled", result.StructuredContent.GetValueOrDefault().GetProperty("status").GetString());
        }
        finally
        {
            McpToolRuntimeContext.QueuePaths = null;
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", null);
        }
    }

    [TestMethod]
    public async Task McpServerService_UnityEditorList_ReturnsLocalStructuredResult()
    {
        var projectRoot = CreateUnityProject();
        Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", projectRoot);
        try
        {
            var diagnostics = CreateDiagnostics(projectRoot);
            McpToolRuntimeContext.QueuePaths = new UnityAgentBridge.ExternalBridgeClientCore.QueuePaths(diagnostics.ProjectPath, diagnostics.QueueRoot);
            var service = new McpServerService(
                new UnityAgentBridge.ExternalBridgeClientCore.ExternalBridgeClient(),
                diagnostics,
                new McpStageLogger(diagnostics.ServerLogPath));

            var result = await service.CallToolAsync(
                new CallToolRequestParams
                {
                    Name = "unity_editor_list",
                    Arguments = new Dictionary<string, JsonElement>()
                },
                CancellationToken.None);

            var structured = JsonObject.Create(result.StructuredContent.GetValueOrDefault())!;
            Assert.AreEqual("unity.editor_list", structured["tool"]!.GetValue<string>());
            Assert.IsTrue(structured["editorCount"] is not null);
        }
        finally
        {
            McpToolRuntimeContext.QueuePaths = null;
            Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH", null);
        }
    }

    private static CallToolResult InvokeAdaptResult(string rawJson)
    {
        var diagnostics = CreateDiagnostics("D:/repo/UnityMCP");
        var method = typeof(McpServerService).GetMethod("AdaptResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = method.Invoke(null, new object[] { rawJson, diagnostics });
        Assert.IsNotNull(result);
        return (CallToolResult)result;
    }

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeMcpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }

    private static McpHostDiagnostics CreateDiagnostics(string projectRoot)
    {
        return new McpHostDiagnostics(
            "D:/repo/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe",
            "project-local-runtime",
            Array.Empty<string>(),
            projectRoot,
            Path.Combine(projectRoot, "Temp", "AgentBridge"),
            Path.Combine(projectRoot, "Temp", "AgentBridge", "logs"),
            Path.Combine(projectRoot, "Temp", "AgentBridge", "logs", "mcp-server.log"));
    }
}
