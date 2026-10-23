using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityAgentBridge.ExternalBridgeClientCore;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public class ProjectIdentityTests
{
    private string _root = null!;
    private string _project = null!;
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "agb-identity-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(_root, "project");
        Directory.CreateDirectory(Path.Combine(_project, "Assets"));
        Directory.CreateDirectory(Path.Combine(_project, "ProjectSettings"));
        File.WriteAllText(Path.Combine(_project, "ProjectSettings", "ProjectVersion.txt"), "fixture");
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);
    private ProjectRegistration Registration() => new() { projectId = ProjectIdentityService.GetProjectId(_project), projectPath = _project, queueRoot = Path.Combine(_project, "queue") };
    private EditorSessionInfo Session(ProjectRegistration registration) => new() { projectId = registration.projectId, editorSessionId = "123:456", bridgeEnabled = true, heartbeatUtc = DateTimeOffset.UtcNow.ToString("O"), revision = 1 };
    [TestMethod] public void IdentitySeparatesCheckoutsAndNormalizesSegments()
    {
        Assert.AreEqual(ProjectIdentityService.GetProjectId(_project), ProjectIdentityService.GetProjectId(Path.Combine(_project, "Assets", "..")));
        var second = Path.Combine(_root, "second");
        Directory.CreateDirectory(Path.Combine(second, "Assets")); Directory.CreateDirectory(Path.Combine(second, "ProjectSettings")); File.WriteAllText(Path.Combine(second, "ProjectSettings", "ProjectVersion.txt"), "fixture");
        Assert.AreNotEqual(ProjectIdentityService.GetProjectId(_project), ProjectIdentityService.GetProjectId(second));
        if (OperatingSystem.IsWindows()) Assert.AreEqual(ProjectIdentityService.GetProjectId(_project), ProjectIdentityService.GetProjectId(_project.ToUpperInvariant()));
        Assert.ThrowsException<DirectoryNotFoundException>(() => ProjectIdentityService.GetProjectId(_root));
    }
    [TestMethod] public void AliasUsesPhysicalIdentity()
    {
        var alias = Path.Combine(_root, "alias");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + alias + "\" \"" + _project + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            process!.WaitForExit(); Assert.AreEqual(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(alias, _project);
        Assert.AreEqual(ProjectIdentityService.GetProjectId(_project), ProjectIdentityService.GetProjectId(alias));
        Directory.Delete(alias);
    }
    [TestMethod] public void RegistryRetainsOfflineAndIgnoresCorruptSibling()
    {
        var registry = new ProjectRegistry(Path.Combine(_root, "registry")); var r = Registration();
        registry.RegisterSuccessfulConnection(r, Session(r));
        File.WriteAllText(Path.Combine(registry.Root, "project-corrupt.json"), "{");
        Assert.AreEqual(1, registry.List().Count);
        Assert.AreEqual(r.queueRoot, registry.Find(r.projectId)!.queueRoot);
        var discovery = new ProjectDiscoveryService(registry, _ => Session(r), _ => false);
        Assert.AreEqual(0, discovery.List(false).Count);
        Assert.AreEqual("offline", discovery.List(true)[0].availability);
    }
    [TestMethod] public void StaleHeartbeatDoesNotInventProcessExit()
    {
        var r = Registration(); var session = Session(r); session.heartbeatUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root,"registry")), _ => session, _ => true);
        var observed = discovery.Observe(r);
        Assert.AreEqual("recovering", observed.availability); Assert.AreEqual("HEARTBEAT_STALE", observed.reason); Assert.IsFalse(observed.ready);
    }
    [TestMethod] public void FreshIdentityAndEnabledBridgeRequired()
    {
        var r = Registration(); var session = Session(r); var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root,"registry")), _ => session, _ => true);
        Assert.IsTrue(discovery.Observe(r).ready);
        session.projectId = "wrong"; Assert.IsFalse(discovery.Observe(r).ready);
        session.projectId = r.projectId; session.bridgeEnabled = false; Assert.IsFalse(discovery.Observe(r).ready);
    }
    [TestMethod] public void ContextCopiesIdentityAndCommandStoreStampsTarget()
    {
        var r = Registration(); var session = Session(r); var context = new ProjectContext(r,session,new QueuePaths(_project,"queue"),"connection");
        session.editorSessionId = "changed"; r.projectId = "changed";
        Assert.AreEqual("123:456",context.EditorSessionId);
        CommandStore.EnsureQueueDirectories(context.QueuePaths);
        new CommandStore().WriteInboxAtomic(context.QueuePaths,"command",AgentCommandEnvelope.Build("command","ping",1000,"{}"));
        var parsed = JsonUtil.ExtractCommand(File.ReadAllText(Path.Combine(context.QueuePaths.InboxDirectory,"command.json")));
        Assert.IsTrue(parsed.Success); Assert.AreEqual(context.ProjectId,parsed.Command.projectId); Assert.AreEqual(context.EditorSessionId,parsed.Command.editorSessionId);
        Assert.IsNull(CommandTargetValidator.Validate(parsed.Command,context.ProjectId,context.EditorSessionId));
        Assert.AreEqual("EDITOR_SESSION_MISMATCH",CommandTargetValidator.Validate(parsed.Command,context.ProjectId,"new-instance"));
    }
    [TestMethod] public void RegressedHeartbeatAndConflictingLiveInstanceCannotDispatch()
    {
        var r = Registration(); var session = Session(r); session.revision = 10;
        var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root,"registry")), _ => session, _ => true);
        Assert.IsTrue(discovery.Observe(r).ready);
        session.revision = 9; Assert.AreEqual("SESSION_REVISION_REGRESSED",discovery.Observe(r).reason);
        session.revision = 11; session.editorSessionId = "new"; Assert.AreEqual("SESSION_IDENTITY_CONFLICT",discovery.Observe(r).reason);
    }
    [TestMethod] public void CommandStoreRejectsConflictingExplicitIdentity()
    {
        var r = Registration(); var context = new ProjectContext(r,Session(r),new QueuePaths(_project,"queue"));
        CommandStore.EnsureQueueDirectories(context.QueuePaths);
        var envelope = AgentCommandEnvelope.Build("conflict","ping",1000,"{}",1,"other","123:456");
        Assert.ThrowsException<InvalidOperationException>(() => new CommandStore().WriteInboxAtomic(context.QueuePaths,"conflict",envelope));
        Assert.AreEqual(0,Directory.GetFiles(context.QueuePaths.InboxDirectory).Length);
    }
    [TestMethod] public void CandidateValidationDoesNotRegisterBeforeConnection()
    {
        var r = Registration(); var registry = new ProjectRegistry(Path.Combine(_root,"registry"));
        var discovery = new ProjectDiscoveryService(registry, _ => Session(r), _ => true);
        Assert.AreEqual(r.projectId,discovery.CreateCandidate(_project).ProjectId);
        Assert.AreEqual(0,registry.List().Count);
    }
    [TestMethod] public void TargetProtocolRejectsMalformedAndFutureRecords()
    {
        var envelope = JObject.Parse(AgentCommandEnvelope.Build("c","ping",1000,"{}")); envelope["targetSchemaVersion"] = "invalid";
        Assert.IsFalse(JsonUtil.ExtractCommand(envelope.ToString()).Success);
        envelope["targetSchemaVersion"] = 1; Assert.IsFalse(JsonUtil.ExtractCommand(envelope.ToString()).Success);
        Assert.AreEqual("PROJECT_PROTOCOL_UNSUPPORTED",CommandTargetValidator.Validate(new AgentCommand { targetSchemaVersion = 2 },"p","s"));
        Assert.IsNull(CommandTargetValidator.Validate(new AgentCommand(),"p","s"));
    }
    [DataTestMethod]
    [DataRow("2026-10-22T16:35:33.8040696Z")]
    [DataRow("2026-10-23T00:35:33.8040696+08:00")]
    public void StatusFileRoundTripPreservesCreationTimePrecisionAndOffset(string creationTime)
    {
        var registration = Registration();
        var session = Session(registration);
        session.processStartTime = creationTime;
        Directory.CreateDirectory(Path.Combine(registration.queueRoot, "status"));
        File.WriteAllText(Path.Combine(registration.queueRoot, "status", "unity_bridge_status.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(new { editorSession = session }));
        var observedByProcessProbe = string.Empty;
        var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root, "registry")),
            processAlive: value => { observedByProcessProbe = value.processStartTime; return true; });
        var observed = discovery.Observe(registration);
        Assert.IsTrue(observed.ready);
        Assert.AreEqual(creationTime, observedByProcessProbe);
        Assert.AreEqual(639282837338040696L, DateTimeOffset.Parse(observedByProcessProbe).UtcTicks);
        Assert.AreEqual(creationTime, observed.editorSession.processStartTime);
    }

    [TestMethod] public void DiskSessionForExactCurrentProcessRemainsOnline()
    {
        var registration = Registration();
        var session = Session(registration);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        session.processId = process.Id;
        session.processStartTime = process.StartTime.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        session.editorSessionId = process.Id + ":" + process.StartTime.ToUniversalTime().Ticks;
        Directory.CreateDirectory(Path.Combine(registration.queueRoot, "status"));
        File.WriteAllText(Path.Combine(registration.queueRoot, "status", "unity_bridge_status.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(new { editorSession = session }));
        var discovery = new ProjectDiscoveryService(new ProjectRegistry(Path.Combine(_root, "registry")));
        var observed = discovery.Observe(registration);
        Assert.IsTrue(observed.ready, observed.reason);
        Assert.AreEqual("online", observed.availability);
        Assert.AreEqual(session.processStartTime, observed.editorSession.processStartTime);
    }

    [DataTestMethod]
    [DataRow("2026-10-22T16:35:33.8040696Z")]
    [DataRow("2026-10-23T00:35:33.8040696+08:00")]
    public void TargetStampPreservesBusinessTimestampLiteral(string timestamp)
    {
        var paths = new QueuePaths(_project, "queue", "project-fixture", "session-fixture");
        CommandStore.EnsureQueueDirectories(paths);
        var args = Newtonsoft.Json.JsonConvert.SerializeObject(new { timestamp });
        new CommandStore().WriteInboxAtomic(paths, "timestamp", AgentCommandEnvelope.Build("timestamp", "ping", 1000, args));
        var persisted = File.ReadAllText(Path.Combine(paths.InboxDirectory, "timestamp.json"));
        var command = JsonUtil.ExtractCommand(persisted);
        Assert.IsTrue(command.Success);
        Assert.AreEqual(args, command.Command.rawArgsJson);
    }

}
