using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class ProjectDiscoveryLifecycleTests
{
    private sealed class Scope : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), nameof(ProjectDiscoveryLifecycleTests), Guid.NewGuid().ToString("N"));
        internal DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-10-22T16:35:34.1234567Z");
        internal ProjectRegistry Registry { get; }
        internal ProjectRegistration Registration { get; }
        internal EditorSessionInfo Session { get; }
        internal string ObservationPath => Path.Combine(Registry.Root, "observed-sessions", Registration.projectId + ".json");
        internal string StatusPath => Path.Combine(Registration.queueRoot, QueueLayout.StatusDirectoryName, QueueLayout.StatusFileName);
        internal Scope(bool register = true, bool verifiedRegistration = true)
        {
            var project = Path.Combine(Root, "files");
            Directory.CreateDirectory(Path.Combine(project, "Assets"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "pure file fixture");
            Registry = new ProjectRegistry(Path.Combine(Root, "registry"));
            Registration = new() { projectId = ProjectIdentityService.GetProjectId(project), projectPath = project, queueRoot = Path.Combine(project, "queue") };
            Session = new() { projectId = Registration.projectId, editorSessionId = "12345:639282837331234567", processId = 12345,
                processStartTime = "2026-10-22T16:35:33.1234567Z", heartbeatUtc = Now.ToString("O"), revision = 1, bridgeEnabled = true };
            if (register) Registry.RegisterSuccessfulConnection(Registration, verifiedRegistration ? Session : new EditorSessionInfo
            { projectId = Registration.projectId, editorSessionId = Session.editorSessionId, bridgeEnabled = true });
        }
        internal void WriteStatus()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);
            File.WriteAllText(StatusPath, JsonConvert.SerializeObject(new { editorSession = Session, isCompiling = false, currentStage = "unity.poller.idle" }));
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DataRow(null)]
    public void DeletedStatusUsesVerifiedOriginalInstanceWithoutInventingExit(bool? aliveAfterDeletion)
    {
        using var scope = new Scope();
        EditorSessionInfo? current = scope.Session;
        bool? alive = true;
        var observedSessions = new List<string>();
        var discovery = new ProjectDiscoveryService(scope.Registry, _ => current, session =>
        { observedSessions.Add(session.editorSessionId); return alive; }, () => scope.Now);
        Assert.IsTrue(discovery.Observe(scope.Registration).ready);
        Assert.IsTrue(File.Exists(scope.ObservationPath));
        current = null;
        alive = aliveAfterDeletion;
        var missing = discovery.Observe(scope.Registration);
        Assert.AreEqual(aliveAfterDeletion == false ? "offline" : "recovering", missing.availability);
        Assert.AreEqual(aliveAfterDeletion == false ? "EDITOR_EXITED" : "SESSION_UNAVAILABLE", missing.reason);
        Assert.IsFalse(missing.ready);
        Assert.AreEqual(scope.Session.editorSessionId, missing.editorSession!.editorSessionId);
        Assert.IsTrue(observedSessions.All(id => id == scope.Session.editorSessionId));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DataRow(null)]
    public void UnreadableStatusAfterRestartObservesPersistedExactInstance(bool? aliveAfterRestart)
    {
        using var scope = new Scope();
        scope.WriteStatus();
        var initial = new ProjectDiscoveryService(scope.Registry, processAlive: _ => true, utcNow: () => scope.Now);
        Assert.IsTrue(initial.Observe(scope.Registration).ready);
        File.WriteAllText(scope.StatusPath, "{not-readable-json");
        EditorSessionInfo? checkedSession = null;
        var restarted = new ProjectDiscoveryService(new ProjectRegistry(scope.Registry.Root), processAlive: session =>
        { checkedSession = session; return aliveAfterRestart; }, utcNow: () => scope.Now);
        var observed = restarted.Observe(scope.Registration);
        Assert.AreEqual(aliveAfterRestart == false ? "offline" : "recovering", observed.availability);
        Assert.AreEqual(aliveAfterRestart == false ? "EDITOR_EXITED" : "SESSION_UNREADABLE", observed.reason);
        Assert.IsFalse(observed.ready);
        Assert.AreEqual(scope.Session.editorSessionId, checkedSession!.editorSessionId);
        Assert.AreEqual(scope.Session.processStartTime, checkedSession.processStartTime);
    }

    [TestMethod]
    public void RestartedDiscoveryPreservesTimestampPrecisionAfterActualStatusDeletion()
    {
        using var scope = new Scope();
        scope.WriteStatus();
        var initial = new ProjectDiscoveryService(scope.Registry, processAlive: _ => true, utcNow: () => scope.Now);
        Assert.IsTrue(initial.Observe(scope.Registration).ready);
        File.Delete(scope.StatusPath);
        EditorSessionInfo? checkedSession = null;
        var restarted = new ProjectDiscoveryService(new ProjectRegistry(scope.Registry.Root), processAlive: session =>
        { checkedSession = session; return false; }, utcNow: () => scope.Now);
        var offline = restarted.Observe(scope.Registration);
        Assert.AreEqual("offline", offline.availability);
        Assert.AreEqual("EDITOR_EXITED", offline.reason);
        Assert.AreEqual("2026-10-22T16:35:33.1234567Z", checkedSession!.processStartTime);
        Assert.AreEqual(scope.Session.heartbeatUtc, checkedSession.heartbeatUtc);
        Assert.AreEqual(scope.Session.editorSessionId, checkedSession.editorSessionId);
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.GetDirectoryName(scope.ObservationPath)!, "*.json").Count());
    }

    [TestMethod]
    public void MissingStatusWithoutPriorVerifiedEvidenceNeverMeansOffline()
    {
        using var scope = new Scope(verifiedRegistration: false);
        var processChecks = 0;
        var discovery = new ProjectDiscoveryService(scope.Registry, _ => null, _ => { processChecks++; return false; }, () => scope.Now);
        var observed = discovery.Observe(scope.Registration);
        Assert.AreEqual("recovering", observed.availability);
        Assert.AreEqual("SESSION_UNAVAILABLE", observed.reason);
        Assert.IsFalse(observed.ready);
        Assert.IsNull(observed.editorSession);
        Assert.AreEqual(0, processChecks);
        Assert.IsFalse(File.Exists(scope.ObservationPath));
    }

    [DataTestMethod]
    [DataRow("{broken-observation")]
    [DataRow("{\"schemaVersion\":\"bad\"}")]
    [DataRow("{\"schemaVersion\":{}}")]
    [DataRow("{\"schemaVersion\":999999999999999999999999999999999}")]
    public void CorruptPersistedObservationIsExplicitAndCannotInventAnExitedInstance(string corrupt)
    {
        using var scope = new Scope(verifiedRegistration: false);
        Directory.CreateDirectory(Path.GetDirectoryName(scope.ObservationPath)!);
        File.WriteAllText(scope.ObservationPath, corrupt);
        var processChecks = 0;
        var discovery = new ProjectDiscoveryService(scope.Registry, _ => null, _ => { processChecks++; return false; }, () => scope.Now);
        var observed = discovery.Observe(scope.Registration);
        Assert.AreEqual("recovering", observed.availability);
        Assert.AreEqual("SESSION_OBSERVATION_CORRUPT", observed.reason);
        Assert.IsNull(observed.editorSession);
        Assert.IsFalse(observed.ready);
        Assert.AreEqual(0, processChecks);
    }

    [DataTestMethod]
    [DataRow(0, "2026-10-22T16:35:33.1234567Z")]
    [DataRow(12345, "invalid-start-time")]
    public void ObservationWithoutReliableProcessIdentityIsNotPersisted(int processId, string processStart)
    {
        using var scope = new Scope(verifiedRegistration: false);
        scope.Session.processId = processId;
        scope.Session.processStartTime = processStart;
        var discovery = new ProjectDiscoveryService(scope.Registry, _ => scope.Session, _ => true, () => scope.Now);
        discovery.Observe(scope.Registration);
        Assert.IsFalse(File.Exists(scope.ObservationPath));
        var restarted = new ProjectDiscoveryService(scope.Registry, _ => null, _ => false, () => scope.Now);
        Assert.AreEqual("recovering", restarted.Observe(scope.Registration).availability);
    }

    [TestMethod]
    public void VerifiedCandidateSurvivesMinimalConnectionRegistrationAndDeletedStatusAfterRestart()
    {
        using var scope = new Scope(register: false);
        scope.WriteStatus();
        var discovery = new ProjectDiscoveryService(scope.Registry, processAlive: _ => true, utcNow: () => scope.Now);
        var candidate = discovery.CreateCandidate(scope.Registration.projectPath, scope.Registration.queueRoot, "connection");
        Assert.AreEqual(0, scope.Registry.List().Count);
        Assert.IsFalse(File.Exists(scope.ObservationPath));
        scope.Registry.RegisterSuccessfulConnection(scope.Registration, new EditorSessionInfo
        { projectId = scope.Registration.projectId, editorSessionId = candidate.EditorSessionId, bridgeEnabled = true });
        discovery.PersistVerifiedObservation(scope.Registration, candidate.EditorSessionId);
        Assert.IsTrue(File.Exists(scope.ObservationPath));
        File.Delete(scope.StatusPath);
        EditorSessionInfo? checkedSession = null;
        var restarted = new ProjectDiscoveryService(new ProjectRegistry(scope.Registry.Root), processAlive: session =>
        { checkedSession = session; return false; }, utcNow: () => scope.Now);
        var observed = restarted.Observe(scope.Registration);
        Assert.AreEqual("offline", observed.availability);
        Assert.AreEqual("EDITOR_EXITED", observed.reason);
        Assert.AreEqual(candidate.EditorSessionId, checkedSession!.editorSessionId);
        Assert.AreEqual(scope.Session.processStartTime, checkedSession.processStartTime);
        Assert.IsFalse(observed.ready);
    }

    [TestMethod]
    public void VerifiedCandidateObservationDoesNotImplicitlyRegisterOrPersistHistory()
    {
        using var scope = new Scope(register: false);
        var discovery = new ProjectDiscoveryService(scope.Registry, _ => scope.Session, _ => true, () => scope.Now);
        Assert.IsTrue(discovery.Observe(scope.Registration).ready);
        Assert.AreEqual(0, scope.Registry.List().Count);
        Assert.IsNull(scope.Registry.Find(scope.Registration.projectId));
        Assert.IsFalse(File.Exists(scope.ObservationPath));
        var restarted = new ProjectDiscoveryService(scope.Registry, _ => null, _ => false, () => scope.Now);
        Assert.AreEqual("recovering", restarted.Observe(scope.Registration).availability);
    }
}
