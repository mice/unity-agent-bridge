using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PluginManifestDiscoveryTests
    {
        private readonly List<AgentBridgeSettings> _settings = new List<AgentBridgeSettings>();
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), nameof(PluginManifestDiscoveryTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            ManifestTestProvider.LastContext = null;
        }

        [TearDown]
        public void TearDown()
        {
            UnityMcpPluginManifestDiscovery.PackageResolverOverride = null;
            ManifestTestProvider.LastContext = null;
            foreach (var settings in _settings)
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }

            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_203.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_203")]
        public void Discover_PackageCacheStyleRoot_DefaultsToInstalledDisabledReady()
        {
            var package = CreateValidPackage("PackageCache/com.example.manifest-plugin@0.1.0");
            SetPackages(package);

            var plugin = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings()).Single();

            Assert.That(plugin.PackageRoot, Is.EqualTo(Path.GetFullPath(package.ResolvedPath)));
            Assert.That(plugin.Installed, Is.True);
            Assert.That(plugin.Enabled, Is.False);
            Assert.That(plugin.Ready, Is.True);
            Assert.That(plugin.Exposed, Is.False);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_204.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_204")]
        public void DiscoverAndRegister_EnabledValidPlugin_ExposesToolAndResolvedPayloadContext()
        {
            var package = CreateValidPackage("embedded/com.example.manifest-plugin");
            SetPackages(package);
            var settings = CreateSettings(enabled: true);
            var projectRoot = Path.Combine(_tempRoot, "project");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            var paths = new AgentBridgePaths(projectRoot, settings);
            paths.EnsureDirectories();
            var registry = new AgentToolRegistry();
            registry.Discover();

            var result = UnityMcpPluginRuntime.DiscoverAndRegister(
                registry,
                settings,
                paths,
                new FileAgentBridgeLogger(paths.BridgeLogPath));

            var plugin = result.InstalledPlugins.Single();
            Assert.That(plugin.Enabled && plugin.Ready && plugin.Exposed, Is.True);
            Assert.That(registry.TryGetTool("unity.manifest.get.test", out _), Is.True);
            Assert.That(result.Catalog.tools.Any(item => item.bridgeTool == "unity.manifest.get.test"), Is.True);
            Assert.That(ManifestTestProvider.LastContext, Is.Not.Null);
            Assert.That(ManifestTestProvider.LastContext.PluginRoot, Is.EqualTo(Path.GetFullPath(package.ResolvedPath)));
            Assert.That(ManifestTestProvider.LastContext.Payloads.Single().Path, Does.StartWith(Path.GetFullPath(package.ResolvedPath)));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_205.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_205")]
        public void Discover_TraversalEntryPath_RejectsCandidateWithoutAffectingOtherPackage()
        {
            var invalid = CreateValidPackage("invalid-traversal", pluginId: "com.example.traversal-plugin");
            var manifest = ReadManifest(invalid);
            manifest.entry.dllPath = "../outside.dll";
            WriteManifest(invalid, manifest);
            var valid = CreateValidPackage("valid-neighbor");
            SetPackages(invalid, valid);

            var plugins = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings());

            Assert.That(plugins.Single(item => item.PluginId == "com.example.traversal-plugin").DiagnosticCode,
                Is.EqualTo(UnityMcpPluginDiagnosticCodes.PathInvalid));
            Assert.That(plugins.Single(item => item.PluginId == ManifestTestProvider.PluginId).Ready, Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_206.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_206")]
        public void Discover_DuplicatePluginIds_MarksEveryCandidateNotReady()
        {
            var first = CreateValidPackage("duplicate-first");
            var second = CreateValidPackage("duplicate-second");
            SetPackages(first, second);

            var plugins = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings());

            Assert.That(plugins, Has.Count.EqualTo(2));
            Assert.That(plugins.All(item => !item.Ready && item.DiagnosticCode == UnityMcpPluginDiagnosticCodes.DuplicateId), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_207.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_207")]
        public void Discover_MissingRequiredPayload_ReportsStableDiagnostic()
        {
            var package = CreateValidPackage("missing-payload");
            File.Delete(PayloadPath(package));
            SetPackages(package);

            var plugin = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings(enabled: true)).Single();

            Assert.That(plugin.Ready, Is.False);
            Assert.That(plugin.DiagnosticCode, Is.EqualTo(UnityMcpPluginDiagnosticCodes.PayloadUnavailable));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_208.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_208")]
        public void Discover_AlteredPayload_ReportsHashMismatchWithoutRepairingFile()
        {
            var package = CreateValidPackage("hash-mismatch");
            var payloadPath = PayloadPath(package);
            File.AppendAllText(payloadPath, "altered");
            var altered = File.ReadAllText(payloadPath);
            SetPackages(package);

            var plugin = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings(enabled: true)).Single();

            Assert.That(plugin.Ready, Is.False);
            Assert.That(plugin.DiagnosticCode, Is.EqualTo(UnityMcpPluginDiagnosticCodes.PayloadHashMismatch));
            Assert.That(File.ReadAllText(payloadPath), Is.EqualTo(altered));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_209.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_209")]
        public void Discover_AssemblyAndProviderMismatch_StayVisibleWithScopedDiagnostics()
        {
            var assemblyMismatch = CreateValidPackage("assembly-mismatch", pluginId: "com.example.assembly-mismatch");
            var assemblyManifest = ReadManifest(assemblyMismatch);
            assemblyManifest.entry.assemblyName = "Missing.UnityMcp.Plugin.Assembly";
            WriteManifest(assemblyMismatch, assemblyManifest);

            var providerMismatch = CreateValidPackage("provider-mismatch", pluginId: "com.example.provider-mismatch");
            var providerManifest = ReadManifest(providerMismatch);
            providerManifest.entry.providerType = typeof(string).FullName;
            WriteManifest(providerMismatch, providerManifest);
            SetPackages(assemblyMismatch, providerMismatch);

            var plugins = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings(enabled: true));

            Assert.That(plugins.Single(item => item.PluginId == "com.example.assembly-mismatch").DiagnosticCode,
                Is.EqualTo(UnityMcpPluginDiagnosticCodes.AssemblyUnavailable));
            Assert.That(plugins.Single(item => item.PluginId == "com.example.provider-mismatch").DiagnosticCode,
                Is.EqualTo(UnityMcpPluginDiagnosticCodes.ProviderInvalid));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_210.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_210")]
        public void ExternalPluginLifecycle_SetEnabled_NormalizesPersistedStateByStableId()
        {
            var settings = CreateSettings();
            settings.externalPluginStates.Add(new UnityMcpExternalPluginState { pluginId = ManifestTestProvider.PluginId, enabled = false });
            settings.externalPluginStates.Add(new UnityMcpExternalPluginState { pluginId = ManifestTestProvider.PluginId, enabled = false });

            var changed = UnityMcpExternalPluginLifecycle.SetEnabled(settings, ManifestTestProvider.PluginId, true);

            Assert.That(changed, Is.True);
            Assert.That(settings.externalPluginStates.Count(item => item.pluginId == ManifestTestProvider.PluginId), Is.EqualTo(1));
            Assert.That(UnityMcpExternalPluginLifecycle.IsEnabled(settings, ManifestTestProvider.PluginId), Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_211.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_211")]
        public void Discover_UpgradedPackage_PreservesEnablementAndRevalidatesVersion()
        {
            var package = CreateValidPackage("upgrade");
            var manifest = ReadManifest(package);
            manifest.version = "0.2.0";
            WriteManifest(package, manifest);
            SetPackages(package);

            var plugin = UnityMcpPluginRuntime.DiscoverInstalledPlugins(CreateSettings(enabled: true)).Single();

            Assert.That(plugin.Enabled, Is.True);
            Assert.That(plugin.Version, Is.EqualTo("0.2.0"));
            Assert.That(plugin.Ready, Is.False);
            Assert.That(plugin.DiagnosticCode, Is.EqualTo(UnityMcpPluginDiagnosticCodes.ProviderInvalid));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_212.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_212")]
        public void DiscoverAndRegister_DisableThenRemove_RebuiltCatalogContainsNoStaleTool()
        {
            var package = CreateValidPackage("disable-remove");
            SetPackages(package);
            var settings = CreateSettings(enabled: true);
            var projectRoot = Path.Combine(_tempRoot, "refresh-project");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            var paths = new AgentBridgePaths(projectRoot, settings);
            paths.EnsureDirectories();
            var logger = new FileAgentBridgeLogger(paths.BridgeLogPath);

            var enabledRegistry = new AgentToolRegistry();
            enabledRegistry.Discover();
            var enabledResult = UnityMcpPluginRuntime.DiscoverAndRegister(enabledRegistry, settings, paths, logger);
            Assert.That(enabledResult.Catalog.tools.Any(item => item.bridgeTool == "unity.manifest.get.test"), Is.True);

            UnityMcpExternalPluginLifecycle.SetEnabled(settings, ManifestTestProvider.PluginId, false);
            var disabledRegistry = new AgentToolRegistry();
            disabledRegistry.Discover();
            var disabledResult = UnityMcpPluginRuntime.DiscoverAndRegister(disabledRegistry, settings, paths, logger);
            Assert.That(disabledResult.Catalog.tools.Any(item => item.bridgeTool == "unity.manifest.get.test"), Is.False);

            SetPackages();
            var removedRegistry = new AgentToolRegistry();
            removedRegistry.Discover();
            var removedResult = UnityMcpPluginRuntime.DiscoverAndRegister(removedRegistry, settings, paths, logger);
            Assert.That(removedResult.InstalledPlugins, Is.Empty);
            Assert.That(removedResult.Catalog.tools.Any(item => item.bridgeTool == "unity.manifest.get.test"), Is.False);
        }

        private AgentBridgeSettings CreateSettings(bool enabled = false)
        {
            var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
            settings.pluginRegistrations.Clear();
            settings.externalPluginStates.Clear();
            if (enabled)
            {
                settings.externalPluginStates.Add(new UnityMcpExternalPluginState
                {
                    pluginId = ManifestTestProvider.PluginId,
                    enabled = true
                });
                settings.externalPluginStates.Add(new UnityMcpExternalPluginState
                {
                    pluginId = "com.example.assembly-mismatch",
                    enabled = true
                });
                settings.externalPluginStates.Add(new UnityMcpExternalPluginState
                {
                    pluginId = "com.example.provider-mismatch",
                    enabled = true
                });
            }

            _settings.Add(settings);
            return settings;
        }

        private UnityMcpResolvedPackage CreateValidPackage(string relativeRoot, string pluginId = ManifestTestProvider.PluginId)
        {
            var root = Path.Combine(_tempRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            var dllPath = Path.Combine(root, "Editor", "Plugins", "ManifestTestPlugin.dll");
            var payloadPath = Path.Combine(root, "Tools~", "runtimes", "win-x64", "manifest-test.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(dllPath) ?? root);
            Directory.CreateDirectory(Path.GetDirectoryName(payloadPath) ?? root);
            File.WriteAllText(dllPath, "Unity imports the real test assembly; this file proves package provenance only.");
            File.WriteAllText(payloadPath, "payload");

            var package = new UnityMcpResolvedPackage
            {
                Name = pluginId,
                Version = "0.1.0",
                ResolvedPath = root
            };
            WriteManifest(package, new UnityMcpPackagePluginManifest
            {
                schemaVersion = 1,
                pluginId = pluginId,
                displayName = "Manifest Test Plugin",
                version = "0.1.0",
                defaultEnabled = true,
                entry = new UnityMcpPackagePluginEntry
                {
                    kind = "managedDll",
                    dllPath = "Editor/Plugins/ManifestTestPlugin.dll",
                    assemblyName = typeof(ManifestTestProvider).Assembly.GetName().Name,
                    providerType = typeof(ManifestTestProvider).FullName
                },
                payloads = new List<UnityMcpPackagePluginPayload>
                {
                    new UnityMcpPackagePluginPayload
                    {
                        id = "linter",
                        rid = "win-x64",
                        path = "Tools~/runtimes/win-x64/manifest-test.exe",
                        sha256 = ComputeSha256(payloadPath),
                        required = true
                    }
                }
            });
            return package;
        }

        private static UnityMcpPackagePluginManifest ReadManifest(UnityMcpResolvedPackage package)
        {
            return JsonUtility.FromJson<UnityMcpPackagePluginManifest>(
                File.ReadAllText(Path.Combine(package.ResolvedPath, "unitymcp-plugin.json")));
        }

        private static void WriteManifest(UnityMcpResolvedPackage package, UnityMcpPackagePluginManifest manifest)
        {
            Directory.CreateDirectory(package.ResolvedPath);
            File.WriteAllText(
                Path.Combine(package.ResolvedPath, "unitymcp-plugin.json"),
                JsonUtility.ToJson(manifest, true));
        }

        private static string PayloadPath(UnityMcpResolvedPackage package)
        {
            return Path.Combine(package.ResolvedPath, "Tools~", "runtimes", "win-x64", "manifest-test.exe");
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static void SetPackages(params UnityMcpResolvedPackage[] packages)
        {
            UnityMcpPluginManifestDiscovery.PackageResolverOverride = () => packages;
        }
    }

    [UnityMcpPlugin(PluginId, "0.1.0")]
    public sealed class ManifestTestProvider : IUnityMcpToolProvider
    {
        public const string PluginId = "com.example.manifest-plugin";
        public static UnityMcpPluginContext LastContext { get; set; }

        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            LastContext = context;
            return new[] { new ManifestTestTool() };
        }

        private sealed class ManifestTestTool : IUnityMcpTool
        {
            public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
            {
                Name = "unity.manifest.get.test",
                Title = "Manifest Test",
                Description = "Verifies manifest plugin activation.",
                DefaultTimeoutMs = 1000,
                AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
                SideEffect = UnityMcpToolSideEffect.ReadsProject
            };

            public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
            {
                Kind = UnityMcpSchemaKind.InlineJson,
                Value = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            };

            public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
            {
                return new UnityMcpToolResult
                {
                    Success = true,
                    Status = UnityMcpToolStatus.Success,
                    Summary = "ok"
                };
            }
        }
    }
}
