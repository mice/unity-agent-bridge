using System;
using System.IO;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentCommandQueueTests
    {
        private string _workspaceRoot;

        [SetUp]
        public void SetUp()
        {
            _workspaceRoot = Path.Combine(Path.GetTempPath(), "AgentBridgeQueueTests", Guid.NewGuid().ToString("N"));
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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_025.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_025")]
        public void TryDequeue_IgnoresTmpFile()
        {
            var queue = CreateQueue();
            File.WriteAllText(Path.Combine(queue.InboxDirectory, "cmd-025.json.tmp"), CreateCommandJson("cmd-025"), System.Text.Encoding.UTF8);

            var dequeued = queue.TryDequeue(out var command);

            Assert.That(dequeued, Is.False);
            Assert.That(command, Is.Null);
            Assert.That(File.Exists(Path.Combine(queue.InboxDirectory, "cmd-025.json.tmp")), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_026.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_026")]
        public void Enqueue_DuplicateCommandId_ReturnsDuplicate()
        {
            var queue = CreateQueue();

            var first = queue.Enqueue(CreateCommandJson("cmd-026"));
            var second = queue.Enqueue(CreateCommandJson("cmd-026"));

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
            Assert.That(second.Reason, Is.EqualTo("duplicate_command_id"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_027.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_027")]
        public void Recover_RunningState_ReturnsResuming()
        {
            var now = new DateTime(2026, 6, 5, 10, 0, 10, DateTimeKind.Utc);
            var queue = CreateQueue(() => now);
            SeedProcessing(queue, "cmd-027", CreateCommandJson("cmd-027"), new QueueCommandState
            {
                commandId = "cmd-027",
                tool = "unity.echo",
                status = ToolResultStatus.Running,
                startedAt = now.AddSeconds(-1).ToString("O"),
                timeoutMs = 1000
            });

            var records = queue.Recover();

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Action, Is.EqualTo(QueueRecoveryAction.Resuming));
            Assert.That(records[0].Command.Command.commandId, Is.EqualTo("cmd-027"));
            Assert.That(File.ReadAllText(records[0].Command.StatePath), Does.Contain("\"status\":\"resuming\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_028.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_028")]
        public void Recover_ExpiredProcessing_WritesTimeoutAndMovesToFailed()
        {
            var now = new DateTime(2026, 6, 5, 10, 5, 0, DateTimeKind.Utc);
            var queue = CreateQueue(() => now);
            SeedProcessing(queue, "cmd-028", CreateCommandJson("cmd-028"), new QueueCommandState
            {
                commandId = "cmd-028",
                tool = "unity.echo",
                status = ToolResultStatus.Running,
                startedAt = now.AddSeconds(-5).ToString("O"),
                timeoutMs = 1000
            });

            var records = queue.Recover();

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Action, Is.EqualTo(QueueRecoveryAction.TimedOut));
            Assert.That(records[0].Result.status, Is.EqualTo(ToolResultStatus.Timeout));
            Assert.That(File.Exists(Path.Combine(queue.OutboxDirectory, "cmd-028.result.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(queue.FailedDirectory, "cmd-028.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(queue.ProcessingDirectory, "cmd-028.state.json")), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_029.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_029")]
        public void Recover_StateWithoutCommand_WritesExceptionResult()
        {
            var queue = CreateQueue();
            var statePath = Path.Combine(queue.ProcessingDirectory, "cmd-029.state.json");
            File.WriteAllText(statePath, "{\"commandId\":\"cmd-029\",\"tool\":\"unity.echo\",\"status\":\"running\",\"startedAt\":\"2026-06-05T10:00:00.0000000Z\",\"timeoutMs\":1000}");

            var records = queue.Recover();

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Action, Is.EqualTo(QueueRecoveryAction.ExceptionWritten));
            Assert.That(records[0].Result.errors[0].code, Is.EqualTo("AGENTBRIDGE_STATE_WITHOUT_COMMAND"));
            Assert.That(File.Exists(Path.Combine(queue.OutboxDirectory, "cmd-029.result.json")), Is.True);
            Assert.That(File.Exists(statePath), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_030.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_030")]
        public void Recover_ExistingOutbox_CleansProcessingWithoutRedo()
        {
            var queue = CreateQueue();
            SeedProcessing(queue, "cmd-030", CreateCommandJson("cmd-030"), new QueueCommandState
            {
                commandId = "cmd-030",
                tool = "unity.echo",
                status = ToolResultStatus.Running,
                startedAt = "2026-06-05T10:00:00.0000000Z",
                timeoutMs = 1000
            });
            File.WriteAllText(Path.Combine(queue.OutboxDirectory, "cmd-030.result.json"), "{\"status\":\"success\"}");

            var records = queue.Recover();

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Action, Is.EqualTo(QueueRecoveryAction.CompletedCleanup));
            Assert.That(File.Exists(Path.Combine(queue.ProcessingDirectory, "cmd-030.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(queue.ProcessingDirectory, "cmd-030.state.json")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(queue.OutboxDirectory, "cmd-030.result.json")), Is.EqualTo("{\"status\":\"success\"}"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_031.md
        [Test]
        [Category("AGB_Queue")]
        [Category("AGB_031")]
        public void TryDequeue_LockedInboxFile_SkipsWithoutFatal()
        {
            var queue = CreateQueue();
            var commandPath = Path.Combine(queue.InboxDirectory, "cmd-031.json");
            File.WriteAllText(commandPath, CreateCommandJson("cmd-031"));

            using (new FileStream(commandPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var dequeued = queue.TryDequeue(out var command);

                Assert.That(dequeued, Is.False);
                Assert.That(command, Is.Null);
            }
        }

        private AgentCommandQueue CreateQueue(Func<DateTime> utcNowProvider = null)
        {
            return new AgentCommandQueue(_workspaceRoot, "Temp/AgentBridge", utcNowProvider);
        }

        private static string CreateCommandJson(string commandId)
        {
            return "{\"schemaVersion\":\"1.0\",\"commandId\":\"" + commandId + "\",\"tool\":\"unity.echo\",\"timeoutMs\":1000,\"createdAt\":\"2026-06-05T10:00:00Z\",\"args\":{}}";
        }

        private static void SeedProcessing(AgentCommandQueue queue, string commandId, string rawCommandJson, QueueCommandState state)
        {
            File.WriteAllText(Path.Combine(queue.ProcessingDirectory, commandId + ".json"), rawCommandJson);
            File.WriteAllText(
                Path.Combine(queue.ProcessingDirectory, commandId + ".state.json"),
                "{\"commandId\":\"" + state.commandId +
                "\",\"tool\":\"" + state.tool +
                "\",\"status\":\"" + state.status +
                "\",\"startedAt\":\"" + state.startedAt +
                "\",\"timeoutMs\":" + state.timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "}");
        }
    }
}
