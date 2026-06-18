using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class UnityToolFacadeTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_032.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_032")]
        public void Execute_MissingTool_ReturnsUnsupported()
        {
            var facade = CreateFacade();

            var result = facade.Execute(CreateCommand("unity.missing"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Unsupported));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_033.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_033")]
        public void Execute_ToolReturnsSuccess_PreservesSuccess()
        {
            var facade = CreateFacade(new DelegateTool("unity.success", (_, __) => new ToolResult { success = true, status = ToolResultStatus.Success, summary = "ok" }));

            var result = facade.Execute(CreateCommand("unity.success"), NoOpAgentCancellation.Instance);

            Assert.That(result.success, Is.True);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_034.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_034")]
        public void Execute_ToolReturnsFailed_PreservesFailed()
        {
            var facade = CreateFacade(new DelegateTool("unity.failed", (_, __) => new ToolResult { success = false, status = ToolResultStatus.Failed, summary = "failed" }));

            var result = facade.Execute(CreateCommand("unity.failed"), NoOpAgentCancellation.Instance);

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_035.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_035")]
        public void Execute_ToolReturnsBlocked_PreservesBlocked()
        {
            var facade = CreateFacade(new DelegateTool("unity.blocked", (_, __) => new ToolResult { success = false, status = ToolResultStatus.Blocked, summary = "blocked" }));

            var result = facade.Execute(CreateCommand("unity.blocked"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Blocked));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_036.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_036")]
        public void Execute_ToolReturnsPending_PreservesPending()
        {
            var facade = CreateFacade(new DelegateTool("unity.pending", (_, __) => new ToolResult { success = false, status = ToolResultStatus.Pending, summary = "pending" }));

            var result = facade.Execute(CreateCommand("unity.pending"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Pending));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_037.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_037")]
        public void Execute_ToolReturnsRunning_PreservesRunning()
        {
            var facade = CreateFacade(new DelegateTool("unity.running", (_, __) => new ToolResult { success = false, status = ToolResultStatus.Running, summary = "running" }));

            var result = facade.Execute(CreateCommand("unity.running"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Running));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_038.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_038")]
        public void Execute_ToolReturnsResuming_PreservesResuming()
        {
            var facade = CreateFacade(new DelegateTool("unity.resuming", (_, __) => new ToolResult { success = false, status = ToolResultStatus.Resuming, summary = "resuming" }));

            var result = facade.Execute(CreateCommand("unity.resuming"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Resuming));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_039.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_039")]
        public void Execute_ToolReturnsInvalidArgs_PreservesInvalidArgs()
        {
            var facade = CreateFacade(new DelegateTool("unity.invalid", (_, __) => ToolResult.InvalidArgs("BAD_ARGS", "bad args")));

            var result = facade.Execute(CreateCommand("unity.invalid"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_040.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_040")]
        public void Execute_ExternalCancellation_ReturnsCancelled()
        {
            var facade = CreateFacade(new DelegateTool("unity.cancelled", (_, __) => new ToolResult { success = true, status = ToolResultStatus.Success, summary = "should not reach" }));

            var result = facade.Execute(CreateCommand("unity.cancelled"), new ThrowingCancellation());

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Cancelled));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_041.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_041")]
        public void Execute_ElapsedPastEffectiveTimeout_ReturnsTimeoutAndTruncationWarning()
        {
            var clock = new MutableClock(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
            var facade = CreateFacade(new DelegateTool("unity.timeout", (_, __) =>
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(2);
                return new ToolResult { success = true, status = ToolResultStatus.Success, summary = "late" };
            }), 1000, clock);

            var result = facade.Execute(CreateCommand("unity.timeout", 5000), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Timeout));
            Assert.That(result.warnings, Has.Some.Matches<ToolWarning>(warning => warning.code == "AGENTBRIDGE_TIMEOUT_TRUNCATED"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_042.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_042")]
        public void Execute_ToolThrows_ReturnsException()
        {
            var facade = CreateFacade(new DelegateTool("unity.exception", (_, __) => throw new InvalidOperationException("boom")));

            var result = facade.Execute(CreateCommand("unity.exception"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Exception));
            Assert.That(result.errors, Has.Count.EqualTo(1));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_070.md
        [Test]
        [Category("AGB_Facade")]
        [Category("AGB_070")]
        public void Execute_ToolReceivesNonNullSettings()
        {
            AgentBridgeSettings capturedSettings = null;
            var facade = CreateFacade(new DelegateTool("unity.capture_settings", (context, __) =>
            {
                capturedSettings = context.Settings;
                return new ToolResult { success = true, status = ToolResultStatus.Success, summary = "ok" };
            }));

            var result = facade.Execute(CreateCommand("unity.capture_settings"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(capturedSettings, Is.Not.Null);
        }

        [Test]
        [Category("AGB_Facade")]
        public void Execute_EditOnlyToolInPlayMode_ReturnsBlockedWithoutExecutingTool()
        {
            var executed = false;
            var facade = CreateFacade(
                new DelegateTool("unity.edit_only", (_, __) =>
                {
                    executed = true;
                    return new ToolResult { success = true, status = ToolResultStatus.Success, summary = "ok" };
                }, ToolExecutionModes.Edit),
                runtimeMode: UnityRuntimeMode.PlayMode);

            var result = facade.Execute(CreateCommand("unity.edit_only"), NoOpAgentCancellation.Instance);

            Assert.That(executed, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Blocked));
            Assert.That(result.errors.First().code, Is.EqualTo("AGENTBRIDGE_TOOL_MODE_BLOCKED"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"commandName\":\"unity.edit_only\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"currentMode\":\"Play Mode\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"allowedModes\":[\"Edit Mode\"]"));
        }

        [Test]
        [Category("AGB_Facade")]
        public void Execute_BothModeToolInStablePlayMode_RunsTool()
        {
            var executed = false;
            var facade = CreateFacade(
                new DelegateTool("unity.both_modes", (_, __) =>
                {
                    executed = true;
                    return new ToolResult { success = true, status = ToolResultStatus.Success, summary = "ok" };
                }, ToolExecutionModes.EditAndPlay),
                runtimeMode: UnityRuntimeMode.PlayMode);

            var result = facade.Execute(CreateCommand("unity.both_modes"), NoOpAgentCancellation.Instance);

            Assert.That(executed, Is.True);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
        }

        [Test]
        [Category("AGB_Facade")]
        public void Execute_BothModeToolInTransitionMode_ReturnsBlocked()
        {
            var executed = false;
            var facade = CreateFacade(
                new DelegateTool("unity.transition_blocked", (_, __) =>
                {
                    executed = true;
                    return new ToolResult { success = true, status = ToolResultStatus.Success, summary = "ok" };
                }, ToolExecutionModes.EditAndPlay),
                runtimeMode: UnityRuntimeMode.EnteringPlayMode);

            var result = facade.Execute(CreateCommand("unity.transition_blocked"), NoOpAgentCancellation.Instance);

            Assert.That(executed, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Blocked));
            Assert.That(result.metricsObjectJson, Does.Contain("\"currentMode\":\"Entering Play Mode\""));
        }

        private static UnityToolFacade CreateFacade(
            IAgentTool tool = null,
            int maxToolDurationMs = 300000,
            MutableClock clock = null,
            UnityRuntimeMode runtimeMode = UnityRuntimeMode.EditMode)
        {
            var registry = new AgentToolRegistry();
            if (tool != null)
            {
                registry.Register(tool);
            }

            var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
            settings.maxToolDurationMs = maxToolDurationMs;
            var utcNowProvider = clock == null ? (Func<DateTime>)null : (() => clock.UtcNow);
            return new UnityToolFacade(registry, settings, null, utcNowProvider, new StubRuntimeModeProvider(runtimeMode));
        }

        private static AgentCommand CreateCommand(string toolName, int timeoutMs = 5000)
        {
            return new AgentCommand
            {
                schemaVersion = "1.0",
                commandId = toolName + ".cmd",
                tool = toolName,
                timeoutMs = timeoutMs,
                createdAt = "2026-06-05T10:00:00Z",
                rawArgsJson = "{}"
            };
        }

        private sealed class DelegateTool : IAgentTool
        {
            private readonly Func<AgentToolContext, IAgentCancellation, ToolResult> _execute;

            public DelegateTool(string name, Func<AgentToolContext, IAgentCancellation, ToolResult> execute, ToolExecutionModes allowedModes = ToolExecutionModes.EditAndPlay)
            {
                _execute = execute;
                Descriptor = new ToolDescriptor
                {
                    Name = name,
                    SchemaVersion = JsonUtil.CurrentSchemaVersion,
                    Description = "Test tool.",
                    AllowedModes = allowedModes,
                    SideEffect = ToolSideEffect.ReadsProject,
                    MayTriggerDomainReload = false
                };
            }

            public ToolDescriptor Descriptor { get; }

            public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
            {
                return _execute(context, cancellation);
            }
        }

        private sealed class ThrowingCancellation : IAgentCancellation
        {
            public bool IsCancellationRequested => true;

            public void ThrowIfCancellationRequested()
            {
                throw new OperationCanceledException("cancelled");
            }
        }

        private sealed class MutableClock
        {
            public MutableClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; set; }
        }

        private sealed class StubRuntimeModeProvider : IUnityRuntimeModeProvider
        {
            private readonly UnityRuntimeMode _mode;

            public StubRuntimeModeProvider(UnityRuntimeMode mode)
            {
                _mode = mode;
            }

            public UnityRuntimeMode GetCurrentMode()
            {
                return _mode;
            }
        }
    }
}
