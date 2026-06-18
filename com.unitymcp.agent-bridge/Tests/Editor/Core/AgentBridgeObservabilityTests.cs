using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentBridgeObservabilityTests
    {
        private string _workspaceRoot;

        [SetUp]
        public void SetUp()
        {
            _workspaceRoot = Path.Combine(Path.GetTempPath(), "AgentBridgeObservabilityTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspaceRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_workspaceRoot))
            {
                Directory.Delete(_workspaceRoot, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_094.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_094")]
        public void Paths_EnsureDirectories_CreatesStatusDirectoryAndStatusFilePath()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            var paths = new AgentBridgePaths(_workspaceRoot, settings);

            paths.EnsureDirectories();

            Assert.That(Directory.Exists(paths.StatusRoot), Is.True);
            Assert.That(paths.StatusFilePath.Replace('\\', '/'), Does.EndWith("Temp/AgentBridge/status/unity_bridge_status.json"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_095.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_095")]
        public void Queue_UpdateState_ChangesProcessingStateAtomically()
        {
            var queue = new AgentCommandQueue(_workspaceRoot, "Temp/AgentBridge");
            var commandJson = CreateCommandJson("cmd-095");
            var result = queue.Enqueue(commandJson);
            Assert.That(result.Success, Is.True);
            Assert.That(queue.TryDequeue(out var queued), Is.True);

            queue.UpdateState("cmd-095", state => state.status = ToolResultStatus.Resuming);

            Assert.That(queue.TryReadState("cmd-095", out var state), Is.True);
            Assert.That(state.status, Is.EqualTo(ToolResultStatus.Resuming));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_096.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_096")]
        public void Poller_StartStop_PublishesHeartbeatStatusFile()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pollIntervalMs = 1;
            settings.compileBackoffMs = 1;
            var paths = new AgentBridgePaths(_workspaceRoot, settings);
            paths.EnsureDirectories();
            var queue = new AgentCommandQueue(_workspaceRoot, settings.tempRoot);
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var facade = new UnityToolFacade(registry, settings, logger);
            var poller = new AgentCommandPoller(queue, facade, settings, paths, logger);

            poller.Start();
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
                var onEditorUpdate = typeof(AgentCommandPoller).GetMethod("OnEditorUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.That(onEditorUpdate, Is.Not.Null);
                onEditorUpdate.Invoke(poller, null);

                Assert.That(File.Exists(paths.StatusFilePath), Is.True);
                var json = File.ReadAllText(paths.StatusFilePath);
                Assert.That(json, Does.Contain("\"heartbeatUtc\""));
                Assert.That(json, Does.Contain("\"currentStage\":\"unity.poller.idle\""));
            }
            finally
            {
                poller.Stop();
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void Poller_StatusFile_ContainsCompileLifecycleFields()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pollIntervalMs = 1;
            settings.compileBackoffMs = 1;
            var paths = new AgentBridgePaths(_workspaceRoot, settings);
            paths.EnsureDirectories();
            var store = new CompileLifecycleStore(_workspaceRoot, settings.tempRoot);
            store.Write(new CompileLifecycleState
            {
                compileEpoch = 3,
                currentStage = "waiting_for_finish",
                lastTransition = "compile_started",
                lastTransitionAtUtc = "2026-06-14T01:00:00.0000000Z",
                activeCommandIds = { "cmd-144-status" },
                activeTargetEpochs = { 3 },
                projectPath = _workspaceRoot.Replace('\\', '/')
            });

            var queue = new AgentCommandQueue(_workspaceRoot, settings.tempRoot);
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var facade = new UnityToolFacade(registry, settings, logger);
            var poller = new AgentCommandPoller(queue, facade, settings, paths, logger);

            InvokePollerUpdate(poller);

            var json = File.ReadAllText(paths.StatusFilePath);
            Assert.That(json, Does.Contain("\"currentCompileEpoch\":3"));
            Assert.That(json, Does.Contain("\"compileLifecycleStage\":\"waiting_for_finish\""));
            Assert.That(json, Does.Contain("\"compileLastTransition\":\"compile_started\""));
            Assert.That(json, Does.Contain("\"projectPath\""));
            Assert.That(json, Does.Contain("\"staleDetectedProjectPath\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_097.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_097")]
        public void Queue_Recover_ExpiredProcessingCommand_WritesTimeoutResult()
        {
            var queue = new AgentCommandQueue(_workspaceRoot, "Temp/AgentBridge", () => new DateTime(2026, 6, 8, 10, 1, 0, DateTimeKind.Utc));
            var commandJson = CreateCommandJson("cmd-097", 1000);
            var result = queue.Enqueue(commandJson);
            Assert.That(result.Success, Is.True);
            Assert.That(queue.TryDequeue(out var queued), Is.True);

            queue.UpdateState("cmd-097", state => state.startedAt = "2026-06-08T09:59:00.0000000Z");

            var recovery = queue.Recover();

            Assert.That(recovery.Any(record => record.CommandId == "cmd-097" && record.Action == QueueRecoveryAction.TimedOut), Is.True);
            var outboxPath = Path.Combine(queue.OutboxDirectory, "cmd-097.result.json");
            Assert.That(File.Exists(outboxPath), Is.True);
            var json = File.ReadAllText(outboxPath);
            Assert.That(json, Does.Contain("\"status\":\"timeout\""));
            Assert.That(File.Exists(Path.Combine(queue.FailedDirectory, "cmd-097.json")), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_098.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_098")]
        public void Poller_ToolException_WritesTerminalResult()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pollIntervalMs = 1;
            settings.compileBackoffMs = 1;
            var paths = new AgentBridgePaths(_workspaceRoot, settings);
            paths.EnsureDirectories();
            var queue = new AgentCommandQueue(_workspaceRoot, settings.tempRoot);
            var registry = new AgentToolRegistry();
            registry.Register(new ThrowingObservabilityTool());
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var facade = new UnityToolFacade(registry, settings, logger);
            var poller = new AgentCommandPoller(queue, facade, settings, paths, logger);

            var enqueue = queue.Enqueue(CreateCommandJson("cmd-098", 1000, "unity.observability.throw"));
            Assert.That(enqueue.Success, Is.True);

            InvokePollerUpdate(poller);

            var outboxPath = Path.Combine(queue.OutboxDirectory, "cmd-098.result.json");
            Assert.That(File.Exists(outboxPath), Is.True);
            var json = File.ReadAllText(outboxPath);
            Assert.That(json, Does.Contain("\"status\":\"exception\""));
            Assert.That(json, Does.Contain("AGENTBRIDGE_TOOL_EXCEPTION"));
            Assert.That(File.Exists(Path.Combine(queue.ProcessingDirectory, "cmd-098.json")), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void Poller_StatusFile_DoesNotPromoteSuccessSummaryToLastError()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pollIntervalMs = 1;
            settings.compileBackoffMs = 1;
            var paths = new AgentBridgePaths(_workspaceRoot, settings);
            paths.EnsureDirectories();
            var queue = new AgentCommandQueue(_workspaceRoot, settings.tempRoot);
            var registry = new AgentToolRegistry();
            registry.Register(new SuccessfulObservabilityTool());
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var facade = new UnityToolFacade(registry, settings, logger);
            var poller = new AgentCommandPoller(queue, facade, settings, paths, logger);

            var enqueue = queue.Enqueue(CreateCommandJson("cmd-144-last-error", 1000, "unity.observability.success"));
            Assert.That(enqueue.Success, Is.True);

            InvokePollerUpdate(poller);

            var json = File.ReadAllText(paths.StatusFilePath);
            Assert.That(json, Does.Contain("\"currentStage\":\"unity.write_result\""));
            Assert.That(json, Does.Not.Contain("Project info collected."));
        }

        [Test]
        [Category("AGB_Observability")]
        public void Poller_LifecycleReadFailure_IsLoggedWithoutStoppingHeartbeat()
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pollIntervalMs = 1;
            settings.compileBackoffMs = 1;
            var paths = new AgentBridgePaths(_workspaceRoot, settings);
            paths.EnsureDirectories();
            var lifecycleStore = new CompileLifecycleStore(_workspaceRoot, settings.tempRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(lifecycleStore.StatePath) ?? string.Empty);
            File.WriteAllText(lifecycleStore.StatePath, "{not-json");

            var queue = new AgentCommandQueue(_workspaceRoot, settings.tempRoot);
            var registry = new AgentToolRegistry();
            registry.Discover();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
            var facade = new UnityToolFacade(registry, settings, logger);
            var poller = new AgentCommandPoller(queue, facade, settings, paths, logger);

            InvokePollerUpdate(poller);
            InvokePollerUpdate(poller);

            Assert.That(File.Exists(paths.StatusFilePath), Is.True);
            var statusJson = File.ReadAllText(paths.StatusFilePath);
            Assert.That(statusJson, Does.Contain("\"heartbeatUtc\""));

            var logText = File.Exists(paths.BridgeLogPath) ? File.ReadAllText(paths.BridgeLogPath) : string.Empty;
            Assert.That(logText, Does.Not.Contain("\"stage\":\"unity.poller.update_failed\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_099.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_099")]
        public void Logger_Stage_WritesVersionedStageEntry()
        {
            var logPath = Path.Combine(_workspaceRoot, "bridge.log");
            var logger = new FileAgentBridgeLogger(logPath);

            logger.Stage("mcp.write_command", "cmd-099", "unity.ping", "pending", "test stage");

            var text = File.ReadAllText(logPath);
            Assert.That(text, Does.Contain("\"logVersion\":\"1.0\""));
            Assert.That(text, Does.Contain("\"schemaVersion\":\"1.0\""));
            Assert.That(text, Does.Contain("\"stage\":\"mcp.write_command\""));
            Assert.That(text, Does.Contain("\"commandId\":\"cmd-099\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void Logger_Stage_WritesCompileLifecycleStageEntry()
        {
            var logPath = Path.Combine(_workspaceRoot, "bridge.log");
            var logger = new FileAgentBridgeLogger(logPath);

            logger.Stage("compile_started", "cmd-144-log", "unity.compile", "running", "compile epoch 3 started");

            var text = File.ReadAllText(logPath);
            Assert.That(text, Does.Contain("\"stage\":\"compile_started\""));
            Assert.That(text, Does.Contain("\"tool\":\"unity.compile\""));
            Assert.That(text, Does.Contain("\"status\":\"running\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void Logger_Stage_WritesCompileResultWrittenEntry()
        {
            var logPath = Path.Combine(_workspaceRoot, "bridge.log");
            var logger = new FileAgentBridgeLogger(logPath);

            logger.Stage("compile_result_written", "cmd-144-result", "unity.compile", "success", "terminal result written");

            var text = File.ReadAllText(logPath);
            Assert.That(text, Does.Contain("\"stage\":\"compile_result_written\""));
            Assert.That(text, Does.Contain("\"commandId\":\"cmd-144-result\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_100.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_100")]
        public void McpServer_RuntimeSources_DoNotUseConsoleOrHostingLogsForStdioDiagnostics()
        {
            var repoRoot = ResolveRepositoryRoot();
            var serverPath = Path.Combine(repoRoot, "UnityAgentBridge.Cli", "UnityAgentBridge.Mcp", "McpServerRuntime.cs");

            Assert.That(File.Exists(serverPath), Is.True, "McpServerRuntime.cs must exist for protocol-purity inspection.");
            var content = File.ReadAllText(serverPath);
            Assert.That(content, Does.Not.Contain("Console.WriteLine"));
            Assert.That(content, Does.Not.Contain("AddConsole("));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_137.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_137")]
        public void McpServer_HealthSource_DefinesLifecycleStateFields()
        {
            var repoRoot = ResolveRepositoryRoot();
            var cliHostPath = Path.Combine(repoRoot, "UnityAgentBridge.Cli", "UnityAgentBridge.ExternalBridgeClientCore", "BridgeHealthClient.cs");

            Assert.That(File.Exists(cliHostPath), Is.True);
            var text = File.ReadAllText(cliHostPath);
            Assert.That(text, Does.Contain("lifecycleState"));
            Assert.That(text, Does.Contain("healthReason"));
            Assert.That(text, Does.Contain("reconnectRequired"));
            Assert.That(text, Does.Contain("recommendedActionCode"));
            Assert.That(text, Does.Contain("recommendedAction"));
            Assert.That(text, Does.Contain("toolExecution"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_138.md
        [Test]
        [Category("AGB_Observability")]
        [Category("AGB_138")]
        public void McpServer_HealthSource_ClassifiesStaleLifecycle()
        {
            var repoRoot = ResolveRepositoryRoot();
            var cliHostPath = Path.Combine(repoRoot, "UnityAgentBridge.Cli", "UnityAgentBridge.ExternalBridgeClientCore", "BridgeHealthClient.cs");

            Assert.That(File.Exists(cliHostPath), Is.True);
            var text = File.ReadAllText(cliHostPath);
            Assert.That(text, Does.Contain("BridgeLifecycleStatus.Degraded(\"UnityUnavailable\", \"Reconnect\""));
            Assert.That(text, Does.Contain("Restart Unity or reconnect the MCP server because the bridge heartbeat is stale."));
            Assert.That(text, Does.Contain("BlockedBeforeDispatch"));
        }

        private static string CreateCommandJson(string commandId, int timeoutMs = 1000, string toolName = "unity.echo")
        {
            return "{\"schemaVersion\":\"1.0\",\"commandId\":\"" + commandId + "\",\"tool\":\"" + toolName + "\",\"timeoutMs\":" + timeoutMs + ",\"createdAt\":\"2026-06-08T10:00:00Z\",\"args\":{}}";
        }

        private static void InvokePollerUpdate(AgentCommandPoller poller)
        {
            var onEditorUpdate = typeof(AgentCommandPoller).GetMethod("OnEditorUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(onEditorUpdate, Is.Not.Null);
            onEditorUpdate.Invoke(poller, null);
        }

        private static string ResolveRepositoryRoot()
        {
            for (var cursor = Directory.GetCurrentDirectory(); !string.IsNullOrWhiteSpace(cursor); cursor = Path.GetDirectoryName(cursor))
            {
                var directCandidate = ResolveRepositoryRootCandidate(cursor);
                if (!string.IsNullOrWhiteSpace(directCandidate))
                {
                    return directCandidate;
                }

                var siblingCandidate = ResolveRepositoryRootCandidate(Path.Combine(cursor, "unity-agent-bridge"));
                if (!string.IsNullOrWhiteSpace(siblingCandidate))
                {
                    return siblingCandidate;
                }
            }

            Assert.Fail("Repository root could not be resolved for MCP server source inspection.");
            return string.Empty;
        }

        private static string ResolveRepositoryRootCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            return Directory.Exists(Path.Combine(candidate, "UnityAgentBridge.Cli")) &&
                   Directory.Exists(Path.Combine(candidate, "com.unitymcp.agent-bridge"))
                ? candidate
                : string.Empty;
        }

        private sealed class ThrowingObservabilityTool : IAgentTool
        {
            public ToolDescriptor Descriptor { get; } = new ToolDescriptor
            {
                Name = "unity.observability.throw",
                SchemaVersion = "1.0",
                Description = "Throw for observability tests.",
                AllowedModes = ToolExecutionModes.EditAndPlay,
                SideEffect = ToolSideEffect.RunsUserCode,
                MayTriggerDomainReload = false,
                ArgsSchemaPath = string.Empty
            };

            public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
            {
                throw new InvalidOperationException("throwing tool for observability");
            }
        }

        private sealed class SuccessfulObservabilityTool : IAgentTool
        {
            public ToolDescriptor Descriptor { get; } = new ToolDescriptor
            {
                Name = "unity.observability.success",
                SchemaVersion = "1.0",
                Description = "Return success summary for observability tests.",
                AllowedModes = ToolExecutionModes.EditAndPlay,
                SideEffect = ToolSideEffect.ReadsProject,
                MayTriggerDomainReload = false,
                ArgsSchemaPath = string.Empty
            };

            public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
            {
                return new ToolResult
                {
                    success = true,
                    status = ToolResultStatus.Success,
                    summary = "Project info collected."
                };
            }
        }
    }
}
