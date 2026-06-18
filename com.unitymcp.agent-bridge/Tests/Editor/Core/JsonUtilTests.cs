using NUnit.Framework;
using System;
using System.Reflection;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class JsonUtilTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_001.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_001")]
        public void ExtractCommand_ValidCommand_PreservesFieldsAndRawArgs()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-001\",\"tool\":\"unity.get_console\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{\"types\":[\"error\",\"warning\"],\"count\":0}}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Command.commandId, Is.EqualTo("cmd-001"));
            Assert.That(result.Command.tool, Is.EqualTo("unity.get_console"));
            Assert.That(result.Command.timeoutMs, Is.EqualTo(1234));
            Assert.That(result.Command.createdAt, Is.EqualTo("2026-06-05T10:00:00Z"));
            Assert.That(result.Command.rawArgsJson, Is.EqualTo("{\"types\":[\"error\",\"warning\"],\"count\":0}"));
        }

        [Test]
        [Category("AGB_Core")]
        public void ExtractCommand_CSharpMcpPingEnvelope_TreatsCreatedAtAsJsonString()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"20260613_021329989_12484_000002\",\"tool\":\"unity.ping\",\"timeoutMs\":5000,\"createdAt\":\"2026-06-13T02:13:30.0056085Z\",\"args\":{}}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Command.commandId, Is.EqualTo("20260613_021329989_12484_000002"));
            Assert.That(result.Command.tool, Is.EqualTo("unity.ping"));
            Assert.That(result.Command.createdAt, Is.EqualTo("2026-06-13T02:13:30.0056085Z"));
            Assert.That(result.Command.rawArgsJson, Is.EqualTo("{}"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_002.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_002")]
        public void ExtractCommand_MissingSchemaVersion_ReturnsInvalidArgs()
        {
            var rawJson = "{\"commandId\":\"cmd-002\",\"tool\":\"unity.ping\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{}}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_REQUIRED_FIELD_MISSING"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_003.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_003")]
        public void SerializeResult_MetricsRemainJsonObject()
        {
            var result = new ToolResult
            {
                commandId = "cmd-003",
                tool = "unity.project.get_info",
                success = true,
                status = ToolResultStatus.Success,
                metricsObjectJson = "{\"x\":1}"
            };

            var json = JsonUtil.SerializeResult(result);

            Assert.That(json, Does.Contain("\"metrics\":{\"x\":1}"));
            Assert.That(json, Does.Not.Contain("\"metrics\":\"{\\\"x\\\":1}\""));
        }

        [Test]
        [Category("AGB_Core")]
        [Category("AGB_150")]
        public void SerializeResult_FollowUpMetadata_RemainsStructuredObject()
        {
            var result = new ToolResult
            {
                commandId = "cmd-follow-up",
                tool = "unity.get_hierarchy",
                success = true,
                status = ToolResultStatus.Success,
                metricsObjectJson = "{\"details\":{\"available\":true,\"reportPath\":\"Temp/AgentBridge/reports/x.json\",\"recommendedRead\":false,\"recommendedPointers\":[\"/result/nodes\"]},\"followUp\":{\"recommended\":true,\"options\":[{\"tool\":\"unity.get_hierarchy\",\"reason\":\"Inspect a subtree.\",\"args\":{\"locator\":\"currentScene#Root\"}}]}}"
            };

            var json = JsonUtil.SerializeResult(result);

            Assert.That(json, Does.Contain("\"metrics\":{\"details\":{\"available\":true"));
            Assert.That(json, Does.Contain("\"followUp\":{\"recommended\":true"));
            Assert.That(json, Does.Contain("\"options\":[{\"tool\":\"unity.get_hierarchy\""));
            Assert.That(json, Does.Not.Contain("\"metrics\":\"{\\\"details\\\""));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_151.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_151")]
        public void ToolResultMetadata_Recommended_ValidAndInvalidShapesAreEnforced()
        {
            var editorAssembly = typeof(UnityGetHierarchyTool).Assembly;
            var metadataType = editorAssembly.GetType("UnityMcp.AgentBridge.ToolResultMetadata");
            var optionType = editorAssembly.GetType("UnityMcp.AgentBridge.ToolFollowUpOption");

            Assert.That(metadataType, Is.Not.Null);
            Assert.That(optionType, Is.Not.Null);

            var optionMethod = metadataType.GetMethod("Option");
            var recommendedMethod = metadataType.GetMethod("Recommended");

            Assert.That(optionMethod, Is.Not.Null);
            Assert.That(recommendedMethod, Is.Not.Null);

            var option = optionMethod.Invoke(null, new object[] { "unity.read_report", "Read the report.", null });
            var singleOption = Array.CreateInstance(optionType, 1);
            singleOption.SetValue(option, 0);

            dynamic valid = recommendedMethod.Invoke(null, new object[] { singleOption });

            Assert.That(valid.recommended, Is.True);
            Assert.That(valid.options, Has.Length.EqualTo(1));
            Assert.That(valid.options[0].tool, Is.EqualTo("unity.read_report"));
            Assert.That(valid.options[0].reason, Is.EqualTo("Read the report."));
            Assert.That(valid.options[0].args, Is.Not.Null);

            var emptyOptions = Array.CreateInstance(optionType, 0);
            var fourOptions = Array.CreateInstance(optionType, 4);
            for (var index = 0; index < fourOptions.Length; index++)
            {
                fourOptions.SetValue(option, index);
            }

            Assert.Throws<TargetInvocationException>(() => recommendedMethod.Invoke(null, new object[] { emptyOptions }));
            Assert.Throws<TargetInvocationException>(() => recommendedMethod.Invoke(null, new object[] { null }));
            Assert.Throws<TargetInvocationException>(() => recommendedMethod.Invoke(null, new object[] { fourOptions }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_012.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_012")]
        public void ExtractCommand_EmptyJson_ReturnsInvalidArgs()
        {
            var result = JsonUtil.ExtractCommand(string.Empty);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_COMMAND_EMPTY"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_013.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_013")]
        public void ExtractCommand_InvalidTopLevelJson_ReturnsInvalidArgs()
        {
            var result = JsonUtil.ExtractCommand("[]");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_COMMAND_PARSE_FAILED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_014.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_014")]
        public void ExtractCommand_UnsupportedSchemaVersion_ReturnsUnsupported()
        {
            var rawJson = "{\"schemaVersion\":\"2.0\",\"commandId\":\"cmd-014\",\"tool\":\"unity.ping\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{}}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.Unsupported));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_SCHEMA_UNSUPPORTED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_015.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_015")]
        public void ExtractCommand_TrailingCharacters_ReturnsInvalidArgs()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-015\",\"tool\":\"unity.ping\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{}} trailing";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_COMMAND_PARSE_FAILED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_016.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_016")]
        public void ExtractCommand_TimeoutWrongType_ReturnsInvalidArgs()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-016\",\"tool\":\"unity.ping\",\"timeoutMs\":\"slow\",\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{}}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_FIELD_TYPE_INVALID"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_017.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_017")]
        public void ExtractCommand_MissingArgs_ReturnsInvalidArgs()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-017\",\"tool\":\"unity.ping\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\"}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_ARGS_OBJECT_REQUIRED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_018.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_018")]
        public void ExtractCommand_ArrayArgs_ReturnsInvalidArgs()
        {
            var rawJson = "{\"schemaVersion\":\"1.0\",\"commandId\":\"cmd-018\",\"tool\":\"unity.ping\",\"timeoutMs\":1234,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":[]}";

            var result = JsonUtil.ExtractCommand(rawJson);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.Failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_ARGS_OBJECT_REQUIRED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_019.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_019")]
        public void SerializeResult_NullResult_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => JsonUtil.SerializeResult(null));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_020.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_020")]
        public void SerializeResult_WritesCollectionsEscapesAndNullMetricsFallback()
        {
            var result = new ToolResult
            {
                schemaVersion = null,
                commandId = "cmd-020",
                tool = "unity.project.get_info",
                success = false,
                status = ToolResultStatus.Failed,
                startedAt = "2026-06-05T10:00:00Z",
                finishedAt = "2026-06-05T10:00:01Z",
                durationMs = 1000,
                summary = "line1\nline2",
                metricsObjectJson = null,
                reportPath = "Reports/out.json"
            };
            result.errors.Add(new ToolError { code = "ERR", message = "bad", file = "Assets/Test.cs", line = 7, column = 9 });
            result.warnings.Add(new ToolWarning { code = "WARN", message = "heads up" });
            result.logs.Add(new ToolLog { level = "info", message = "hello\tworld", timestamp = "2026-06-05T10:00:00Z" });
            result.changedFiles.Add("Assets/Test.cs");

            var json = JsonUtil.SerializeResult(result);

            Assert.That(json, Does.Contain("\"schemaVersion\":\"1.0\""));
            Assert.That(json, Does.Contain("\"summary\":\"line1\\nline2\""));
            Assert.That(json, Does.Contain("\"errors\":[{\"code\":\"ERR\",\"message\":\"bad\",\"file\":\"Assets/Test.cs\",\"line\":7,\"column\":9}]"));
            Assert.That(json, Does.Contain("\"warnings\":[{\"code\":\"WARN\",\"message\":\"heads up\"}]"));
            Assert.That(json, Does.Contain("\"logs\":[{\"level\":\"info\",\"message\":\"hello\\tworld\",\"timestamp\":\"2026-06-05T10:00:00Z\"}]"));
            Assert.That(json, Does.Contain("\"metrics\":{}"));
            Assert.That(json, Does.Contain("\"changedFiles\":[\"Assets/Test.cs\"]"));
            Assert.That(json, Does.Contain("\"reportPath\":\"Reports/out.json\""));
        }

    }
}
