using System;
using NUnit.Framework;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class CompileLifecycleStateMachineTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_140.md
        [Test]
        [Category("AGB_140")]
        public void RecordCompilationStarted_AssignsMonotonicEpochAndResetsLifecycle()
        {
            var state = new CompileLifecycleState
            {
                compileEpoch = 2,
                lastStartedEpoch = 2,
                lastFinishedEpoch = 2,
                activeCommandIds = { "cmd-1" }
            };

            var updated = CompileLifecycleStateMachine.RecordCompilationStarted(state, new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc), "D:/Proj");

            Assert.That(updated.compileEpoch, Is.EqualTo(3));
            Assert.That(updated.lastStartedEpoch, Is.EqualTo(3));
            Assert.That(updated.currentStage, Is.EqualTo("compile_started"));
            Assert.That(updated.activeTargetEpochs, Is.EqualTo(new[] { 3 }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_141.md
        [Test]
        [Category("AGB_141")]
        public void RegisterWaitingCommand_AddsCommandAndTargetEpoch()
        {
            var state = CompileLifecycleStateMachine.RegisterWaitingCommand(new CompileLifecycleState(), "cmd-141", 5, "waiting_for_finish", "D:/Proj");

            Assert.That(state.activeCommandIds, Does.Contain("cmd-141"));
            Assert.That(state.activeTargetEpochs, Does.Contain(5));
            Assert.That(state.currentStage, Is.EqualTo("waiting_for_finish"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_142.md
        [Test]
        [Category("AGB_142")]
        public void HasFinishedEpoch_RejectsStaleFinishedEpoch()
        {
            var state = new CompileLifecycleState
            {
                lastFinishedEpoch = 4
            };

            Assert.That(CompileLifecycleStateMachine.HasFinishedEpoch(state, 5), Is.False);
            Assert.That(CompileLifecycleStateMachine.HasFinishedEpoch(state, 4), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_143.md
        [Test]
        [Category("AGB_143")]
        public void UnregisterCommand_RemovesCommandAndTargetEpoch()
        {
            var state = new CompileLifecycleState
            {
                activeCommandIds = { "cmd-143" },
                activeTargetEpochs = { 7 }
            };

            var updated = CompileLifecycleStateMachine.UnregisterCommand(state, "cmd-143", 7);

            Assert.That(updated.activeCommandIds, Does.Not.Contain("cmd-143"));
            Assert.That(updated.activeTargetEpochs.Contains(7), Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_143.md
        [Test]
        [Category("AGB_143")]
        public void RegisterWaitingCommand_UnknownEpochCanRebindToLaterObservedStart()
        {
            var state = new CompileLifecycleState
            {
                compileEpoch = 4,
                lastStartedEpoch = 4,
                activeCommandIds = { "cmd-143b" }
            };

            var started = CompileLifecycleStateMachine.RecordCompilationStarted(state, new DateTime(2026, 6, 14, 1, 0, 0, DateTimeKind.Utc), "D:/Proj");
            var rebound = CompileLifecycleStateMachine.RegisterWaitingCommand(started, "cmd-143b", started.compileEpoch, "waiting_for_finish", "D:/Proj");

            Assert.That(rebound.compileEpoch, Is.EqualTo(5));
            Assert.That(rebound.activeTargetEpochs, Does.Contain(5));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_143.md
        [Test]
        [Category("AGB_143")]
        public void UnknownEpochRestored_DoesNotBindWithoutLaterStart()
        {
            var state = CompileLifecycleStateMachine.EnsureInitialized(new CompileLifecycleState
            {
                compileEpoch = 4,
                lastStartedEpoch = 4,
                lastFinishedEpoch = 4,
                currentStage = "unknown_epoch_restored"
            }, "D:/Proj");

            Assert.That(CompileLifecycleStateMachine.HasFinishedEpoch(state, 0), Is.False);
            Assert.That(state.currentStage, Is.EqualTo("unknown_epoch_restored"));
        }
    }
}
