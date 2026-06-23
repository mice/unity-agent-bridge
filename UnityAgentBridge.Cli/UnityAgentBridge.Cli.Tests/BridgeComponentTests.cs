using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Text;
using UnityAgentBridge.Cli;
using UnityAgentBridge.Cli.Diagnostics;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Cli.Tests;

[TestClass]
public sealed class BridgeComponentTests
{
    [TestMethod]
    public void QueuePathsResolvesRelativeQueueRootUnderProject()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");

        Assert.AreEqual(Path.Combine(root, "Temp", "AgentBridge"), paths.QueueRoot);
        Assert.AreEqual(Path.Combine(root, "Temp", "AgentBridge", "inbox"), paths.InboxDirectory);
    }

    [TestMethod]
    public void CommandEnvelopeContainsCompatibleShape()
    {
        var json = AgentCommandEnvelope.Build("abc", "unity.ping", 5000, "{}");
        var parsed = JObject.Parse(json);

        Assert.AreEqual("1.0", parsed.Value<string>("schemaVersion"));
        Assert.AreEqual("abc", parsed.Value<string>("commandId"));
        Assert.AreEqual("unity.ping", parsed.Value<string>("tool"));
        Assert.AreEqual(5000, parsed.Value<int>("timeoutMs"));
        Assert.IsNotNull(parsed["createdAt"]);
        Assert.IsTrue(JToken.DeepEquals(new JObject(), parsed["args"]));
    }

    [TestMethod]
    public async Task WaitForResultReturnsSyntheticOutboxJson()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        File.WriteAllText(Path.Combine(paths.OutboxDirectory, "abc.result.json"), """{"status":"success","value":1}""");

        var result = await new CommandStore().WaitForResultAsync(paths, "abc", 1000, CancellationToken.None);

        Assert.AreEqual("success", result.Status);
        Assert.IsFalse(result.IsUnknownStatus);
    }

    [TestMethod]
    public void BridgeHealthDoesNotRequireUnityEditor()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var spec = new BridgeCommandSpec("unity.bridge_health", 5000, "{}");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "health", out var result);

        Assert.IsTrue(handled);
        Assert.AreEqual("success", result.Status);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual(false, parsed.Value<bool>("statusFileExists"));
        Assert.AreEqual("degraded", parsed.Value<string>("lifecycleState"));
        Assert.AreEqual("UnityUnavailable", parsed.Value<string>("healthReason"));
        Assert.AreEqual(true, parsed.Value<bool>("reconnectRequired"));
        Assert.AreEqual("Reconnect", parsed.Value<string>("recommendedActionCode"));
        Assert.AreEqual("BlockedBeforeDispatch", parsed.Value<string>("toolExecution"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(parsed.Value<string>("recommendedAction")));
        Assert.IsNotNull(parsed["currentCompileEpoch"]);
        Assert.IsNotNull(parsed["compileLifecycleStage"]);
        Assert.IsNotNull(parsed["projectPath"]);
        Assert.IsNotNull(parsed["staleProjectBindingKind"]);
        Assert.IsNotNull(parsed["staleDetectedProjectPath"]);
    }

    [TestMethod]
    public void BridgeHealthIncludesProjectBindingEvidence()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        Directory.CreateDirectory(paths.StatusDirectory);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"),
            "{\"projectPath\":\"" + root.Replace("\\", "/") + "\",\"staleProjectBindingKind\":\"bound\",\"staleDetectedProjectPath\":\"" + root.Replace("\\", "/") + "\"}");
        var spec = new BridgeCommandSpec("unity.bridge_health", 5000, "{}");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "health-binding", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("bound", parsed.Value<string>("staleProjectBindingKind"));
        Assert.AreEqual(root.Replace("\\", "/"), parsed.Value<string>("staleDetectedProjectPath"));
    }

    [TestMethod]
    public void BridgeHealthProjectMismatchReportsLayeredDegradedStatus()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        WriteStatus(paths,
            "{\"heartbeatUtc\":\"" + DateTime.UtcNow.ToString("O") + "\",\"currentStage\":\"unity.poller.idle\",\"projectPath\":\"" + root.Replace("\\", "/") + "\",\"staleProjectBindingKind\":\"explicit\",\"staleConfiguredProjectPath\":\"C:/OtherProject\",\"staleDetectedProjectPath\":\"" + root.Replace("\\", "/") + "\"}");
        var spec = new BridgeCommandSpec("unity.bridge_health", 5000, "{}");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "health-mismatch", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("degraded", parsed.Value<string>("lifecycleState"));
        Assert.AreEqual("ProjectMismatch", parsed.Value<string>("healthReason"));
        Assert.AreEqual("UpdateConfig", parsed.Value<string>("recommendedActionCode"));
        Assert.AreEqual("BlockedBeforeDispatch", parsed.Value<string>("toolExecution"));
        Assert.AreEqual(true, parsed.Value<bool>("reconnectRequired"));
    }

    [TestMethod]
    public async Task ExecuteAsync_ProjectMismatchBlocksBeforeDispatch()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        WriteStatus(paths,
            "{\"heartbeatUtc\":\"" + DateTime.UtcNow.ToString("O") + "\",\"currentStage\":\"unity.poller.idle\",\"projectPath\":\"" + root.Replace("\\", "/") + "\",\"staleProjectBindingKind\":\"explicit\",\"staleConfiguredProjectPath\":\"C:/OtherProject\",\"staleDetectedProjectPath\":\"" + root.Replace("\\", "/") + "\"}");

        var result = await new ExternalBridgeClient().ExecuteAsync(
            paths,
            "cmd-blocked",
            new BridgeCommandSpec("unity.ping", 5000, "{}"),
            CancellationToken.None);

        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("blocked", result.Status);
        Assert.AreEqual("degraded", parsed.Value<string>("lifecycleState"));
        Assert.AreEqual("ProjectMismatch", parsed.Value<string>("healthReason"));
        Assert.AreEqual("BlockedBeforeDispatch", parsed.Value<string>("toolExecution"));
        Assert.AreEqual(false, File.Exists(Path.Combine(paths.InboxDirectory, "cmd-blocked.json")));
    }

    [TestMethod]
    public async Task ExecuteAsync_UnityUnavailableBlocksBeforeDispatch()
    {
        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);

        var result = await new ExternalBridgeClient().ExecuteAsync(
            paths,
            "cmd-unavailable",
            new BridgeCommandSpec("unity.ping", 5000, "{}"),
            CancellationToken.None);

        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("blocked", result.Status);
        Assert.AreEqual("degraded", parsed.Value<string>("lifecycleState"));
        Assert.AreEqual("UnityUnavailable", parsed.Value<string>("healthReason"));
        Assert.AreEqual("Reconnect", parsed.Value<string>("recommendedActionCode"));
        Assert.AreEqual("BlockedBeforeDispatch", parsed.Value<string>("toolExecution"));
        Assert.AreEqual(false, File.Exists(Path.Combine(paths.InboxDirectory, "cmd-unavailable.json")));
    }

    [TestMethod]
    public async Task RunAsync_BridgeHealth_ReturnsExitCodeZero()
    {
        var projectRoot = CreateUnityProject();
        var exitCode = await AgentBridgeCli.RunAsync(
            ["bridge-health", "--project-path", projectRoot],
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public async Task RunAsync_BridgeHealth_DefaultJsonWritesJsonPayload()
    {
        var projectRoot = CreateUnityProject();
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        var originalStdout = CliJsonWriter.Stdout;
        var originalStderr = CliJsonWriter.Stderr;

        try
        {
            CliJsonWriter.Stdout = stdout;
            CliJsonWriter.Stderr = stderr;

            var exitCode = await AgentBridgeCli.RunAsync(
                ["bridge-health", "--project-path", projectRoot],
                CancellationToken.None);

            Assert.AreEqual(0, exitCode);
            var payload = stdout.ToString().Trim();
            Assert.IsTrue(payload.StartsWith("{", StringComparison.Ordinal));
            Assert.AreEqual(string.Empty, stderr.ToString());
        }
        finally
        {
            CliJsonWriter.Stdout = originalStdout;
            CliJsonWriter.Stderr = originalStderr;
        }
    }

    [TestMethod]
    public async Task RunAsync_BridgeHealth_TextOutputWritesHumanReadableSummary()
    {
        var projectRoot = CreateUnityProject();
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        var originalStdout = CliJsonWriter.Stdout;
        var originalStderr = CliJsonWriter.Stderr;

        try
        {
            CliJsonWriter.Stdout = stdout;
            CliJsonWriter.Stderr = stderr;

            var exitCode = await AgentBridgeCli.RunAsync(
                ["bridge-health", "--project-path", projectRoot, "--output", "text"],
                CancellationToken.None);

            Assert.AreEqual(0, exitCode);
            var payload = stdout.ToString();
            StringAssert.Contains(payload, "unity.bridge_health: success");
            StringAssert.Contains(payload, "Bridge health collected.");
            Assert.AreEqual(string.Empty, stderr.ToString());
        }
        finally
        {
            CliJsonWriter.Stdout = originalStdout;
            CliJsonWriter.Stderr = originalStderr;
        }
    }

    [TestMethod]
    public async Task RunAsync_UnsupportedOutputValueReturnsExitCodeThreeAndStderr()
    {
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        var originalStdout = CliJsonWriter.Stdout;
        var originalStderr = CliJsonWriter.Stderr;

        try
        {
            CliJsonWriter.Stdout = stdout;
            CliJsonWriter.Stderr = stderr;

            var exitCode = await AgentBridgeCli.RunAsync(
                ["compile", "--output", "xml"],
                CancellationToken.None);

            Assert.AreEqual(3, exitCode);
            Assert.AreEqual(string.Empty, stdout.ToString());
            StringAssert.Contains(stderr.ToString(), "--output must be one of: json, text.");
        }
        finally
        {
            CliJsonWriter.Stdout = originalStdout;
            CliJsonWriter.Stderr = originalStderr;
        }
    }

    [TestMethod]
    public void ExitCodeMappingMatchesContract()
    {
        Assert.AreEqual(0, ExitCodeMapper.Map("success"));
        Assert.AreEqual(1, ExitCodeMapper.Map("failed"));
        Assert.AreEqual(2, ExitCodeMapper.Map("timeout"));
        Assert.AreEqual(3, ExitCodeMapper.Map("invalid_args"));
        Assert.AreEqual(4, ExitCodeMapper.Map("unsupported"));
        Assert.AreEqual(5, ExitCodeMapper.Map("blocked"));
        Assert.AreEqual(6, ExitCodeMapper.Map("exception"));
        Assert.AreEqual(7, ExitCodeMapper.Map("cancelled"));
        Assert.AreEqual(6, ExitCodeMapper.Map("mystery"));
    }

    [TestMethod]
    public async Task RunAsync_InvalidAssetSearchArgs_ReturnsExitCodeThree()
    {
        var projectRoot = CreateUnityProject();
        var exitCode = await AgentBridgeCli.RunAsync(
            ["assetdatabase_search", "--query", "t:Scene", "--limit", "0", "--project-path", projectRoot],
            CancellationToken.None);

        Assert.AreEqual(3, exitCode);
    }

    [TestMethod]
    public void WindowsProcessDiscovery_ParsesQuotedProjectPath()
    {
        var projectPath = @"D:\ProjCommon\Unity Project";
        var commandLine = $@"""C:\Program Files\Unity\Hub\Editor\2022.3.0f1\Editor\Unity.exe"" -projectPath ""{projectPath}"" -someFlag";

        var parsed = WindowsUnityEditorProcessDiscovery.ParseProjectPathFromCommandLine(commandLine);

        Assert.AreEqual(projectPath, parsed);
    }

    [TestMethod]
    public void ProjectVersionReader_ReadsVersionFromProjectVersionFile()
    {
        var root = CreateUnityProject();

        var success = UnityEditorProjectVersionReader.TryReadVersion(root, out var version, out var code);

        Assert.IsTrue(success);
        Assert.AreEqual("2022.3.0f1", version);
        Assert.IsNull(code);
    }

    [TestMethod]
    public void LaunchSettings_RejectsInvalidOverride()
    {
        Assert.ThrowsException<BridgeCommandValidationException>(() =>
            UnityEditorLaunchSettings.ResolveMaxRunningEditors(0));
    }

    [TestMethod]
    public void LaunchSettings_ReadsEnvironmentOverride()
    {
        Environment.SetEnvironmentVariable(UnityEditorLaunchSettings.MaxRunningEditorsEnvironmentVariable, "7");
        try
        {
            var resolved = UnityEditorLaunchSettings.ResolveMaxRunningEditors(null);
            Assert.AreEqual(7, resolved.Limit);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UnityEditorLaunchSettings.MaxRunningEditorsEnvironmentVariable, null);
        }
    }

    [TestMethod]
    public void LaunchSettings_UsesConfigFallbackAndWarnsOnInvalidConfig()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "UnityEditorLaunchSettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var configPath = Path.Combine(tempRoot, "Start-Codex-With-UnityMcp.json");
        File.WriteAllText(configPath, """{"unityProjectPath":"D:/Proj","maxRunningUnityEditors":5}""");
        Environment.SetEnvironmentVariable(UnityEditorLaunchSettings.LauncherConfigEnvironmentVariable, configPath);
        try
        {
            var resolved = UnityEditorLaunchSettings.ResolveMaxRunningEditors(null);
            Assert.AreEqual(5, resolved.Limit);
            Assert.AreEqual(0, resolved.Warnings.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UnityEditorLaunchSettings.LauncherConfigEnvironmentVariable, null);
            Directory.Delete(tempRoot, true);
        }
    }

    [TestMethod]
    public void UnityEditorList_OnUnsupportedPlatformReturnsDeterministicFailure()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unsupported platform behavior is only asserted on non-Windows runtimes.");
        }

        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var spec = new BridgeCommandSpec("unity.editor_list", 5000, "{}");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "editor-list", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("UnsupportedPlatform", parsed.Value<string>("code"));
    }

    [TestMethod]
    public void UnityEditorOpen_MissingProjectPathReturnsProjectPathMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-first validation.");
        }

        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var spec = new BridgeCommandSpec("unity.editor_open", 5000, """{}""");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "editor-open-missing", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("ProjectPathMissing", parsed.Value<string>("code"));
    }

    [TestMethod]
    public void UnityEditorOpen_NonUnityProjectReturnsNotUnityProject()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-first validation.");
        }

        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var tempFolder = Path.Combine(Path.GetTempPath(), "NonUnityProject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        var spec = new BridgeCommandSpec("unity.editor_open", 5000, $$"""{"projectPath":"{{tempFolder.Replace("\\", "\\\\")}}"}""");

        try
        {
            var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "editor-open-nonunity", out var result);

            Assert.IsTrue(handled);
            var parsed = JObject.Parse(result.RawJson);
            Assert.AreEqual("NotUnityProject", parsed.Value<string>("code"));
        }
        finally
        {
            Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void UnityEditorOpen_LockFileReturnsProjectLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-first validation.");
        }

        var root = CreateUnityProject();
        Directory.CreateDirectory(Path.Combine(root, "Temp"));
        File.WriteAllText(Path.Combine(root, "Temp", "UnityLockfile"), "locked");
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var spec = new BridgeCommandSpec("unity.editor_open", 5000, $$"""{"projectPath":"{{root.Replace("\\", "\\\\")}}"}""");

        var handled = new BridgeHealthClient().TryHandleLocalCommand(paths, spec, "editor-open-locked", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("ProjectLocked", parsed.Value<string>("code"));
    }

    [TestMethod]
    public void UnityEditorOpen_DuplicateProjectReturnsAlreadyOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-first validation.");
        }

        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var client = new BridgeHealthClient(new FakeDiscovery(
        [
            new UnityEditorInstance(1234, @"C:\Program Files\Unity\Hub\Editor\2022.3.0f1\Editor\Unity.exe", root, "2022.3.0f1", Array.Empty<string>())
        ]));
        var spec = new BridgeCommandSpec("unity.editor_open", 5000, $$"""{"projectPath":"{{root.Replace("\\", "\\\\")}}"}""");

        var handled = client.TryHandleLocalCommand(paths, spec, "editor-open-duplicate", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("UnityAlreadyOpenForProject", parsed.Value<string>("code"));
        Assert.AreEqual(1234, parsed.Value<int?>("processId"));
    }

    [TestMethod]
    public void UnityEditorOpen_EditorLimitReachedReturnsDeterministicCode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-first validation.");
        }

        var root = CreateUnityProject();
        var paths = new QueuePaths(root, "Temp/AgentBridge");
        CommandStore.EnsureQueueDirectories(paths);
        var client = new BridgeHealthClient(new FakeDiscovery(
        [
            new UnityEditorInstance(1111, @"C:\Unity\2022.3.0f1\Editor\Unity.exe", @"D:\ProjA", "2022.3.0f1", Array.Empty<string>()),
            new UnityEditorInstance(2222, @"C:\Unity\2022.3.0f1\Editor\Unity.exe", @"D:\ProjB", "2022.3.0f1", Array.Empty<string>())
        ]));
        var spec = new BridgeCommandSpec("unity.editor_open", 5000, $$"""{"projectPath":"{{root.Replace("\\", "\\\\")}}","maxRunningUnityEditors":2}""");

        var handled = client.TryHandleLocalCommand(paths, spec, "editor-open-limit", out var result);

        Assert.IsTrue(handled);
        var parsed = JObject.Parse(result.RawJson);
        Assert.AreEqual("UnityEditorLimitReached", parsed.Value<string>("code"));
        Assert.AreEqual(2, parsed.Value<int?>("runningEditorCount"));
        Assert.AreEqual(2, parsed.Value<int?>("configuredLimit"));
    }

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }

    private static void WriteStatus(QueuePaths paths, string json)
    {
        Directory.CreateDirectory(paths.StatusDirectory);
        File.WriteAllText(Path.Combine(paths.StatusDirectory, "unity_bridge_status.json"), json);
    }

    private sealed class FakeDiscovery : IUnityEditorProcessDiscovery
    {
        private readonly IReadOnlyList<UnityEditorInstance> _instances;

        public FakeDiscovery(IReadOnlyList<UnityEditorInstance> instances)
        {
            _instances = instances;
        }

        public IReadOnlyList<UnityEditorInstance> Discover()
        {
            return _instances;
        }
    }
}
