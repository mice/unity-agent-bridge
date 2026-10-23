using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class ProjectDiscoveryReadRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
    private sealed class Registry : IProjectRegistry
    {
        public IReadOnlyList<ProjectRegistration> List() => Array.Empty<ProjectRegistration>();
        public ProjectRegistration? Find(string id) => null;
        public void RegisterSuccessfulConnection(ProjectRegistration registration, EditorSessionInfo session)
            => throw new AssertFailedException("Observation cannot register a project.");
    }
    private static ProjectRegistration Registration() => new()
        { projectId = "A", projectPath = Path.Combine(Path.GetTempPath(), "read-retry-A"), queueRoot = Path.Combine(Path.GetTempPath(), "read-retry-A", "queue") };
    private static EditorSessionInfo Session(long revision) => new()
        { projectId = "A", editorSessionId = "session-A", revision = revision, bridgeEnabled = true,
            heartbeatUtc = Now.ToString("O"), capabilityRevision = "catalog-" + revision };
    private static Exception WindowsError(int code) => code == 5 ? new UnauthorizedAccessException("Injected Windows access conflict")
        : new IOException("Injected Windows read error", unchecked((int)0x80070000) | code);

    [DataTestMethod]
    [DataRow(5)]
    [DataRow(32)]
    [DataRow(33)]
    public void TransientSharingFailureRereadsCurrentSessionInsteadOfRestoringCachedReady(int code)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows sharing/access retry boundary.");
        var reads = 0;
        var discovery = new ProjectDiscoveryService(new Registry(), _ =>
        {
            var attempt = Interlocked.Increment(ref reads);
            return attempt switch { 1 => Session(1), 2 => throw WindowsError(code), _ => Session(2) };
        }, _ => true, () => Now);
        var registration = Registration();
        Assert.IsTrue(discovery.Observe(registration).ready);
        var recovered = discovery.Observe(registration);
        Assert.IsTrue(recovered.ready, recovered.reason);
        Assert.AreEqual(3, reads);
        Assert.AreEqual(2L, recovered.editorSession.revision);
        Assert.AreEqual("catalog-2", recovered.editorSession.capabilityRevision, "Recovery must use the newly read complete session, not prior readiness.");
    }

    [TestMethod]
    public async Task PersistentSharingFailureIsBoundedAndNeverRestoresPriorReady()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows sharing/access retry boundary.");
        var fail = false;
        var failedReads = 0;
        var discovery = new ProjectDiscoveryService(new Registry(), _ =>
        {
            if (!fail) return Session(1);
            Interlocked.Increment(ref failedReads);
            throw WindowsError(32);
        }, _ => true, () => Now);
        var registration = Registration();
        Assert.IsTrue(discovery.Observe(registration).ready);
        fail = true;
        var rejected = await Task.Run(() => discovery.Observe(registration)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(rejected.ready);
        Assert.AreEqual("SESSION_UNREADABLE", rejected.reason);
        Assert.AreEqual("recovering", rejected.availability);
        Assert.AreEqual(1L, rejected.editorSession.revision, "Prior identity may explain recovery, but cannot authorize readiness.");
        Assert.IsTrue(failedReads > 1 && failedReads <= 64, "The short retry is bounded; it must not spin indefinitely or promote cached readiness.");
    }

    [DataTestMethod]
    [DataRow("json")]
    [DataRow("missing")]
    [DataRow("io")]
    public void CorruptMissingAndNonSharingFailuresAreNotRetried(string kind)
    {
        var reads = 0;
        var discovery = new ProjectDiscoveryService(new Registry(), _ =>
        {
            Interlocked.Increment(ref reads);
            Exception failure = kind switch
            {
                "json" => new JsonReaderException("Malformed heartbeat JSON"),
                "missing" => new FileNotFoundException("Heartbeat missing"),
                _ => new IOException("Non-sharing device error", unchecked((int)0x8007001f))
            };
            throw failure;
        }, _ => true, () => Now);
        var rejected = discovery.Observe(Registration());
        Assert.IsFalse(rejected.ready);
        Assert.AreEqual("SESSION_UNREADABLE", rejected.reason);
        Assert.AreEqual(1, reads, "Malformed/missing/non-sharing evidence must fail directly.");
    }
}
