using System;
using System.IO;
using NUnit.Framework;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpEditorSettingsStoreTests
    {
        private string _tempDirectory;
        private string _settingsPath;

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
            _settingsPath = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_001.md
        [Test]
        [Category("AGBM_Settings")]
        [Category("AGBM_001")]
        public void Load_MissingFile_ReturnsDefaults()
        {
            var store = new McpEditorSettingsStore(_settingsPath);

            var settings = store.Load();

            Assert.That(settings.SchemaVersion, Is.EqualTo("1.0"));
            Assert.That(settings.DiagnosticTimeoutMs, Is.EqualTo(30000));
            Assert.That(settings.ToolsRoot, Is.Empty);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_002.md
        [Test]
        [Category("AGBM_Settings")]
        [Category("AGBM_002")]
        public void Load_InvalidJson_ReturnsDefaults()
        {
            File.WriteAllText(_settingsPath, "{ bad json");
            var store = new McpEditorSettingsStore(_settingsPath);

            var settings = store.Load();

            Assert.That(settings.SchemaVersion, Is.EqualTo("1.0"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_003.md
        [Test]
        [Category("AGBM_Settings")]
        [Category("AGBM_003")]
        public void Load_UnsupportedSchemaVersion_NormalizesToCurrent()
        {
            File.WriteAllText(_settingsPath, "{\"schemaVersion\":\"9.0\"}");
            var store = new McpEditorSettingsStore(_settingsPath);

            var settings = store.Load();

            Assert.That(settings.SchemaVersion, Is.EqualTo("1.0"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_008.md
        [Test]
        [Category("AGBM_Settings")]
        [Category("AGBM_008")]
        public void Load_NonPositiveDiagnosticTimeout_FallsBackToDefault()
        {
            File.WriteAllText(_settingsPath, "{\"schemaVersion\":\"1.0\",\"diagnosticTimeoutMs\":0}");
            var store = new McpEditorSettingsStore(_settingsPath);

            var settings = store.Load();

            Assert.That(settings.DiagnosticTimeoutMs, Is.EqualTo(30000));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_011.md
        [Test]
        [Category("AGBM_Settings")]
        [Category("AGBM_011")]
        public void Load_TrimmedPaths_AreNormalized()
        {
            File.WriteAllText(_settingsPath, "{\"schemaVersion\":\"1.0\",\"workspaceRoot\":\"  D:/Repo  \",\"toolsRoot\":\"  D:/Repo/Tools  \"}");
            var store = new McpEditorSettingsStore(_settingsPath);

            var settings = store.Load();

            Assert.That(settings.WorkspaceRoot, Is.EqualTo("D:/Repo"));
            Assert.That(settings.ToolsRoot, Is.EqualTo("D:/Repo/Tools"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_012.md
        [Test]
        [Timeout(5000)]
        [Category("AGBM_Settings")]
        [Category("AGBM_012")]
        public void Save_CreatesDirectoryAndPersistsNormalizedJson()
        {
            var nestedPath = Path.Combine(_tempDirectory, "Library", "AgentBridge", "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(nestedPath);

            store.Save(new McpEditorSettings
            {
                SchemaVersion = "9.0",
                DiagnosticTimeoutMs = -1,
                WorkspaceRoot = "  D:/Repo  ",
                ToolsRoot = "  D:/Repo/Tools  ",
            });

            var savedJson = File.ReadAllText(nestedPath);

            Assert.That(savedJson, Does.Contain("\"schemaVersion\": \"1.0\""));
            Assert.That(savedJson, Does.Not.Contain("preferredClient"));
            Assert.That(savedJson, Does.Not.Contain("startupAction"));
            Assert.That(savedJson, Does.Not.Contain("startupDelaySeconds"));
            Assert.That(savedJson, Does.Contain("\"diagnosticTimeoutMs\": 30000"));
            Assert.That(savedJson, Does.Contain("\"workspaceRoot\": \"D:/Repo\""));
            Assert.That(savedJson, Does.Contain("\"toolsRoot\": \"D:/Repo/Tools\""));
        }
    }
}
