using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class ProjectDiscoveryConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
    private sealed class Registry : IProjectRegistry
    {
        public IReadOnlyList<ProjectRegistration> List() => Array.Empty<ProjectRegistration>();
        public ProjectRegistration? Find(string id) => null;
        public void RegisterSuccessfulConnection(ProjectRegistration registration, EditorSessionInfo session)
            => throw new AssertFailedException("Observation must not register a project.");
    }
    private static ProjectRegistration Registration(string id) => new()
        { projectId = id, projectPath = Path.Combine(Path.GetTempPath(), "discovery-concurrency", id), queueRoot = Path.Combine(Path.GetTempPath(), "discovery-concurrency", id, "queue") };
    private static EditorSessionInfo Session(string id, long revision) => new()
        { projectId = id, editorSessionId = "session-" + id, revision = revision, bridgeEnabled = true, heartbeatUtc = Now.ToString("O"), capabilityRevision = "catalog" };

    [TestMethod]
    public void SameProjectObservationsSerializeReadValidationAndSnapshotPublication()
    {
        using var firstRead = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        using var secondAttempt = new ManualResetEventSlim();
        var reads = 0;
        var registration = Registration("A");
        var discovery = new ProjectDiscoveryService(new Registry(), _ =>
        {
            var revision = Interlocked.Increment(ref reads);
            if (revision == 1)
            {
                firstRead.Set();
                if (!releaseRead.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("First read was not released.");
            }
            return Session("A", revision == 3 ? 1 : revision);
        }, _ => true, () => Now);
        ProjectSessionInfo? first = null, second = null;
        Exception? firstError = null, secondError = null;
        var firstThread = new Thread(() => { try { first = discovery.Observe(registration); } catch (Exception e) { firstError = e; } });
        var secondThread = new Thread(() => { secondAttempt.Set(); try { second = discovery.Observe(registration); } catch (Exception e) { secondError = e; } });
        firstThread.IsBackground = secondThread.IsBackground = true;
        try
        {
            firstThread.Start();
            Assert.IsTrue(firstRead.Wait(TimeSpan.FromSeconds(5)), "First observer must enter the controlled read.");
            secondThread.Start();
            Assert.IsTrue(secondAttempt.Wait(TimeSpan.FromSeconds(5)));
            // Wait for an observable scheduling outcome, not a sleep intended to make the race likely.
            Assert.IsTrue(SpinWait.SpinUntil(() => (secondThread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0, TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, Volatile.Read(ref reads), "Second observation must block before reading the same project's newer revision.");
        }
        finally
        {
            releaseRead.Set();
            Assert.IsTrue(firstThread.Join(TimeSpan.FromSeconds(5)));
            if ((secondThread.ThreadState & ThreadState.Unstarted) == 0) Assert.IsTrue(secondThread.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.IsNull(firstError); Assert.IsNull(secondError);
        Assert.IsTrue(first!.ready, first.reason); Assert.AreEqual(1L, first.editorSession.revision);
        Assert.IsTrue(second!.ready, second.reason); Assert.AreEqual(2L, second.editorSession.revision);
        var stale = discovery.Observe(registration);
        Assert.IsFalse(stale.ready);
        Assert.AreEqual("SESSION_REVISION_REGRESSED", stale.reason);
        Assert.AreEqual(2L, stale.editorSession.revision, "Serialization must retain the newer snapshot and preserve stale-state rejection.");
    }

    [TestMethod]
    public async Task BlockedProjectObservationDoesNotSerializeAnIndependentProject()
    {
        using var firstRead = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        var discovery = new ProjectDiscoveryService(new Registry(), registration =>
        {
            if (registration.projectId == "A")
            {
                firstRead.Set();
                if (!releaseRead.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Project A was not released.");
            }
            return Session(registration.projectId, 1);
        }, _ => true, () => Now);
        var blocked = Task.Factory.StartNew(() => discovery.Observe(Registration("A")), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.IsTrue(firstRead.Wait(TimeSpan.FromSeconds(5)));
            var independent = await Task.Factory.StartNew(() => discovery.Observe(Registration("B")), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(independent.ready, independent.reason);
            Assert.AreEqual("B", independent.editorSession.projectId);
            Assert.IsFalse(blocked.IsCompleted, "Project B must finish while A's read is still held.");
        }
        finally { releaseRead.Set(); await blocked.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
