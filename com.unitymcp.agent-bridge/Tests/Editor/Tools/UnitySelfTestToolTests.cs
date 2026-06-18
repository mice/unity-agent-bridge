using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.BuiltInPlugins.TestRunner;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnitySelfTestToolTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_079.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_079")]
        public void SelfTestMetrics_SerializeObject_ReturnsJson()
        {
            var metrics = new AgentBridgeSelfTestMetrics
            {
                suiteVersion = "1.0",
                overallPassed = true,
                caseCount = 1,
                passedCount = 1,
                failedCount = 0,
                cancelledCount = 0,
                cases = new[]
                {
                    new AgentBridgeSelfTestCaseResult
                    {
                        id = "ping",
                        scenario = "basic",
                        tool = "unity.ping",
                        expectedStatus = ToolResultStatus.Success,
                        actualStatus = ToolResultStatus.Success,
                        passed = true,
                        summary = "pong",
                        durationMs = 12,
                        metricsObjectJson = "{}"
                    },
                },
            };

            var json = JsonUtil.SerializeObject(metrics);

            Assert.That(json, Does.Contain("\"suiteVersion\":\"1.0\""));
            Assert.That(json, Does.Contain("\"caseCount\":1"));
            Assert.That(json, Does.Contain("\"id\":\"ping\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_080.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_080")]
        public void SelfTestRunner_AllImmediateCasesPassing_CompletesWithSuccess()
        {
            var runner = CreateRunner(new StubFacade
            {
                Handler = command =>
                {
                    if (command.tool == "unity.__self_test_nonexistent__")
                    {
                        return CreateResult(command, ToolResultStatus.Unsupported, "unsupported");
                    }

                    if (command.tool == "unity.run_static_method" &&
                        command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal))
                    {
                        return CreateResult(command, ToolResultStatus.InvalidArgs, "not allowed");
                    }

                    return CreateResult(command, ToolResultStatus.Success, command.tool + " ok");
                },
            });

            var command = CreateCommand("agb.selftest.080");
            var result = runner.StartOrResume(command, new SelfTestRunOptions
            {
                includeEditModeCase = false,
                includeDiagnosticCase = false,
                continueOnFailure = true,
                timeoutMs = 1000,
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.success, Is.True);
            Assert.That(result.metricsObjectJson, Does.Contain("\"caseCount\":6"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"passedCount\":6"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_081.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_081")]
        public void SelfTestRunner_FailureWithContinueOnFailure_CompletesRemainingCases()
        {
            var calls = new List<string>();
            var runner = CreateRunner(new StubFacade
            {
                Handler = command =>
                {
                    calls.Add(command.tool);
                    if (command.tool == "unity.project.get_info")
                    {
                        return CreateResult(command, ToolResultStatus.Failed, "project info failed");
                    }

                    if (command.tool == "unity.__self_test_nonexistent__")
                    {
                        return CreateResult(command, ToolResultStatus.Unsupported, "unsupported");
                    }

                    if (command.tool == "unity.run_static_method" &&
                        command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal))
                    {
                        return CreateResult(command, ToolResultStatus.InvalidArgs, "not allowed");
                    }

                    return CreateResult(command, ToolResultStatus.Success, "ok");
                },
            });

            var result = runner.StartOrResume(CreateCommand("agb.selftest.081"), new SelfTestRunOptions
            {
                includeEditModeCase = false,
                includeDiagnosticCase = false,
                continueOnFailure = true,
                timeoutMs = 1000,
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(calls.Count, Is.EqualTo(6));
            Assert.That(result.metricsObjectJson, Does.Contain("\"failedCount\":1"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_082.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_082")]
        public void SelfTestRunner_StaticMethodNotAllowed_InvalidArgs_MarksCasePassed()
        {
            var runner = CreateRunner(new StubFacade
            {
                Handler = command =>
                {
                    if (command.tool == "unity.run_static_method" &&
                        command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal))
                    {
                        return CreateResult(command, ToolResultStatus.InvalidArgs, "not allowed");
                    }

                    return CreateResult(command, command.tool == "unity.__self_test_nonexistent__" ? ToolResultStatus.Unsupported : ToolResultStatus.Success, "ok");
                },
            });

            var result = runner.StartOrResume(CreateCommand("agb.selftest.082"), new SelfTestRunOptions
            {
                includeEditModeCase = false,
                includeDiagnosticCase = false,
                continueOnFailure = true,
                timeoutMs = 1000,
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.metricsObjectJson, Does.Contain("\"expectedStatus\":\"invalid_args\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"actualStatus\":\"invalid_args\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"passed\":true"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_083.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_083")]
        public void SelfTestRunner_UnsupportedTool_MarksCasePassed()
        {
            var runner = CreateRunner(new StubFacade
            {
                Handler = command =>
                {
                    if (command.tool == "unity.__self_test_nonexistent__")
                    {
                        return CreateResult(command, ToolResultStatus.Unsupported, "unsupported");
                    }

                    return CreateResult(command, command.tool == "unity.run_static_method" &&
                        command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal)
                        ? ToolResultStatus.InvalidArgs
                        : ToolResultStatus.Success, "ok");
                },
            });

            var result = runner.StartOrResume(CreateCommand("agb.selftest.083"), new SelfTestRunOptions
            {
                includeEditModeCase = false,
                includeDiagnosticCase = false,
                continueOnFailure = true,
                timeoutMs = 1000,
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.metricsObjectJson, Does.Contain("\"tool\":\"unity.__self_test_nonexistent__\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"actualStatus\":\"unsupported\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_084.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_084")]
        public void SelfTestTool_InvalidArgs_ReturnsFailure()
        {
            var tool = new UnitySelfTestTool(CreateRunner(new StubFacade()));

            var result = tool.Execute(new AgentToolContext
            {
                Command = CreateCommand("agb.selftest.084"),
                RawArgsJson = "[]",
                Settings = ScriptableObject.CreateInstance<AgentBridgeSettings>(),
            }, NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_085.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_085")]
        public void SelfTestTool_Descriptor_UsesExpectedContract()
        {
            var tool = new UnitySelfTestTool(CreateRunner(new StubFacade()));

            Assert.That(tool.Descriptor.Name, Is.EqualTo("unity.agent_bridge_self_test"));
            Assert.That(tool.Descriptor.Description, Is.EqualTo("Run the Unity Agent Bridge self-test suite."));
            Assert.That(tool.Descriptor.AllowedModes, Is.EqualTo(ToolExecutionModes.Edit));
            Assert.That(tool.Descriptor.SideEffect, Is.EqualTo(ToolSideEffect.RunsUserCode));
            Assert.That(tool.Descriptor.MayTriggerDomainReload, Is.True);
            Assert.That(tool.Descriptor.ArgsSchemaPath, Is.EqualTo("Documentation~/schemas/unity.agent_bridge_self_test.args.schema.json"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_086.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_086")]
        public void AgentToolRegistry_ManualRegister_FindsSelfTestTool()
        {
            var registry = new AgentToolRegistry();
            var tool = new UnitySelfTestTool(CreateRunner(new StubFacade()));

            registry.Discover();
            registry.Register(tool);

            Assert.That(registry.TryGetTool("unity.agent_bridge_self_test", out var found), Is.True);
            Assert.That(found, Is.SameAs(tool));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_087.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_087")]
        public void SelfTestRunner_OperationCase_ReturnsRunningThenSuccess()
        {
            var queue = CreateQueueRoot("AGB_087");
            var commandId = string.Empty;
            var facade = new StubFacade
            {
                Handler = command =>
                {
                    if (command.tool == "unity.run_editmode_tests")
                    {
                        commandId = command.commandId;
                        return CreateResult(command, ToolResultStatus.Running, "running");
                    }

                    return CreateResult(command, command.tool == "unity.__self_test_nonexistent__" ? ToolResultStatus.Unsupported :
                        command.tool == "unity.run_static_method" && command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal)
                            ? ToolResultStatus.InvalidArgs
                            : ToolResultStatus.Success, "ok");
                },
            };

            var runner = CreateRunner(facade, queue);
            var command = CreateCommand("agb.selftest.087");

            var first = runner.StartOrResume(command, new SelfTestRunOptions
            {
                includeEditModeCase = true,
                includeDiagnosticCase = false,
                continueOnFailure = true,
                timeoutMs = 2000,
            }, NoOpAgentCancellation.Instance);

            Assert.That(first.status, Is.EqualTo(ToolResultStatus.Running));
            Assert.That(commandId, Is.Not.Empty);

            WriteOutboxResult(queue.OutboxDirectory, commandId, CreateResult(
                new AgentCommand { commandId = commandId, tool = "unity.run_editmode_tests" },
                ToolResultStatus.Success,
                "editmode passed"));

            runner.Tick();
            var second = ReadOutboxResult(queue.OutboxDirectory, command.commandId);

            Assert.That(second.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(second.metricsObjectJson, Does.Contain("\"tool\":\"unity.run_editmode_tests\""));
            Assert.That(second.metricsObjectJson, Does.Contain("\"actualStatus\":\"success\""));
            Assert.That(File.Exists(Path.Combine(queue.OutboxDirectory, command.commandId + ".result.json")), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_089.md
        [Test]
        [Category("AGB_SelfTest")]
        [Category("AGB_089")]
        public void SelfTestRunner_SuiteTimeout_RecordsTimeoutAndCancelled()
        {
            var queue = CreateQueueRoot("AGB_089");
            var now = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);
            var facade = new StubFacade
            {
                Handler = command =>
                {
                    if (command.tool == "unity.run_editmode_tests")
                    {
                        return CreateResult(command, ToolResultStatus.Running, "running");
                    }

                    return CreateResult(command, command.tool == "unity.__self_test_nonexistent__" ? ToolResultStatus.Unsupported :
                        command.tool == "unity.run_static_method" && command.rawArgsJson.Contains("\"System.Console\"", StringComparison.Ordinal)
                            ? ToolResultStatus.InvalidArgs
                            : ToolResultStatus.Success, "ok");
                },
            };

            var runner = CreateRunner(facade, queue, () => now);
            var command = CreateCommand("agb.selftest.089");

            var first = runner.StartOrResume(command, new SelfTestRunOptions
            {
                includeEditModeCase = true,
                includeDiagnosticCase = true,
                continueOnFailure = true,
                timeoutMs = 10,
            }, NoOpAgentCancellation.Instance);

            Assert.That(first.status, Is.EqualTo(ToolResultStatus.Running));

            now = now.AddMilliseconds(20);
            runner.Tick();

            var final = ReadOutboxResult(queue.OutboxDirectory, command.commandId);

            Assert.That(final.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(final.metricsObjectJson, Does.Contain("\"actualStatus\":\"timeout\""));
            Assert.That(final.metricsObjectJson, Does.Contain("\"actualStatus\":\"cancelled\""));
        }

        private static AgentBridgeSelfTestRunner CreateRunner(StubFacade facade, AgentCommandQueue queue = null, Func<DateTime> utcNowProvider = null)
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.maxToolDurationMs = 60000;
            queue ??= CreateQueueRoot(Guid.NewGuid().ToString("N"));
            return new AgentBridgeSelfTestRunner(facade, queue, settings, null, utcNowProvider);
        }

        private static AgentCommandQueue CreateQueueRoot(string suffix)
        {
            var root = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge.SelfTest", suffix);
            Directory.CreateDirectory(root);
            return new AgentCommandQueue(root, "Temp/AgentBridge");
        }

        private static AgentCommand CreateCommand(string commandId)
        {
            return new AgentCommand
            {
                schemaVersion = JsonUtil.CurrentSchemaVersion,
                commandId = commandId,
                tool = "unity.agent_bridge_self_test",
                timeoutMs = 5000,
                createdAt = "2026-06-08T00:00:00Z",
                rawArgsJson = "{}",
            };
        }

        private static ToolResult CreateResult(AgentCommand command, string status, string summary)
        {
            return new ToolResult
            {
                schemaVersion = JsonUtil.CurrentSchemaVersion,
                commandId = command.commandId,
                tool = command.tool,
                success = string.Equals(status, ToolResultStatus.Success, StringComparison.Ordinal),
                status = status,
                startedAt = "2026-06-08T00:00:00Z",
                finishedAt = "2026-06-08T00:00:01Z",
                durationMs = 10,
                summary = summary,
                metricsObjectJson = "{}",
            };
        }

        private static void WriteOutboxResult(string outboxDirectory, string commandId, ToolResult result)
        {
            Directory.CreateDirectory(outboxDirectory);
            File.WriteAllText(Path.Combine(outboxDirectory, commandId + ".result.json"), JsonUtil.SerializeResult(result));
        }

        private static ToolResult ReadOutboxResult(string outboxDirectory, string commandId)
        {
            var path = Path.Combine(outboxDirectory, commandId + ".result.json");
            var raw = File.ReadAllText(path);
            return new ToolResult
            {
                schemaVersion = raw.Contains("\"schemaVersion\":\"1.0\"", StringComparison.Ordinal) ? JsonUtil.CurrentSchemaVersion : string.Empty,
                commandId = commandId,
                tool = raw.Contains("\"tool\":\"unity.agent_bridge_self_test\"", StringComparison.Ordinal) ? "unity.agent_bridge_self_test" : string.Empty,
                success = raw.Contains("\"success\":true", StringComparison.Ordinal),
                status = raw.Contains("\"status\":\"success\"", StringComparison.Ordinal)
                    ? ToolResultStatus.Success
                    : raw.Contains("\"status\":\"failed\"", StringComparison.Ordinal)
                        ? ToolResultStatus.Failed
                        : raw.Contains("\"status\":\"running\"", StringComparison.Ordinal)
                            ? ToolResultStatus.Running
                            : string.Empty,
                summary = raw,
                metricsObjectJson = raw
            };
        }

        private sealed class StubFacade : IUnityToolFacade
        {
            public Func<AgentCommand, ToolResult> Handler { get; set; }

            public ToolResult Execute(AgentCommand command, IAgentCancellation cancellation)
            {
                return Handler?.Invoke(command) ?? CreateResult(command, ToolResultStatus.Success, "ok");
            }

            public IReadOnlyList<ToolDescriptor> ListTools()
            {
                return Array.Empty<ToolDescriptor>();
            }

            public bool TryGetTool(string toolName, out IAgentTool tool)
            {
                tool = null;
                return false;
            }
        }
    }
}
