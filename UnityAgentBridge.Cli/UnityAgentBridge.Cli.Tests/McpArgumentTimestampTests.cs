using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using UnityAgentBridge.Mcp;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public class McpArgumentTimestampTests
{
    [DataTestMethod]
    [DataRow("2026-10-22T16:35:33.8040696Z")]
    [DataRow("2026-10-23T00:35:33.8040696+08:00")]
    public void IsoTimestampRemainsValidStringBusinessArgument(string timestamp)
    {
        const string schema = """{"type":"object","properties":{"timestamp":{"type":"string","minLength":20}},"required":["timestamp"],"additionalProperties":false}""";
        var arguments = JsonConvert.SerializeObject(new { timestamp });
        McpArgumentValidator.ValidateOrThrow("unity_fixture_timestamp", schema, arguments);
    }

    [TestMethod]
    public void TimestampEnumUsesExactStringInsteadOfEquivalentInstant()
    {
        const string schema = """{"type":"object","properties":{"timestamp":{"enum":["2026-10-22T16:35:33.8040696Z"]}},"required":["timestamp"]}""";
        McpArgumentValidator.ValidateOrThrow("unity_fixture_timestamp", schema,
            """{"timestamp":"2026-10-22T16:35:33.8040696Z"}""");
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow(
            "unity_fixture_timestamp", schema, """{"timestamp":"2026-10-23T00:35:33.8040696+08:00"}"""));
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow(
            "unity_fixture_timestamp", schema, """{"timestamp":"2026-10-22T16:35:33.8040697Z"}"""));
    }

    [TestMethod]
    public void StringTimestampSchemaStillRejectsNumericBusinessValue()
    {
        const string schema = """{"type":"object","properties":{"timestamp":{"type":"string"}},"required":["timestamp"]}""";
        Assert.ThrowsException<McpArgumentValidationException>(() => McpArgumentValidator.ValidateOrThrow(
            "unity_fixture_timestamp", schema, """{"timestamp":639282837338040696}"""));
    }
}
