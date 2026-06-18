using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.Cli.Commands;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class CommandSpecParserTests
{
    [TestMethod]
    public void CompileTimeoutMapsToUnityCompile()
    {
        var parsed = CommandSpecParser.Parse(["compile", "--timeout-ms", "60000"]);

        Assert.AreEqual("unity.compile", parsed.Spec.Tool);
        Assert.AreEqual(60000, parsed.Spec.TimeoutMs);
        AssertJson("{}", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void RunStaticCombinedMapsTypeAndMethod()
    {
        var parsed = CommandSpecParser.Parse(["run-static", "Namespace.Type.Method", "--parameters", "{}"]);

        Assert.AreEqual("unity.run_static_method", parsed.Spec.Tool);
        Assert.AreEqual(60000, parsed.Spec.TimeoutMs);
        AssertJson("""{"typeName":"Namespace.Type","methodName":"Method","parameters":{}}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void TestEditStructuredRejectsWildcards()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["test-edit", "--test-name", "*Foo*"]));

        StringAssert.Contains(ex.Message, "--test-name does not support wildcard characters");
    }

    [TestMethod]
    public void SelfTestFlagsMapBooleans()
    {
        var parsed = CommandSpecParser.Parse(["self-test", "--no-editmode", "--no-diagnostic", "--stop-on-failure", "--timeout-ms", "120000"]);

        Assert.AreEqual("unity.agent_bridge_self_test", parsed.Spec.Tool);
        AssertJson("""{"includeEditModeCase":false,"includeDiagnosticCase":false,"continueOnFailure":false,"timeoutMs":120000}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void AssetDatabaseSearchMapsStructuredArgs()
    {
        var parsed = CommandSpecParser.Parse(["assetdatabase_search", "--query", "t:Prefab Player", "--folder", "Assets", "--offset", "5", "--limit", "10", "--include-details"]);

        Assert.AreEqual("unity.assetdatabase_search", parsed.Spec.Tool);
        AssertJson("""{"query":"t:Prefab Player","folders":["Assets"],"offset":5,"limit":10,"includeDetails":true}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void AssetDatabaseSearchRejectsZeroLimit()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["assetdatabase_search", "--query", "t:Scene", "--limit", "0"]));

        StringAssert.Contains(ex.Message, "--limit must be in the range 1..200.");
    }

    [TestMethod]
    public void GetEditorStateMapsToUnityTool()
    {
        var parsed = CommandSpecParser.Parse(["get_editor_state", "--timeout-ms", "15000"]);

        Assert.AreEqual("unity.get_editor_state", parsed.Spec.Tool);
        Assert.AreEqual(15000, parsed.Spec.TimeoutMs);
        AssertJson("{}", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void OpenSceneMapsStructuredArgs()
    {
        var parsed = CommandSpecParser.Parse([
            "open_scene",
            "--scene-path", "Assets/Scenes/AppMain.unity",
            "--mode", "additive",
            "--set-active", "false",
            "--save-modified-scenes", "true"
        ]);

        Assert.AreEqual("unity.open_scene", parsed.Spec.Tool);
        AssertJson("""{"scenePath":"Assets/Scenes/AppMain.unity","mode":"additive","setActive":false,"saveModifiedScenes":true}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void OpenSceneRejectsUnsupportedMode()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["open_scene", "--scene-path", "Assets/Scenes/AppMain.unity", "--mode", "merge"]));

        StringAssert.Contains(ex.Message, "--mode must be one of: single, additive.");
    }

    [TestMethod]
    public void GetHierarchyMapsStructuredArgs()
    {
        var parsed = CommandSpecParser.Parse([
            "get_hierarchy",
            "--locator", "Assets/Scenes/AppMain.unity#Canvas/Button",
            "--max-depth", "3",
            "--limit", "25",
            "--include-components"
        ]);

        Assert.AreEqual("unity.get_hierarchy", parsed.Spec.Tool);
        AssertJson("""{"locator":"Assets/Scenes/AppMain.unity#Canvas/Button","maxDepth":3,"limit":25,"includeComponents":true}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void GetHierarchyRejectsOutOfRangeLimit()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["get_hierarchy", "--limit", "5001"]));

        StringAssert.Contains(ex.Message, "--limit must be in the range 1..5000.");
    }

    [TestMethod]
    public void ReadReportMapsStructuredArgs()
    {
        var parsed = CommandSpecParser.Parse([
            "read_report",
            "--report-path", "Temp/AgentBridge/reports/get_hierarchy_cmd.json",
            "--json-pointer", "/result/nodes",
            "--offset", "10",
            "--limit", "25",
            "--max-bytes", "4096"
        ]);

        Assert.AreEqual("unity.read_report", parsed.Spec.Tool);
        AssertJson("""{"reportPath":"Temp/AgentBridge/reports/get_hierarchy_cmd.json","jsonPointer":"/result/nodes","offset":10,"limit":25,"maxBytes":4096}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void ReadReportRequiresReportPath()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["read_report", "--limit", "5"]));

        StringAssert.Contains(ex.Message, "--report-path is required.");
    }

    [TestMethod]
    public void GetSelectionInfoMapsIncludeDetailsFlag()
    {
        var parsed = CommandSpecParser.Parse(["get_selection_info", "--include-details"]);

        Assert.AreEqual("unity.get_selection_info", parsed.Spec.Tool);
        AssertJson("""{"includeDetails":true}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void GetGameObjectComponentInfoMapsStructuredArgs()
    {
        var parsed = CommandSpecParser.Parse([
            "get_gameobject_component_info",
            "--locator", "currentScene#Canvas/Button",
            "--component-name", "UnityEngine.UI.Button",
            "--component-index", "1",
            "--property-mode", "serialized",
            "--property-limit", "25",
            "--array-element-limit", "5",
            "--string-max-length", "128"
        ]);

        Assert.AreEqual("unity.get_gameobject_component_info", parsed.Spec.Tool);
        AssertJson("""{"locator":"currentScene#Canvas/Button","componentName":"UnityEngine.UI.Button","componentIndex":1,"propertyMode":"serialized","propertyLimit":25,"arrayElementLimit":5,"stringMaxLength":128}""", parsed.Spec.ArgsJson);
    }

    [TestMethod]
    public void GetGameObjectComponentInfoRejectsInvalidPropertyMode()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["get_gameobject_component_info", "--property-mode", "summary"]));

        StringAssert.Contains(ex.Message, "--property-mode must be one of: debug, serialized.");
    }

    [TestMethod]
    public void GetGameObjectComponentInfoRejectsNegativePropertyLimit()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["get_gameobject_component_info", "--property-limit", "-1"]));

        StringAssert.Contains(ex.Message, "--property-limit must be in the range 0..1000.");
    }

    [TestMethod]
    public void GlobalOutputDefaultsToJson()
    {
        var parsed = CommandSpecParser.Parse(["compile"]);

        Assert.AreEqual("json", parsed.Global.OutputFormat);
    }

    [TestMethod]
    public void GlobalOutputAcceptsText()
    {
        var parsed = CommandSpecParser.Parse(["compile", "--output", "text"]);

        Assert.AreEqual("text", parsed.Global.OutputFormat);
    }

    [TestMethod]
    public void GlobalOutputRejectsUnsupportedValue()
    {
        var ex = Assert.ThrowsException<CommandValidationException>(() =>
            CommandSpecParser.Parse(["compile", "--output", "xml"]));

        StringAssert.Contains(ex.Message, "--output must be one of: json, text.");
    }

    private static void AssertJson(string expectedJson, string actualJson)
    {
        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(expectedJson), JToken.Parse(actualJson)), $"Expected {expectedJson}, got {actualJson}");
    }
}
