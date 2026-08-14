using System;
using System.IO;
using NUnit.Framework;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class MachineRuntimeSelectionTests
    {
        private string _tempDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            AgentBridgeBootstrap.SetSuppressStartForTests(true);
            AgentBridgeBootstrap.Reconfigure();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AgentBridgeBootstrap.SetSuppressStartForTests(false);
        }

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_201.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_201")]
        public void MachineSelection_RoundTripsExactVersionAndChannel()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            store.Save(new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "1.2.3",
                RuntimeChannel = "preview",
                MachineRuntimeRoot = " D:/AgentBridge "
            });

            var loaded = store.Load();
            Assert.That(loaded.RuntimeMode, Is.EqualTo("machine"));
            Assert.That(loaded.RuntimeVersion, Is.EqualTo("1.2.3"));
            Assert.That(loaded.RuntimeChannel, Is.EqualTo("preview"));
            Assert.That(loaded.MachineRuntimeRoot, Is.EqualTo("D:/AgentBridge"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_202.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_202")]
        public void MachineSelection_InvalidChannelFallsBackToExactOnly()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            store.Save(new McpEditorSettings { RuntimeMode = "machine", RuntimeVersion = "1.2.3", RuntimeChannel = "unsupported" });

            var loaded = store.Load();
            Assert.That(loaded.RuntimeMode, Is.EqualTo("machine"));
            Assert.That(loaded.RuntimeVersion, Is.EqualTo("1.2.3"));
            Assert.That(loaded.RuntimeChannel, Is.Empty);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_203.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_203")]
        public void MachineSelection_RepeatedSaveRemainsSingular()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            var settings = new McpEditorSettings { RuntimeMode = "machine", RuntimeVersion = "1.2.3", RuntimeChannel = "stable" };
            store.Save(settings);
            store.Save(settings);

            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"runtimeVersion\": \"1.2.3\""));
            Assert.That(json.Split(new[] { "runtimeVersion" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
        }

        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_204")]
        public void MachineSelection_FlatReleaseRuntimeResolvesExecutableAndLauncher()
        {
            var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");
            var versionRoot = Path.Combine(managerRoot, "versions", "1.2.3");
            var runtimeExecutable = Path.Combine(versionRoot, "runtime", "win-x64", "unity-agent-bridge.exe");
            var launcher = Path.Combine(versionRoot, "launcher", "Start-UnityAgentBridge-Mcp.cmd");
            var stableLauncher = Path.Combine(managerRoot, "bin", "agent-bridge-mcp.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeExecutable));
            Directory.CreateDirectory(Path.GetDirectoryName(launcher));
            Directory.CreateDirectory(Path.GetDirectoryName(stableLauncher));
            File.WriteAllText(runtimeExecutable, "runtime");
            File.WriteAllText(launcher, "launcher");
            File.WriteAllText(stableLauncher, "stable launcher");
            File.WriteAllText(Path.Combine(versionRoot, "release-manifest.json"), "{\"version\":\"1.2.3\"}");

            var settings = new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "1.2.3",
                MachineRuntimeRoot = managerRoot,
            };
            var locator = new MachineRuntimeLocator();

            Assert.That(locator.ResolveRuntimeExecutablePath(settings), Is.EqualTo(Path.GetFullPath(runtimeExecutable)));
            Assert.That(locator.ResolveLauncherPath(settings), Is.EqualTo(Path.GetFullPath(stableLauncher)));
            Assert.That(locator.ResolveRuntimeExecutablePath(new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "../escape",
                MachineRuntimeRoot = managerRoot,
            }), Is.Empty);
        }
    }
}
