using System.Text.Json.Nodes;

namespace UnityAgentBridge.Cli.Tests;

internal static class VerifiedAnalysisCatalogFixture
{
    public static void Install(string projectRoot)
    {
        const string schema = "{\"type\":\"object\",\"properties\":{\"problemId\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256},\"evidenceIds\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":16,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":256}},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":90000}},\"required\":[\"problemId\",\"evidenceIds\"],\"additionalProperties\":false}";
        var tools = new JsonArray();
        foreach (var asset in new[] { false, true })
        {
            tools.Add(new JsonObject
            {
                ["pluginId"] = "com.unitymcp.pi-agent", ["pluginVersion"] = "0.1.0",
                ["assemblyName"] = "UnityMcp.PiAgent.Plugin",
                ["bridgeTool"] = asset ? "unity.analysis.asset_problem" : "unity.analysis.compile_failure",
                ["mcpName"] = asset ? "unity_analysis_asset_problem" : "unity_analysis_compile_failure",
                ["title"] = "Verified analysis", ["description"] = "Fixture for the enabled optional analysis provider.",
                ["defaultTimeoutMs"] = 90000, ["allowedRuntimeModes"] = "Edit", ["sideEffect"] = "ReadsProject",
                ["mayTriggerDomainReload"] = false, ["inputSchemaJson"] = schema
            });
        }
        var directory = Path.Combine(projectRoot, "Library", "AgentBridge");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin-catalog.json"), new JsonObject { ["version"] = 1, ["tools"] = tools }.ToJsonString());
    }
}
