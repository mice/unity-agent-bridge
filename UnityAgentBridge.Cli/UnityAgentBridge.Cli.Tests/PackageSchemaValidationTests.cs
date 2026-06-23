using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class PackageSchemaValidationTests
{
    [TestMethod]
    public void PackageSchemas_AreWellFormedAndExposeTokenFriendlyContracts()
    {
        var schemaRoot = Path.Combine(
            ResolveRepoRoot(),
            "com.unitymcp.agent-bridge",
            "Documentation~",
            "schemas");

        var expectedFiles = new[]
        {
            "tool-result.schema.json",
            "unity.get_hierarchy.args.schema.json",
            "unity.get_hierarchy.metrics.schema.json",
            "unity.get_hierarchy.payload.schema.json",
            "unity.get_gameobject_component_info.args.schema.json",
            "unity.get_gameobject_component_info.metrics.schema.json",
            "unity.get_gameobject_component_info.payload.schema.json",
            "unity.read_report.args.schema.json",
            "unity.read_report.metrics.schema.json",
            "unity.read_report.payload.schema.json"
        };

        foreach (var fileName in expectedFiles)
        {
            var fullPath = Path.Combine(schemaRoot, fileName);
            Assert.IsTrue(File.Exists(fullPath), $"Missing schema file: {fullPath}");
            Assert.IsNotNull(JObject.Parse(File.ReadAllText(fullPath)));
        }

        var toolResultSchema = LoadSchema(schemaRoot, "tool-result.schema.json");
        Assert.AreEqual("object", toolResultSchema.Value<string>("type"));
        Assert.AreEqual(false, toolResultSchema.Value<bool>("additionalProperties"));
        Assert.AreEqual("object", toolResultSchema["properties"]?["metrics"]?.Value<string>("type"));

        var hierarchyArgs = LoadSchema(schemaRoot, "unity.get_hierarchy.args.schema.json");
        Assert.AreEqual(4, hierarchyArgs["properties"]?["maxDepth"]?["default"]?.Value<int>());
        Assert.AreEqual(150, hierarchyArgs["properties"]?["limit"]?["default"]?.Value<int>());
        Assert.AreEqual(false, hierarchyArgs["properties"]?["includeComponents"]?["default"]?.Value<bool>());

        var hierarchyMetrics = LoadSchema(schemaRoot, "unity.get_hierarchy.metrics.schema.json");
        Assert.AreEqual("hierarchy.v2", hierarchyMetrics["properties"]?["contractVersion"]?["const"]?.Value<string>());
        Assert.AreEqual(3, hierarchyMetrics["$defs"]?["followUp"]?["properties"]?["options"]?["maxItems"]?.Value<int>());
        var componentTypeOptions = hierarchyMetrics["$defs"]?["componentSummary"]?["properties"]?["type"]?["type"]?.Values<string>().ToArray();
        CollectionAssert.AreEqual(new[] { "string", "null" }, componentTypeOptions);

        var componentInfoMetrics = LoadSchema(schemaRoot, "unity.get_gameobject_component_info.metrics.schema.json");
        Assert.AreEqual("gameobject_component_info.v1", componentInfoMetrics["properties"]?["contractVersion"]?["const"]?.Value<string>());
        Assert.AreEqual(3, componentInfoMetrics["$defs"]?["followUp"]?["properties"]?["options"]?["maxItems"]?.Value<int>());
        Assert.AreEqual("object", componentInfoMetrics["$defs"]?["followUpOption"]?["properties"]?["args"]?.Value<string>("type"));

        var readReportArgs = LoadSchema(schemaRoot, "unity.read_report.args.schema.json");
        CollectionAssert.AreEqual(new[] { "reportPath" }, readReportArgs["required"]!.Values<string>().ToArray());
        Assert.AreEqual(100, readReportArgs["properties"]?["limit"]?["default"]?.Value<int>());
        Assert.AreEqual(65536, readReportArgs["properties"]?["maxBytes"]?["default"]?.Value<int>());

        var readReportMetrics = LoadSchema(schemaRoot, "unity.read_report.metrics.schema.json");
        Assert.AreEqual("report_read.v1", readReportMetrics["properties"]?["contractVersion"]?["const"]?.Value<string>());
        Assert.AreEqual(262144, readReportMetrics["properties"]?["maxBytes"]?["maximum"]?.Value<int>());
        Assert.AreEqual(true, readReportMetrics["required"]!.Values<string>().Contains("items"));
        Assert.AreEqual(true, readReportMetrics["required"]!.Values<string>().Contains("value"));
    }

    private static JObject LoadSchema(string schemaRoot, string fileName)
    {
        return JObject.Parse(File.ReadAllText(Path.Combine(schemaRoot, fileName)));
    }

    private static string ResolveRepoRoot()
    {
        for (var cursor = Directory.GetCurrentDirectory(); !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (Directory.Exists(Path.Combine(cursor, "UnityAgentBridge.Cli")) &&
                Directory.Exists(Path.Combine(cursor, "com.unitymcp.agent-bridge")) &&
                Directory.Exists(Path.Combine(cursor, "openspec")))
            {
                return cursor;
            }

            var parent = Directory.GetParent(cursor)?.FullName;
            var workbenchRoot = string.IsNullOrWhiteSpace(parent) ? null : Path.Combine(parent, "unity-agent-bridge-workbench");
            if (Directory.Exists(Path.Combine(cursor, "UnityAgentBridge.Cli")) &&
                !string.IsNullOrWhiteSpace(workbenchRoot) &&
                Directory.Exists(Path.Combine(cursor, "com.unitymcp.agent-bridge")) &&
                Directory.Exists(Path.Combine(workbenchRoot, "openspec")))
            {
                return cursor;
            }
        }

        throw new DirectoryNotFoundException("Repository root could not be resolved.");
    }
}
