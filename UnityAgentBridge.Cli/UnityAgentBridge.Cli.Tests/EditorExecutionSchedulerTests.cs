using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class EditorExecutionSchedulerTests
{
    // TestRecord: Documentation~/test_records/AGBM_306.md
    [TestMethod, TestCategory("AGBM_306")]
    public void TwoCallers_CannotStartCompileUntilActualTestCompletion()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test", "C1"));
        fixture.Scheduler.Admit(fixture.Request("compile", "C2"));
        var starts = new List<string>();
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var test));
        starts.Add(test.Request.RequestId);
        Assert.IsTrue(fixture.Scheduler.MarkRunning("test", test.OwnershipToken, "run-123"));
        Assert.IsFalse(fixture.Scheduler.TryClaimNext(null, out _));
        // A logical timeout and an observer cancellation do not mean Unity stopped.
        Assert.IsTrue(fixture.Scheduler.RecordOutcome("test", "TimedOut", "AGENT_TASK_TIMEOUT"));
        Assert.AreEqual(AgentTaskCancellationDisposition.AlreadyTerminal, fixture.Scheduler.CancelWaiting("test"));
        Assert.IsFalse(fixture.Scheduler.TryClaimNext(null, out _));
        Assert.IsTrue(fixture.Scheduler.ConfirmExecutionEnded("test", test.OwnershipToken, "RunFinished(run-123)"));
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var compile));
        starts.Add(compile.Request.RequestId);
        CollectionAssert.AreEqual(new[] { "test", "compile" }, starts);
        Assert.AreEqual("C1", fixture.Scheduler.Find("test").Request.ConnectionId);
        Assert.AreEqual("C2", compile.Request.ConnectionId);
        Assert.IsFalse(fixture.Scheduler.RecordOutcome("test", "Completed"), "Late callbacks must not replace timeout.");
    }

    // TestRecord: Documentation~/test_records/AGBM_307.md
    [TestMethod, TestCategory("AGBM_307")]
    public void AdmissionOrder_IsIndependentOfHostTimestampsAndCallerMutation()
    {
        var fixture = new Fixture();
        var first = fixture.Request("first");
        first.SubmittedAt = fixture.Clock.UtcNow.AddSeconds(-10);
        fixture.Scheduler.Admit(first);
        var second = fixture.Request("second");
        second.SubmittedAt = fixture.Clock.UtcNow.AddSeconds(-20);
        fixture.Scheduler.Admit(second);
        first.RequestId = "changed-after-admission";
        var read = fixture.Scheduler.Find("first");
        read.Request.ConnectionId = "mutated-read";
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var claim));
        Assert.AreEqual("first", claim.Request.RequestId);
        Assert.AreEqual("C1", claim.Request.ConnectionId);
    }

    // TestRecord: Documentation~/test_records/AGBM_308.md
    [TestMethod, TestCategory("AGBM_308")]
    public void QueueFull_IsExplicitAndDoesNotAllocateSequence()
    {
        var fixture = new Fixture(capacity: 1);
        Assert.IsTrue(fixture.Scheduler.Admit(fixture.Request("first")).Accepted);
        var rejected = fixture.Scheduler.Admit(fixture.Request("second"));
        Assert.AreEqual("SCHEDULER_QUEUE_FULL", rejected.ErrorCode);
        Assert.AreEqual(1, fixture.Scheduler.WaitingCount);
        Assert.IsNull(fixture.Scheduler.Find("second"));
        Assert.AreEqual(2L, fixture.Store.Committed!.NextSequence);
    }

    // TestRecord: Documentation~/test_records/AGBM_309.md
    [TestMethod, TestCategory("AGBM_309")]
    public void QueuedCancellation_PreventsDispatchAndPreservesFollowingOrder()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("cancelled"));
        fixture.Scheduler.Admit(fixture.Request("remaining"));
        Assert.AreEqual(AgentTaskCancellationDisposition.Accepted, fixture.Scheduler.CancelWaiting("cancelled"));
        Assert.AreEqual(AgentTaskCancellationDisposition.AlreadyCancelled, fixture.Scheduler.CancelWaiting("cancelled"));
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var next));
        Assert.AreEqual("remaining", next.Request.RequestId);
        Assert.IsNull(fixture.Scheduler.Find("cancelled").StartedAt);
    }

    // TestRecord: Documentation~/test_records/AGBM_310.md
    [TestMethod, TestCategory("AGBM_310")]
    public void DispatchClaim_WinsCancellationWithoutReleasingSlot()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var claimed));
        Assert.AreEqual(AgentTaskCancellationDisposition.Unsupported, fixture.Scheduler.CancelWaiting("test"));
        Assert.AreEqual(claimed.OwnershipToken, fixture.Scheduler.Active.OwnershipToken);
        Assert.IsFalse(fixture.Scheduler.ConfirmExecutionEnded("test", "another-owner", "unrelated callback"));
    }

    // TestRecord: Documentation~/test_records/AGBM_311.md
    [TestMethod, TestCategory("AGBM_311")]
    public void ExpiredWaitingRequest_NeverCrossesDispatchBoundary()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("expired", milliseconds: 100));
        fixture.Scheduler.Admit(fixture.Request("next"));
        fixture.Clock.Advance(100);
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var next));
        Assert.AreEqual("next", next.Request.RequestId);
        var expired = fixture.Scheduler.Find("expired");
        Assert.AreEqual("TimedOut", expired.Outcome);
        Assert.IsNull(expired.StartedAt);
        Assert.IsNull(expired.OwnershipToken);
    }

    // TestRecord: Documentation~/test_records/AGBM_312.md
    [TestMethod, TestCategory("AGBM_312")]
    public void ExpiryProgressesWhileAnotherOperationOwnsSlot()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("running"));
        fixture.Scheduler.TryClaimNext(null, out var owner);
        fixture.Scheduler.Admit(fixture.Request("expires", milliseconds: 20));
        fixture.Clock.Advance(21);
        Assert.AreEqual(1, fixture.Scheduler.ExpireWaiting());
        Assert.AreEqual("TimedOut", fixture.Scheduler.Find("expires").Outcome);
        Assert.AreEqual(owner.OwnershipToken, fixture.Scheduler.Active.OwnershipToken);
    }

    // TestRecord: Documentation~/test_records/AGBM_313.md
    [TestMethod, TestCategory("AGBM_313")]
    public void ManualBusyReason_PausesFifoWithoutOvertaking()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("first"));
        fixture.Scheduler.Admit(fixture.Request("second"));
        Assert.IsFalse(fixture.Scheduler.TryClaimNext("Manual test is active", out _));
        Assert.AreEqual("Manual test is active", fixture.Scheduler.Find("first").BlockingReason);
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var next));
        Assert.AreEqual("first", next.Request.RequestId);
    }

    // TestRecord: Documentation~/test_records/AGBM_314.md
    [TestMethod, TestCategory("AGBM_314")]
    public void DuplicateDispatch_ReusesIdentityAndRejectsConflictingOwnership()
    {
        var fixture = new Fixture();
        var request = fixture.Request("once");
        var original = fixture.Scheduler.Admit(request);
        var repeated = fixture.Scheduler.Admit(request);
        Assert.IsTrue(repeated.Existing);
        Assert.AreEqual(original.Execution.Sequence, repeated.Execution.Sequence);
        request.ConnectionId = "C2";
        Assert.AreEqual("SCHEDULER_OWNERSHIP_CONFLICT", fixture.Scheduler.Admit(request).ErrorCode);
        Assert.AreEqual(1, fixture.Scheduler.ReadEntries().Count);
    }

    // TestRecord: Documentation~/test_records/AGBM_315.md
    [TestMethod, TestCategory("AGBM_315")]
    public void DomainReload_RestoresQueueAndActiveTokenWithoutReplayingStart()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Scheduler.Admit(fixture.Request("compile"));
        fixture.Scheduler.TryClaimNext(null, out var active);
        fixture.Scheduler.MarkRunning("test", active.OwnershipToken, "original-run");
        fixture.Clock.Advance(500);
        var restored = fixture.Restore();
        Assert.AreEqual(AgentExecutionPhase.Recovering, restored.Active.Phase);
        Assert.AreEqual(active.OwnershipToken, restored.Active.OwnershipToken);
        Assert.IsFalse(restored.TryClaimNext(null, out _));
        Assert.IsFalse(restored.ResumeObservation("test", active.OwnershipToken, "replacement-run"));
        Assert.IsTrue(restored.ResumeObservation("test", active.OwnershipToken, "original-run"));
        Assert.AreEqual(fixture.Request("compile").SubmittedAt.AddMilliseconds(-500), restored.Find("compile").Request.SubmittedAt);
        Assert.IsTrue(restored.ConfirmExecutionEnded("test", active.OwnershipToken, "original-run finished"));
        Assert.IsTrue(restored.TryClaimNext(null, out var next));
        Assert.AreEqual("compile", next.Request.RequestId);
    }

    // TestRecord: Documentation~/test_records/AGBM_316.md
    [TestMethod, TestCategory("AGBM_316")]
    public void CrashBetweenClaimAndStart_IsUnknownOccupancyRatherThanReplay()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Scheduler.TryClaimNext(null, out var claimed);
        var restored = fixture.Restore();
        Assert.AreEqual(AgentExecutionPhase.Recovering, restored.Active.Phase);
        Assert.IsNull(restored.Active.OperationCorrelation);
        Assert.IsFalse(restored.TryClaimNext(null, out _));
        Assert.IsFalse(restored.MarkRunning("test", claimed.OwnershipToken, "new-run"));
        Assert.IsFalse(restored.ResumeObservation("test", claimed.OwnershipToken, "new-run"));
    }

    // TestRecord: Documentation~/test_records/AGBM_317.md
    [TestMethod, TestCategory("AGBM_317")]
    public void NewEditor_DoesNotExecuteOldWaitingWorkOrAssumeOldActiveStopped()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("old-running"));
        fixture.Scheduler.TryClaimNext(null, out var old);
        fixture.Scheduler.Admit(fixture.Request("old-waiting"));
        var restored = fixture.Restore("S2");
        Assert.AreEqual("Interrupted", restored.Find("old-waiting").Outcome);
        Assert.IsNull(restored.Find("old-waiting").StartedAt);
        Assert.IsFalse(restored.TryClaimNext(null, out _));
        Assert.IsTrue(restored.ConfirmExecutionEnded("old-running", old.OwnershipToken, "Original process exit verified by PID and start-time"));
        var request = fixture.Request("new-work");
        request.EditorSessionId = "S2";
        Assert.IsTrue(restored.Admit(request).Accepted);
        Assert.IsTrue(restored.TryClaimNext(null, out var current));
        Assert.AreEqual("new-work", current.Request.RequestId);
    }

    // TestRecord: Documentation~/test_records/AGBM_318.md
    [TestMethod, TestCategory("AGBM_318")]
    public void PersistenceFailureBeforeClaim_DoesNotGrantExecutionOwnership()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Store.FailWrites = true;
        Assert.IsFalse(fixture.Scheduler.TryClaimNext(null, out var claim));
        Assert.IsNull(claim);
        Assert.IsNotNull(fixture.Scheduler.RecoveryError);
        Assert.IsFalse(fixture.Scheduler.Admit(fixture.Request("another")).Accepted);
        fixture.Store.FailWrites = false;
        Assert.IsTrue(fixture.Scheduler.Reload());
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out _));
    }

    // TestRecord: Documentation~/test_records/AGBM_319.md
    [TestMethod, TestCategory("AGBM_319")]
    public void AmbiguousCommittedClaim_RecoversAsOccupied()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Store.CommitThenThrow = true;
        Assert.IsFalse(fixture.Scheduler.TryClaimNext(null, out _));
        fixture.Store.CommitThenThrow = false;
        var restored = fixture.Restore();
        Assert.AreEqual(AgentExecutionPhase.Recovering, restored.Active.Phase);
        Assert.IsFalse(restored.TryClaimNext(null, out _));
    }

    // TestRecord: Documentation~/test_records/AGBM_320.md
    [TestMethod, TestCategory("AGBM_320")]
    public void CompletionWriteFailure_DoesNotReleaseActiveOwnership()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Scheduler.TryClaimNext(null, out var active);
        fixture.Store.FailWrites = true;
        Assert.IsFalse(fixture.Scheduler.ConfirmExecutionEnded("test", active.OwnershipToken, "finished callback"));
        Assert.IsNotNull(fixture.Scheduler.Active);
        Assert.IsFalse(fixture.Scheduler.TryClaimNext(null, out _));
    }

    // TestRecord: Documentation~/test_records/AGBM_321.md
    [TestMethod, TestCategory("AGBM_321")]
    public void CorruptOrUnsupportedStore_FailsClosedInsteadOfStartingEmpty()
    {
        var fixture = new Fixture();
        fixture.Store.Committed!.Version = 999;
        var restored = fixture.Restore();
        Assert.IsNotNull(restored.RecoveryError);
        Assert.IsFalse(restored.Admit(fixture.Request("unsafe")).Accepted);
        Assert.IsFalse(restored.TryClaimNext(null, out _));
    }

    // TestRecord: Documentation~/test_records/AGBM_322.md
    [TestMethod, TestCategory("AGBM_322")]
    public void MissingStoreAfterCommittedHistory_DoesNotEraseKnownOccupancy()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Scheduler.TryClaimNext(null, out _);
        fixture.Store.Committed = null;
        Assert.IsFalse(fixture.Scheduler.Reload());
        Assert.IsNotNull(fixture.Scheduler.Active);
    }

    // TestRecord: Documentation~/test_records/AGBM_323.md
    [TestMethod, TestCategory("AGBM_323")]
    public void ActualCompletion_ReleasesSlotWhileOriginalDeliveryIsPending()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("test"));
        fixture.Scheduler.TryClaimNext(null, out var active);
        fixture.Scheduler.RecordOutcome("test", "Completed");
        fixture.Scheduler.ConfirmExecutionEnded("test", active.OwnershipToken, "RunFinished");
        fixture.Scheduler.Admit(fixture.Request("compile"));
        Assert.IsTrue(fixture.Scheduler.TryClaimNext(null, out var compile));
        Assert.AreEqual("compile", compile.Request.RequestId);
        Assert.IsFalse(fixture.Scheduler.Find("test").DeliveryAcknowledged);
    }

    // TestRecord: Documentation~/test_records/AGBM_324.md
    [TestMethod, TestCategory("AGBM_324")]
    public void Cleanup_PreservesUnresolvedOccupancyAndPendingDelivery()
    {
        var fixture = new Fixture();
        fixture.Scheduler.Admit(fixture.Request("running"));
        fixture.Scheduler.TryClaimNext(null, out var active);
        fixture.Scheduler.RecordOutcome("running", "TimedOut");
        fixture.Scheduler.AcknowledgeDelivery("running");
        fixture.Scheduler.Admit(fixture.Request("undelivered"));
        fixture.Scheduler.CancelWaiting("undelivered");
        fixture.Scheduler.Admit(fixture.Request("delivered"));
        fixture.Scheduler.CancelWaiting("delivered");
        fixture.Scheduler.AcknowledgeDelivery("delivered");
        fixture.Clock.Advance((long)TimeSpan.FromDays(8).TotalMilliseconds);
        Assert.AreEqual(1, fixture.Scheduler.Cleanup());
        Assert.IsNotNull(fixture.Scheduler.Find("running"));
        Assert.IsNotNull(fixture.Scheduler.Find("undelivered"));
        Assert.IsNull(fixture.Scheduler.Find("delivered"));
        Assert.AreEqual(active.OwnershipToken, fixture.Scheduler.Active.OwnershipToken);
    }

    // TestRecord: Documentation~/test_records/AGBM_325.md
    [TestMethod, TestCategory("AGBM_325")]
    public void DifferentProjects_HaveIndependentExecutionSlots()
    {
        var a = new Fixture();
        var b = new Fixture(projectId: "B");
        a.Scheduler.Admit(a.Request("test"));
        b.Scheduler.Admit(b.Request("test"));
        Assert.IsTrue(a.Scheduler.TryClaimNext(null, out _));
        Assert.IsTrue(b.Scheduler.TryClaimNext(null, out _));
        Assert.AreEqual("A", a.Scheduler.Active.Request.ProjectId);
        Assert.AreEqual("B", b.Scheduler.Active.Request.ProjectId);
    }

    // TestRecord: Documentation~/test_records/AGBM_326.md
    [TestMethod, TestCategory("AGBM_326")]
    public void CrossProjectAndOldEditorAdmission_AreRejected()
    {
        var fixture = new Fixture();
        var request = fixture.Request("wrong");
        request.ProjectId = "B";
        Assert.IsFalse(fixture.Scheduler.Admit(request).Accepted);
        request.ProjectId = "A";
        request.EditorSessionId = "old-session";
        Assert.IsFalse(fixture.Scheduler.Admit(request).Accepted);
    }

    // TestRecord: Documentation~/test_records/AGBM_327.md
    [TestMethod, TestCategory("AGBM_327")]
    public void InvalidCapacityAndOversizedPayload_AreRejected()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Fixture(capacity: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Fixture(capacity: 257));
        var fixture = new Fixture();
        var request = fixture.Request("too-large");
        request.Payload = new string('x', 1024 * 1024 + 1);
        Assert.AreEqual("SCHEDULER_REQUEST_INVALID", fixture.Scheduler.Admit(request).ErrorCode);
        Assert.AreEqual(0, fixture.Scheduler.WaitingCount);
    }

    // TestRecord: Documentation~/test_records/AGBM_367.md
    [TestMethod, TestCategory("AGBM_367")]
    public void RestoredDuplicateCommandOrTaskAlias_FailsClosed()
    {
        foreach (var duplicateCommand in new[] { true, false })
        {
            var fixture = new Fixture();
            fixture.Scheduler.Admit(fixture.Request("one"));
            fixture.Scheduler.Admit(fixture.Request("two"));
            var entries = fixture.Store.Committed!.Entries;
            if (duplicateCommand) entries[1].Request.CommandId = entries[0].Request.CommandId;
            else { entries[0].Request.AgentTaskId = "shared-task"; entries[1].LinkedTaskId = "shared-task"; }
            var restored = fixture.Restore();
            Assert.IsNotNull(restored.RecoveryError);
            Assert.IsFalse(restored.TryClaimNext(null, out _));
            Assert.IsFalse(restored.Admit(fixture.Request("new")).Accepted);
        }
    }

    private sealed class Fixture
    {
        public readonly FakeClock Clock = new();
        public readonly JournalStore Store = new();
        public readonly AgentExecutionScheduler Scheduler;
        private readonly string projectId;
        private readonly AgentExecutionSchedulerOptions options;
        public Fixture(int capacity = 32, string projectId = "A")
        {
            this.projectId = projectId;
            options = new AgentExecutionSchedulerOptions { WaitingCapacity = capacity };
            Scheduler = Restore();
        }
        public AgentExecutionScheduler Restore(string session = "S1") => new(projectId, session, Store, Clock, options);
        public AgentScheduledRequest Request(string id, string connection = "C1", int milliseconds = 10000) => new()
        {
            RequestId = id, CommandId = id, ProjectId = projectId, EditorSessionId = "S1", ConnectionId = connection,
            Tool = id.Contains("compile") ? "unity.compile" : "unity.run_editmode_tests", Payload = "{}",
            SubmittedAt = Clock.UtcNow, Deadline = Clock.UtcNow.AddMilliseconds(milliseconds), OriginKind = "durable-submission"
        };
    }

    private sealed class FakeClock : IAgentTaskClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public void Advance(long milliseconds) => UtcNow = UtcNow.AddMilliseconds(milliseconds);
    }

    private sealed class JournalStore : IAgentExecutionJournalStore
    {
        public AgentExecutionJournal? Committed;
        public bool FailWrites;
        public bool CommitThenThrow;
        public AgentExecutionJournal Read() => Copy(Committed)!;
        public void Write(AgentExecutionJournal journal)
        {
            if (FailWrites) throw new IOException("Injected write failure.");
            Committed = Copy(journal);
            if (CommitThenThrow) throw new IOException("Commit succeeded, acknowledgement was lost.");
        }
        private static AgentExecutionJournal? Copy(AgentExecutionJournal? value) => value == null ? null :
            JsonConvert.DeserializeObject<AgentExecutionJournal>(JsonConvert.SerializeObject(value));
    }
}
