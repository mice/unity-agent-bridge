using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class RuntimeReleaseDiscoveryTests
    {
        private string root;
        private const string Sha = "b63be41765f86088f8e4775797be13df596651ca";
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "RuntimeDiscovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { Directory.Delete(root, true); }
        private InstalledRuntimePackageIdentity Identity() => new InstalledRuntimePackageIdentity
        {
            Version = "1.4.3-rc.1", Revision = "v1.4.3-rc.1", CommitSha = Sha, IsGit = true,
            PackageId = "com.unitymcp.agent-bridge@git+https://github.com/mice/unity-agent-bridge.git?path=/com.unitymcp.agent-bridge#v1.4.3-rc.1",
            PackageRoot = Path.Combine(root, "Package")
        };
        private McpEditorSettings Settings() => new McpEditorSettings { RuntimeMode = "machine", MachineRuntimeRoot = Path.Combine(root, "Machine") };
        private PublishedMachineRuntimeVersion Row(string version, string sha = Sha) => new PublishedMachineRuntimeVersion
        { Version = version, Tag = "v" + version, CommitSha = sha, SourceArchiveUrl = "https://github.com/mice/unity-agent-bridge/archive/refs/tags/v" + version + ".zip" };
        private void Manifest(string version, string sha, bool installed = false)
        {
            var directory = Path.Combine(Settings().MachineRuntimeRoot, installed ? "versions" : "releases", version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "release-manifest.json"), "{\"version\":\"" + version + "\",\"gitTag\":\"v" + version + "\",\"commitSha\":\"" + sha + "\",\"buildOrigin\":\"git-tag-source\"}");
            if (installed) { Directory.CreateDirectory(Path.Combine(directory, "runtime/win-x64")); File.WriteAllText(Path.Combine(directory, "runtime/win-x64/unity-agent-bridge.exe"), "fixture only"); }
        }

        // TestRecord: Documentation~/test_records/AGBM_252.md
        [Test, Category("AGBM_252")]
        public void ExactTagDiscoversItselfWithEmptyCustomRootAndNoCatalogRow()
        {
            var locator = new MachineRuntimeLocator(Identity, _ => Array.Empty<PublishedMachineRuntimeVersion>());
            var rows = locator.ListPublishedVersions(Settings());
            Assert.That(rows.Count, Is.EqualTo(1)); Assert.That(rows[0].Version, Is.EqualTo("1.4.3-rc.1"));
            Assert.That(rows[0].CommitSha, Is.EqualTo(Sha)); Assert.That(rows[0].Provenance, Is.EqualTo("installed-package"));
            Assert.That(rows[0].IsInstalled, Is.False); Assert.That(AgentBridgeMcpSetupWindow.CanDownloadAndInstall(rows[0]), Is.True);
            Assert.That(Directory.Exists(Settings().MachineRuntimeRoot), Is.False);
        }
        // TestRecord: Documentation~/test_records/AGBM_253.md
        [TestCase(false), TestCase(true), Category("AGBM_253")]
        public void MissingOrMalformedPackagedCatalogDoesNotHideSelf(bool malformed)
        {
            var identity = Identity();
            if (malformed) { Directory.CreateDirectory(Path.Combine(identity.PackageRoot, "Documentation~")); File.WriteAllText(Path.Combine(identity.PackageRoot, "Documentation~/published-runtime-versions.json"), "{"); }
            var locator = new MachineRuntimeLocator(() => identity);
            var rows = locator.ListPublishedVersions(Settings());
            Assert.That(rows.Select(row => row.Version), Is.EqualTo(new[] { identity.Version }));
            Assert.That(locator.Diagnostics.Any(value => value.Contains("RUNTIME_CATALOG_UNREADABLE")), Is.True);
        }
        // TestRecord: Documentation~/test_records/AGBM_254.md
        [Test, Category("AGBM_254")]
        public void NumericBaseFilterAppliesAfterMachineCatalogAndFallbackMerge()
        {
            Manifest("1.4.2-rc.10", Sha); Manifest("1.3.0-rc.1", Sha);
            var locator = new MachineRuntimeLocator(Identity, _ => new[]
            { Row("1.4.2-rc.1"), Row("1.4.2"), Row("1.4.3-rc.2"), Row("1.4.3-rc.10"), Row("1.4.10-rc.1") }
                .Concat(MachineRuntimeLocator.CreateBuiltInPublishedVersions(Settings().MachineRuntimeRoot)).ToArray());
            Assert.That(locator.ListPublishedVersions(Settings()).Select(row => row.Version), Is.EqualTo(new[]
            { "1.4.10-rc.1", "1.4.3-rc.10", "1.4.3-rc.2", "1.4.3-rc.1", "1.4.2" }));
        }
        // TestRecord: Documentation~/test_records/AGBM_255.md
        [TestCase("branch"), TestCase("foreign"), TestCase("hash"), TestCase("version"), Category("AGBM_255")]
        public void UnverifiedPackageIdentityCannotFabricateSelf(string fault)
        {
            var identity = Identity();
            if (fault == "branch") identity.Revision = "main";
            if (fault == "foreign") identity.PackageId = identity.PackageId.Replace("/mice/", "/foreign/");
            if (fault == "hash") identity.CommitSha = "short";
            if (fault == "version") identity.Version = "invalid";
            var locator = new MachineRuntimeLocator(() => identity, _ => new[] { Row("1.4.2"), Row("1.4.3-rc.2") });
            var rows = locator.ListPublishedVersions(Settings());
            Assert.That(rows.Any(row => row.Version == "1.4.3-rc.1"), Is.False);
            Assert.That(rows.Any(row => row.Version == "1.4.2"), Is.True);
            Assert.That(locator.Diagnostics.Any(value => value.Contains("RUNTIME_PACKAGE_IDENTITY_UNVERIFIED")), Is.True);
            if (fault == "version") { Assert.That(rows.Any(row => row.Version.Contains("-rc.")), Is.False); Assert.That(locator.Diagnostics.Any(value => value.Contains("RUNTIME_PACKAGE_VERSION_INVALID")), Is.True); }
        }
        // TestRecord: Documentation~/test_records/AGBM_256.md
        [Test, Category("AGBM_256")]
        public void SelfIdentityWinsConflictingMetadataWithoutReusingWrongInstallation()
        {
            Manifest("1.4.3-rc.1", new string('a', 40)); Manifest("1.4.3-rc.1", new string('a', 40), true);
            var locator = new MachineRuntimeLocator(Identity, _ => new[] { Row("1.4.3-rc.1", new string('a', 40)) });
            var row = locator.ListPublishedVersions(Settings()).Single();
            Assert.That(row.CommitSha, Is.EqualTo(Sha)); Assert.That(row.IsAvailable, Is.True); Assert.That(row.IsInstalled, Is.False);
            Assert.That(row.Diagnostic, Does.Contain("CONFLICT"));
            Assert.That(File.ReadAllText(Path.Combine(Settings().MachineRuntimeRoot, "versions/1.4.3-rc.1/release-manifest.json")), Does.Contain(new string('a', 40)));
        }
        // TestRecord: Documentation~/test_records/AGBM_257.md
        [Test, Category("AGBM_257")]
        public void OtherVersionConflictsFailClosedRegardlessOfOrder()
        {
            foreach (var reverse in new[] { false, true })
            {
                var entries = new[] { Row("1.4.4-rc.1"), Row("1.4.4-rc.1", new string('a', 40)) };
                var locator = new MachineRuntimeLocator(Identity, _ => reverse ? entries.Reverse().ToArray() : entries);
                var row = locator.ListPublishedVersions(Settings()).Single(value => value.Version == "1.4.4-rc.1");
                Assert.That(row.IsAvailable, Is.False); Assert.That(AgentBridgeMcpSetupWindow.CanDownloadAndInstall(row), Is.False);
                Assert.That(row.Diagnostic, Does.Contain("CONFLICT"));
            }
        }
        // TestRecord: Documentation~/test_records/AGBM_258.md
        [Test, Category("AGBM_258")]
        public void MalformedMachineRowDoesNotPoisonValidRowsAndDuplicatesMerge()
        {
            Manifest("1.4.3-rc.1", Sha); Manifest("1.4.3-rc.1", Sha, true);
            var bad = Path.Combine(Settings().MachineRuntimeRoot, "releases/1.4.5-rc.1"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad, "release-manifest.json"), "{");
            var locator = new MachineRuntimeLocator(Identity, _ => new[] { Row("1.4.3-rc.1"), Row("1.4.2") });
            var rows = locator.ListPublishedVersions(Settings());
            var self = rows.Single(value => value.Version == "1.4.3-rc.1");
            Assert.That(self.IsInstalled, Is.True); Assert.That(self.Provenance, Does.Contain("machine-manifest"));
            Assert.That(rows.Count, Is.EqualTo(2)); Assert.That(locator.Diagnostics.Any(value => value.Contains("UNREADABLE")), Is.True);
        }
        // TestRecord: Documentation~/test_records/AGBM_259.md
        [Test, Category("AGBM_259")]
        public void FilteringLeavesInstalledOldRcAndExplicitPinUntouched()
        {
            Manifest("1.4.2-rc.1", Sha, true);
            var settings = Settings(); settings.RuntimeVersion = "1.4.2-rc.1";
            var locator = new MachineRuntimeLocator(Identity, _ => new[] { Row("1.4.2-rc.1") });
            Assert.That(locator.ListPublishedVersions(settings).Any(row => row.Version == settings.RuntimeVersion), Is.False);
            Assert.That(locator.ListInstalledVersions(settings), Does.Contain("1.4.2-rc.1"));
            Assert.That(locator.ResolveVersion(settings), Is.EqualTo("1.4.2-rc.1"));
        }
    }
}
