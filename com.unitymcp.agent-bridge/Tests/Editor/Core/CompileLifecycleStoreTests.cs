using System;
using System.IO;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class CompileLifecycleStoreTests
    {
        private string _workspaceRoot;

        [SetUp]
        public void SetUp()
        {
            _workspaceRoot = Path.Combine(Path.GetTempPath(), "CompileLifecycleStoreTests", Guid.NewGuid().ToString("N"));
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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void WriteRead_RoundTripsCompileLifecycleState()
        {
            var store = new CompileLifecycleStore(_workspaceRoot, "Temp/AgentBridge");
            var state = new CompileLifecycleState
            {
                compileEpoch = 9,
                currentStage = "waiting_for_finish",
                activeCommandIds = { "cmd-144" },
                activeTargetEpochs = { 9 },
                projectPath = _workspaceRoot.Replace('\\', '/')
            };

            store.Write(state);
            var loaded = store.Read();

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.compileEpoch, Is.EqualTo(9));
            Assert.That(loaded.currentStage, Is.EqualTo("waiting_for_finish"));
            Assert.That(loaded.activeCommandIds, Does.Contain("cmd-144"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_144.md
        [Test]
        [Category("AGB_144")]
        public void WriteRead_RoundTripsLifecycleObservabilityFields()
        {
            var store = new CompileLifecycleStore(_workspaceRoot, "Temp/AgentBridge");
            var state = new CompileLifecycleState
            {
                compileEpoch = 2,
                currentStage = "assembly_finished",
                lastTransition = "assembly_finished",
                lastTransitionAtUtc = "2026-06-14T01:00:00.0000000Z",
                timeoutReason = "compile_finish_timeout",
                projectPath = _workspaceRoot.Replace('\\', '/')
            };

            store.Write(state);
            var loaded = store.Read();

            Assert.That(loaded.lastTransition, Is.EqualTo("assembly_finished"));
            Assert.That(loaded.lastTransitionAtUtc, Is.EqualTo("2026-06-14T01:00:00.0000000Z"));
            Assert.That(loaded.timeoutReason, Is.EqualTo("compile_finish_timeout"));
            Assert.That(loaded.projectPath, Is.EqualTo(_workspaceRoot.Replace('\\', '/')));
        }
    }
}
