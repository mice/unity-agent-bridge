using System;
using System.IO;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class EditorSchedulingDeliveryTests
    {
        private string _root;
        private AgentCommandQueue _queue;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), nameof(EditorSchedulingDeliveryTests), Guid.NewGuid().ToString("N"));
            _queue = new AgentCommandQueue(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_366.md
        [Test, Category("AGB_Scheduling"), Category("AGB_366")]
        public void Complete_RetryPreservesOriginalTerminalBytes()
        {
            _queue.Complete("original", new ToolResult { status = ToolResultStatus.Success, success = true, summary = "original" });
            var path = Path.Combine(_queue.OutboxDirectory, "original.result.json");
            var original = File.ReadAllBytes(path);
            _queue.Complete("original", new ToolResult { status = ToolResultStatus.Timeout, summary = "late timeout" });
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_367.md
        [Test, Category("AGB_Scheduling"), Category("AGB_367")]
        public void Complete_CorruptOriginalCannotBeOverwrittenByRetry()
        {
            var path = Path.Combine(_queue.OutboxDirectory, "original.result.json");
            File.WriteAllText(path, "{broken");
            Assert.Throws<InvalidDataException>(() => _queue.Complete("original", new ToolResult { status = ToolResultStatus.Success }));
            Assert.That(File.ReadAllText(path), Is.EqualTo("{broken"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_364.md
        [Test, Category("AGB_Scheduling"), Category("AGB_364")]
        public void TryDequeue_RetainedOutboxPreventsReplayAfterProcessingCleanup()
        {
            const string id = "duplicate";
            _queue.Complete(id, new ToolResult { status = ToolResultStatus.Success, success = true });
            var json = JsonUtil.BuildCommandEnvelope(id, "unity.compile", 1000, "{}", new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Path.Combine(_queue.InboxDirectory, id + ".json"), json);
            Assert.That(_queue.TryDequeue(out _), Is.False);
            Assert.That(File.Exists(Path.Combine(_queue.InboxDirectory, id + ".json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_queue.OutboxDirectory, id + ".result.json")), Is.True);
        }
    }
}
