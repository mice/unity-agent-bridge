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
        Assert.AreEqual("reconnect-required", parsed.Value<string>("lifecycleState"));
        Assert.AreEqual(true, parsed.Value<bool>("reconnectRequired"));
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

    private static string CreateUnityProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnityAgentBridgeCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.0f1");
        return root;
    }
}
