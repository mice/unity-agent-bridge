using System;
using System.IO;
using System.Linq;
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
            Assert.That(settings.PiExecutablePath, Is.EqualTo("pi"));
            Assert.That(settings.PiModel, Is.EqualTo("deepseek/deepseek-flash"));
            Assert.That(settings.PiTools, Is.EquivalentTo(new[] { "read", "grep", "find", "ls" }));
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
            Assert.That(PiAgentConfigurationValidator.TryValidate(settings, out _), Is.False);
            StringAssert.Contains("PI_CONFIGURATION_INVALID", settings.PiLoadError);
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
            Assert.That(savedJson, Does.Contain("\"piExecutablePath\": \"pi\""));
            Assert.That(savedJson, Does.Contain("\"piModel\": \"deepseek/deepseek-flash\""));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGBM_244.md
        [Test, Category("AGBM_244")]
        [Category("AGBM_Settings")]
        public void Load_InvalidPiValuesRemainVisibleAndCannotBeSaved()
        {
            var json = "{\"piTimeoutMilliseconds\":9999999,\"piMaxTurns\":0,\"piMaxOutputBytes\":1,\"piTools\":[\"bash\"]}";
            File.WriteAllText(_settingsPath, json);
            var store = new McpEditorSettingsStore(_settingsPath);
            var settings = store.Load();
            Assert.That(settings.PiTimeoutMilliseconds, Is.EqualTo(9999999));
            Assert.That(settings.PiMaxTurns, Is.EqualTo(0));
            Assert.That(settings.PiMaxOutputBytes, Is.EqualTo(1));
            Assert.That(settings.PiTools, Is.EqualTo(new[] { "bash" }));
            Assert.That(PiAgentConfigurationValidator.TryValidate(settings, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => store.Save(settings));
            Assert.That(File.ReadAllText(_settingsPath), Is.EqualTo(json));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGBM_245.md
        [Test, Category("AGBM_245")]
        public void PiSettings_RoundTripPreservesSafePolicyAndUnrelatedSettings()
        {
            var store = new McpEditorSettingsStore(_settingsPath);
            store.Save(new McpEditorSettings { WorkspaceRoot = "D:/Repo", PiExecutablePath = "custom-pi",
                PiModel = "provider/model", PiTimeoutMilliseconds = 45000, PiMaxTurns = 3,
                PiMaxOutputBytes = 8192, PiTools = new[] { "read" } });
            var settings = store.Load();
            Assert.That(settings.WorkspaceRoot, Is.EqualTo("D:/Repo"));
            Assert.That(settings.PiSchemaVersion, Is.EqualTo("1.0"));
            Assert.That(settings.PiExecutablePath, Is.EqualTo("custom-pi"));
            Assert.That(settings.PiModel, Is.EqualTo("provider/model"));
            Assert.That(settings.PiTimeoutMilliseconds, Is.EqualTo(45000));
            Assert.That(settings.PiMaxTurns, Is.EqualTo(3));
            Assert.That(settings.PiMaxOutputBytes, Is.EqualTo(8192));
            Assert.That(settings.PiTools, Is.EqualTo(new[] { "read" }));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGBM_249.md
        [Test, Category("AGBM_249")]
        public void PiSettings_ForbiddenFieldAndUnsupportedSchemaRemainUnavailable()
        {
            foreach (var json in new[] { "{\"piSchemaVersion\":\"9.0\"}", "{\"piArguments\":\"--tools bash\"}" })
            {
                File.WriteAllText(_settingsPath, json);
                var settings = new McpEditorSettingsStore(_settingsPath).Load();
                Assert.That(PiAgentConfigurationValidator.TryValidate(settings, out _), Is.False);
                Assert.That(PiAgentTemplateCatalog.List(settings).All(row => !row.IsValid), Is.True);
            }
        }
    }
}
