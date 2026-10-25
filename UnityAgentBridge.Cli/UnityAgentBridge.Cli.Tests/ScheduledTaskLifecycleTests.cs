using Microsoft.VisualStudio.TestTools.UnitTesting;
using UnityMcp.AgentTaskCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class ScheduledTaskLifecycleTests
{
    // TestRecord: Documentation~/test_records/AGBM_334.md
    [TestMethod, TestCategory("AGBM_334")]
    public void UnstartedNoncancellableDriver_CanBeCancelledWithoutInvocation()
    {
        var manager = new AgentTaskManager();
        var driver = new Driver();
        var id = manager.Register(driver);
        Assert.AreEqual(AgentTaskCancellationDisposition.Accepted, manager.Cancel(id));
        Assert.AreEqual(AgentTaskState.Cancelled, manager.Get(id).State);
        Assert.AreEqual(0, driver.StartCount);
        Assert.AreEqual(0, driver.CancelCount);
        Assert.ThrowsException<InvalidOperationException>(() => manager.Start(id));
    }

    // TestRecord: Documentation~/test_records/AGBM_335.md
    [TestMethod, TestCategory("AGBM_335")]
    public void QueuedExpiry_FailsBeforeStartWithImmutableTerminalResult()
    {
        var manager = new AgentTaskManager();
        var driver = new Driver();
        var id = manager.Register(driver);
        Assert.IsTrue(manager.RejectBeforeStart(id, new AgentTaskError("SCHEDULER_QUEUE_TIMEOUT", "Expired while waiting.")));
        Assert.AreEqual(AgentTaskState.Failed, manager.Get(id).State);
        Assert.AreEqual("SCHEDULER_QUEUE_TIMEOUT", manager.Get(id).Error.Code);
        Assert.IsFalse(manager.RejectBeforeStart(id, new AgentTaskError("replacement", "must not replace")));
        Assert.AreEqual(AgentTaskCancellationDisposition.AlreadyTerminal, manager.Cancel(id));
        Assert.AreEqual("SCHEDULER_QUEUE_TIMEOUT", manager.Get(id).Error.Code);
        Assert.AreEqual(0, driver.StartCount);
    }

    // TestRecord: Documentation~/test_records/AGBM_336.md
    [TestMethod, TestCategory("AGBM_336")]
    public void RestoreCreated_RetainsIdentityAndDoesNotExecute()
    {
        var manager = new AgentTaskManager();
        var driver = new Driver();
        var id = new AgentTaskId("retained-before-start");
        var createdAt = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        Assert.AreEqual(id, manager.RestoreCreated(id, driver, createdAt));
        Assert.AreEqual(AgentTaskState.Created, manager.Get(id).State);
        Assert.AreEqual(createdAt, manager.Get(id).CreatedAt);
        Assert.AreEqual(0, driver.StartCount);
        manager.Start(id);
        Assert.AreEqual(1, driver.StartCount);
        Assert.AreEqual(AgentTaskCancellationDisposition.Unsupported, manager.Cancel(id));
    }

    private sealed class Driver : IAgentTask
    {
        public int StartCount;
        public int CancelCount;
        public bool CanCancel => false;
        public void Start(IAgentTaskContext context) => StartCount++;
        public void Advance(IAgentTaskContext context) { }
        public void RequestCancellation(IAgentTaskContext context) => CancelCount++;
    }
}
