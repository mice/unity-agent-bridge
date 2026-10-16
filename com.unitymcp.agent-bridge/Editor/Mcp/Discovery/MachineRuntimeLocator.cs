using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class PublishedMachineRuntimeVersion
    {
        public string Version { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string ArtifactUrl { get; set; } = string.Empty;
        public string SourceArchiveUrl { get; set; } = string.Empty;
        public string CommitSha { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public bool IsAvailable { get; set; } = true;
        public string Diagnostic { get; set; } = string.Empty;
        public string Provenance { get; set; } = string.Empty;
    }

    internal sealed class InstalledRuntimePackageIdentity
    {
        public string Version { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string CommitSha { get; set; } = string.Empty;
        public string PackageRoot { get; set; } = string.Empty;
        public bool IsGit { get; set; }
    }

    internal sealed class MachineRuntimeLocator
    {
        internal const string MachineMode = "machine";
        internal const string RuntimeSelectionFileName = "runtime-selection.json";
        internal const string MinimumPublishedRuntimeVersion = "1.2.12-rc.2";

        private readonly Func<InstalledRuntimePackageIdentity> readPackage;
        private readonly Func<string, IReadOnlyList<PublishedMachineRuntimeVersion>> readCatalog;
        private readonly List<string> diagnostics = new List<string>();
        internal IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();

        internal MachineRuntimeLocator() : this(null, null) { }

        internal MachineRuntimeLocator(Func<InstalledRuntimePackageIdentity> readPackage,
            Func<string, IReadOnlyList<PublishedMachineRuntimeVersion>> readCatalog = null)
        {
            this.readPackage = readPackage ?? ReadInstalledPackage;
            this.readCatalog = readCatalog;
        }

        private static InstalledRuntimePackageIdentity ReadInstalledPackage()
        {
            var package = PackageInfo.FindForAssembly(typeof(MachineRuntimeLocator).Assembly);
            if (package == null) return null;
            return new InstalledRuntimePackageIdentity { Version = package.version, PackageId = package.packageId,
                Revision = package.git?.revision ?? string.Empty, CommitSha = package.git?.hash ?? string.Empty,
                PackageRoot = package.resolvedPath, IsGit = package.source == PackageSource.Git };
        }

        private void Diagnose(string message) { if (diagnostics.Count < 32 && !diagnostics.Contains(message)) diagnostics.Add(message); }

        public string ResolveRoot(McpEditorSettings settings)
        {
            if (settings == null || !string.Equals(settings.RuntimeMode, MachineMode, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var managerRoot = ResolveManagerRoot(settings);
            if (string.IsNullOrWhiteSpace(managerRoot))
            {
                return string.Empty;
            }

            var version = ResolveVersion(settings, managerRoot);
            if (string.IsNullOrWhiteSpace(version))
            {
                return string.Empty;
            }

            var versionRoot = Path.Combine(managerRoot, "versions", version);
            return Directory.Exists(versionRoot) && File.Exists(Path.Combine(versionRoot, "release-manifest.json"))
                ? Path.GetFullPath(versionRoot)
                : string.Empty;
        }

        public string ResolveRuntimeRoot(McpEditorSettings settings)
        {
            var root = ResolveRoot(settings);
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "runtime");
        }

        public string ResolveRuntimeExecutablePath(McpEditorSettings settings)
        {
            var runtimeRoot = ResolveRuntimeRoot(settings);
            return FindRuntimeExecutablePath(runtimeRoot);
        }

        internal IReadOnlyList<string> ListInstalledVersions(McpEditorSettings settings)
        {
            if (settings == null)
            {
                return Array.Empty<string>();
            }

            var versionsRoot = Path.Combine(ResolveManagerRoot(settings), "versions");
            if (!Directory.Exists(versionsRoot))
            {
                return Array.Empty<string>();
            }

            var versions = new List<string>();
            try
            {
                foreach (var versionRoot in Directory.GetDirectories(versionsRoot))
                {
                    var version = Path.GetFileName(versionRoot);
                    if (IsSupportedSemanticVersion(version) && IsUsableInstalledVersion(versionRoot, version))
                    {
                        versions.Add(version);
                    }
                }
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }

            versions.Sort(CompareSemanticVersionsDescending);
            return versions;
        }

        internal IReadOnlyList<PublishedMachineRuntimeVersion> ListPublishedVersions(McpEditorSettings settings)
        {
            diagnostics.Clear();
            if (settings == null) return Array.Empty<PublishedMachineRuntimeVersion>();
            var managerRoot = ResolveManagerRoot(settings);
            InstalledRuntimePackageIdentity package = null;
            try { package = readPackage(); }
            catch { Diagnose("RUNTIME_PACKAGE_IDENTITY_UNAVAILABLE: Installed package metadata could not be read."); }
            var rows = new List<PublishedMachineRuntimeVersion>();
            try
            {
                foreach (var row in readCatalog != null ? readCatalog(managerRoot) : ReadPackagedPublishedVersions(managerRoot, package))
                    if (row != null) { row.Provenance = "packaged-catalog"; rows.Add(row); }
            }
            catch { Diagnose("RUNTIME_CATALOG_UNREADABLE: Packaged catalog could not be read."); }
            ReadMachinePublishedVersions(managerRoot, rows);
            var self = CreateSelfVersion(package);
            if (self != null) rows.Add(self);
            else Diagnose("RUNTIME_PACKAGE_IDENTITY_UNVERIFIED: A self-release requires the expected repository, exact version tag and full resolved commit SHA.");

            var valid = rows.Where(row => IsSupportedPublishedVersion(row.Version)).ToArray();
            var merged = new List<PublishedMachineRuntimeVersion>();
            foreach (var group in valid.GroupBy(row => row.Version, StringComparer.Ordinal))
            {
                var candidates = group.ToArray();
                var authoritative = candidates.FirstOrDefault(row => row.Provenance == "installed-package");
                var identities = candidates.Where(HasCompleteSourceIdentity).Select(row => row.Tag + ":" + row.CommitSha.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
                var selected = authoritative ?? candidates.OrderByDescending(HasCompleteSourceIdentity)
                    .ThenBy(row => row.Tag, StringComparer.Ordinal).ThenBy(row => row.CommitSha, StringComparer.Ordinal).First();
                if (identities.Length > 1)
                {
                    selected.Diagnostic = "RUNTIME_SOURCE_IDENTITY_CONFLICT: Release metadata disagrees about this version's immutable source.";
                    selected.IsAvailable = authoritative != null;
                    Diagnose(selected.Version + ": " + selected.Diagnostic);
                }
                if (!string.Equals(selected.Tag, "v" + selected.Version, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(selected.ArtifactUrl) && !HasCompleteSourceIdentity(selected))
                {
                    selected.IsAvailable = false;
                    selected.Diagnostic = "RUNTIME_SOURCE_IDENTITY_INVALID: A source-only release requires an exact tag, approved source URL and full commit SHA.";
                    Diagnose(selected.Version + ": " + selected.Diagnostic);
                }
                selected.Provenance = string.Join(",", candidates.Select(row => row.Provenance).Distinct().OrderBy(value => value, StringComparer.Ordinal));
                selected.IsInstalled = selected.IsAvailable && IsUsableInstalledVersion(Path.Combine(managerRoot, "versions", selected.Version), selected.Version,
                    HasValidCommitSha(selected.CommitSha) ? selected.CommitSha : null);
                merged.Add(selected);
            }

            var hasThreshold = TryBaseVersion(package?.Version, out var threshold);
            if (!hasThreshold) Diagnose("RUNTIME_PACKAGE_VERSION_INVALID: RC choices are withheld because the installed package base version is unavailable.");
            var visible = merged.Where(row => !IsReleaseCandidate(row.Version) || hasThreshold &&
                TryBaseVersion(row.Version, out var candidateBase) && candidateBase.CompareTo(threshold) >= 0).ToList();
            visible.Sort((left, right) => CompareSemanticVersionsDescending(left.Version, right.Version));
            return visible;
        }

        private PublishedMachineRuntimeVersion CreateSelfVersion(InstalledRuntimePackageIdentity package)
        {
            if (package == null || !package.IsGit || !IsSupportedPublishedVersion(package.Version) || !HasValidCommitSha(package.CommitSha) ||
                package.Revision != "v" + package.Version) return null;
            var suffix = "https://github.com/mice/unity-agent-bridge.git?path=/com.unitymcp.agent-bridge#" + package.Revision;
            if (package.PackageId != "com.unitymcp.agent-bridge@git+" + suffix && package.PackageId != "com.unitymcp.agent-bridge@" + suffix) return null;
            return new PublishedMachineRuntimeVersion { Version = package.Version, Tag = package.Revision,
                SourceArchiveUrl = CreateGitHubSourceArchiveUrl(package.Revision), CommitSha = package.CommitSha.ToLowerInvariant(), Provenance = "installed-package" };
        }

        private void ReadMachinePublishedVersions(string managerRoot, List<PublishedMachineRuntimeVersion> rows)
        {
            var releasesRoot = Path.Combine(managerRoot, "releases");
            if (!Directory.Exists(releasesRoot)) return;
            string[] directories;
            try { directories = Directory.GetDirectories(releasesRoot); }
            catch { Diagnose("RUNTIME_MACHINE_CATALOG_UNREADABLE: Machine releases could not be enumerated."); return; }
            foreach (var directory in directories)
            {
                var version = Path.GetFileName(directory);
                if (!IsSupportedPublishedVersion(version)) continue;
                var path = Path.Combine(directory, "release-manifest.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var manifest = JsonUtility.FromJson<ReleaseManifestFile>(File.ReadAllText(path));
                    if (manifest == null || manifest.version?.Trim() != version) { Diagnose(version + ": RUNTIME_MACHINE_MANIFEST_INVALID"); continue; }
                    var tag = !string.IsNullOrWhiteSpace(manifest.tag) ? manifest.tag.Trim() : !string.IsNullOrWhiteSpace(manifest.gitTag) ? manifest.gitTag.Trim() : "v" + version;
                    var source = CreateGitHubSourceArchiveUrl(tag);
                    var artifact = NormalizeArtifactUrl(version, manifest.artifactUrl);
                    if (manifest.buildOrigin == "git-tag-source" || string.Equals(artifact, source, StringComparison.OrdinalIgnoreCase)) artifact = string.Empty;
                    rows.Add(new PublishedMachineRuntimeVersion { Version = version, Tag = tag, ArtifactUrl = artifact,
                        SourceArchiveUrl = source, CommitSha = manifest.commitSha?.Trim() ?? string.Empty, Provenance = "machine-manifest" });
                }
                catch { Diagnose(version + ": RUNTIME_MACHINE_MANIFEST_UNREADABLE"); }
            }
        }

        private static bool HasCompleteSourceIdentity(PublishedMachineRuntimeVersion row) =>
            HasValidCommitSha(row.CommitSha) && row.Tag == "v" + row.Version &&
            (row.SourceArchiveUrl == CreateGitHubSourceArchiveUrl(row.Tag) ||
             row.SourceArchiveUrl == "https://github.com/mice/unity-agent-bridge/archive/" + row.CommitSha + ".zip");

        private static bool TryBaseVersion(string value, out Version version)
        {
            version = null;
            return IsSupportedSemanticVersion(value) && Version.TryParse(value.Split('-')[0], out version);
        }

        private static bool IsReleaseCandidate(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, "-rc\\.[0-9]+$");

        private static bool HasValidCommitSha(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 40)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<PublishedMachineRuntimeVersion> ReadPackagedPublishedVersions(string managerRoot, InstalledRuntimePackageIdentity identity)
        {
            var catalogPath = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(identity?.PackageRoot))
                    catalogPath = Path.Combine(identity.PackageRoot, "Documentation~", "published-runtime-versions.json");

                if (string.IsNullOrWhiteSpace(catalogPath))
                {
                    var toolsRoot = McpPathResolver.TryResolvePackageToolsRoot();
                    var packageRoot = string.IsNullOrWhiteSpace(toolsRoot)
                        ? null
                        : Directory.GetParent(toolsRoot)?.FullName;
                    if (!string.IsNullOrWhiteSpace(packageRoot))
                    {
                        catalogPath = Path.Combine(packageRoot, "Documentation~", "published-runtime-versions.json");
                    }
                }
            }
            catch
            {
                Diagnose("RUNTIME_CATALOG_UNREADABLE: Using bounded historical fallback metadata.");
                return CreateBuiltInPublishedVersions(managerRoot);
            }

            if (!File.Exists(catalogPath))
            {
                Diagnose("RUNTIME_CATALOG_UNREADABLE: Using bounded historical fallback metadata.");
                return CreateBuiltInPublishedVersions(managerRoot);
            }

            try
            {
                var catalog = JsonUtility.FromJson<PublishedRuntimeCatalogFile>(File.ReadAllText(catalogPath));
                if (catalog?.versions == null)
                {
                    Diagnose("RUNTIME_CATALOG_UNREADABLE: Using bounded historical fallback metadata.");
                    return CreateBuiltInPublishedVersions(managerRoot);
                }

                var publishedVersions = new List<PublishedMachineRuntimeVersion>();
                foreach (var entry in catalog.versions)
                {
                    var version = entry?.version?.Trim() ?? string.Empty;
                    if (!IsSupportedPublishedVersion(version))
                    {
                        continue;
                    }

                    publishedVersions.Add(new PublishedMachineRuntimeVersion
                    {
                        Version = version,
                        Tag = string.IsNullOrWhiteSpace(entry.tag) ? "v" + version : entry.tag.Trim(),
                        ArtifactUrl = NormalizeArtifactUrl(version, entry.artifactUrl),
                        SourceArchiveUrl = string.IsNullOrWhiteSpace(entry.sourceArchiveUrl)
                            ? CreateGitHubSourceArchiveUrl(string.IsNullOrWhiteSpace(entry.tag) ? "v" + version : entry.tag.Trim())
                            : entry.sourceArchiveUrl.Trim(),
                        CommitSha = entry.commitSha?.Trim() ?? string.Empty,
                        IsInstalled = IsUsableInstalledVersion(Path.Combine(managerRoot, "versions", version), version),
                    });
                }

                return publishedVersions;
            }
            catch
            {
                Diagnose("RUNTIME_CATALOG_UNREADABLE: Using bounded historical fallback metadata.");
                return CreateBuiltInPublishedVersions(managerRoot);
            }
        }

        internal static IReadOnlyList<PublishedMachineRuntimeVersion> CreateBuiltInPublishedVersions(string managerRoot)
        {
            var versions = new[]
            {
                "1.2.12-rc.6",
                "1.2.12-rc.5",
                "1.2.12-rc.4",
                "1.2.12-rc.3",
                "1.2.12-rc.2",
            };
            var publishedVersions = new List<PublishedMachineRuntimeVersion>(versions.Length);
            foreach (var version in versions)
            {
                var tag = "v" + version;
                publishedVersions.Add(new PublishedMachineRuntimeVersion
                {
                    Version = version,
                    Tag = tag,
                    ArtifactUrl = version == "1.2.12-rc.6" || version == "1.2.12-rc.5" || version == "1.2.12-rc.4" || version == "1.2.12-rc.3"
                        ? string.Empty
                        : CreateGitHubArtifactUrl(version),
                    SourceArchiveUrl = version == "1.2.12-rc.6"
                        ? "https://github.com/mice/unity-agent-bridge/archive/e987318fd6a4c0f98fedb8e9c487c210be24a8fe.zip"
                        : version == "1.2.12-rc.5"
                            ? "https://github.com/mice/unity-agent-bridge/archive/94d0fe04fa3cdf50cc94d7eb4b3378d781bf3839.zip"
                            : version == "1.2.12-rc.4"
                                ? "https://github.com/mice/unity-agent-bridge/archive/ad401b9c529f8f540213f81cf3a77c5b009bf711.zip"
                                : version == "1.2.12-rc.3"
                                    ? "https://github.com/mice/unity-agent-bridge/archive/956f695ade6ab73c947bad9b78a01fc4fc2a154a.zip"
                                    : CreateGitHubSourceArchiveUrl(tag),
                    CommitSha = version == "1.2.12-rc.6"
                        ? "e987318fd6a4c0f98fedb8e9c487c210be24a8fe"
                        : version == "1.2.12-rc.5"
                            ? "94d0fe04fa3cdf50cc94d7eb4b3378d781bf3839"
                            : version == "1.2.12-rc.4"
                                ? "ad401b9c529f8f540213f81cf3a77c5b009bf711"
                                : version == "1.2.12-rc.3"
                                    ? "956f695ade6ab73c947bad9b78a01fc4fc2a154a"
                                    : version == "1.2.12-rc.2"
                                        ? "fa667ca009bab9e5621e16751ab86d014e4ee80b"
                                        : string.Empty,
                    IsInstalled = IsUsableInstalledVersion(Path.Combine(managerRoot, "versions", version), version),
                });
            }

            return publishedVersions;
        }

        private static string NormalizeArtifactUrl(string version, string artifactUrl)
        {
            var value = artifactUrl?.Trim() ?? string.Empty;
            var releasePageUrl = "https://github.com/mice/unity-agent-bridge/releases/tag/v" + version;
            return string.Equals(value.TrimEnd('/'), releasePageUrl, StringComparison.OrdinalIgnoreCase)
                ? CreateGitHubArtifactUrl(version)
                : value;
        }

        private static string CreateGitHubArtifactUrl(string version)
        {
            return "https://github.com/mice/unity-agent-bridge/releases/download/v" + version +
                   "/unity-agent-bridge-" + version + "-win-x64.zip";
        }

        private static string CreateGitHubSourceArchiveUrl(string tag)
        {
            return "https://github.com/mice/unity-agent-bridge/archive/refs/tags/" + tag + ".zip";
        }

        private static string FindRuntimeExecutablePath(string runtimeRoot)
        {
            if (string.IsNullOrEmpty(runtimeRoot))
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                Path.Combine(runtimeRoot, "win-x64", "unity-agent-bridge.exe"),
                Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe"),
                Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "unity-agent-bridge.exe"),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return string.Empty;
        }

        public string ResolveLauncherPath(McpEditorSettings settings)
        {
            var root = ResolveRoot(settings);
            if (string.IsNullOrEmpty(root))
            {
                return string.Empty;
            }

            var candidates = new[]
            {
                Path.Combine(root, "launcher", "Start-UnityAgentBridge-Mcp.cmd"),
                Path.Combine(ResolveManagerRoot(settings), "bin", "agent-bridge-mcp.cmd"),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return string.Empty;
        }

        public string ResolveVersion(McpEditorSettings settings)
        {
            if (settings == null || !string.Equals(settings.RuntimeMode, MachineMode, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return ResolveVersion(settings, ResolveManagerRoot(settings));
        }

        internal static string ResolveDefaultManagerRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_HOME");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return Path.GetFullPath(overrideRoot.Trim());
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UnityAgentBridge");
        }

        internal static string ResolveManagerRoot(McpEditorSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.MachineRuntimeRoot))
            {
                return Path.GetFullPath(settings.MachineRuntimeRoot.Trim());
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var selectionPath = string.IsNullOrWhiteSpace(projectRoot)
                ? string.Empty
                : Path.Combine(projectRoot, ".unitymcp", RuntimeSelectionFileName);
            if (File.Exists(selectionPath))
            {
                try
                {
                    var selection = JsonUtility.FromJson<RuntimeSelectionFile>(File.ReadAllText(selectionPath));
                    if (selection != null && !string.IsNullOrWhiteSpace(selection.managerHome))
                    {
                        return Path.GetFullPath(selection.managerHome.Trim());
                    }
                }
                catch
                {
                    // Fall back to the machine default when project selection is malformed.
                }
            }

            return ResolveDefaultManagerRoot();
        }

        private static string ResolveVersion(McpEditorSettings settings, string managerRoot)
        {
            if (!string.IsNullOrWhiteSpace(settings.RuntimeVersion))
            {
                var exactVersion = settings.RuntimeVersion.Trim();
                return IsSupportedSemanticVersion(exactVersion) ? exactVersion : string.Empty;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var selectionPath = string.IsNullOrWhiteSpace(projectRoot)
                ? string.Empty
                : Path.Combine(projectRoot, ".unitymcp", RuntimeSelectionFileName);
            if (File.Exists(selectionPath))
            {
                try
                {
                    var selection = JsonUtility.FromJson<RuntimeSelectionFile>(File.ReadAllText(selectionPath));
                    if (selection != null && !string.IsNullOrWhiteSpace(selection.runtimeVersion))
                    {
                        var selectedVersion = selection.runtimeVersion.Trim();
                        return IsSupportedSemanticVersion(selectedVersion) ? selectedVersion : string.Empty;
                    }

                    if (selection != null && !string.IsNullOrWhiteSpace(selection.channel))
                    {
                        return ReadChannelVersion(managerRoot, selection.channel);
                    }
                }
                catch
                {
                    return string.Empty;
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.RuntimeChannel))
            {
                return ReadChannelVersion(managerRoot, settings.RuntimeChannel);
            }

            return ReadChannelVersion(managerRoot, "stable");
        }

        private static bool IsSupportedSemanticVersion(string value)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                value ?? string.Empty,
                "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?$") &&
                Version.TryParse(value.Split('-')[0], out _);
        }

        private static bool IsSupportedPublishedVersion(string value)
        {
            return IsSupportedSemanticVersion(value) &&
                   CompareSemanticVersionsDescending(value, MinimumPublishedRuntimeVersion) <= 0;
        }

        private static bool IsUsableInstalledVersion(string versionRoot, string version, string expectedCommit = null)
        {
            var manifestPath = Path.Combine(versionRoot, "release-manifest.json");
            if (!File.Exists(manifestPath) || string.IsNullOrWhiteSpace(FindRuntimeExecutablePath(Path.Combine(versionRoot, "runtime"))))
            {
                return false;
            }

            try
            {
                var manifest = JsonUtility.FromJson<ReleaseManifestFile>(File.ReadAllText(manifestPath));
                return manifest != null && string.Equals(manifest.version?.Trim(), version, StringComparison.Ordinal) &&
                    (expectedCommit == null || string.Equals(manifest.commitSha, expectedCommit, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static int CompareSemanticVersionsDescending(string left, string right)
        {
            var leftParts = left.Split(new[] { '-' }, 2);
            var rightParts = right.Split(new[] { '-' }, 2);
            var coreComparison = Version.Parse(rightParts[0]).CompareTo(Version.Parse(leftParts[0]));
            if (coreComparison != 0) return coreComparison;

            var leftHasPrerelease = leftParts.Length == 2;
            var rightHasPrerelease = rightParts.Length == 2;
            if (leftHasPrerelease != rightHasPrerelease) return leftHasPrerelease ? 1 : -1;

            if (!leftHasPrerelease) return 0;

            var leftPrerelease = ParsePrereleaseOrdinal(leftParts[1]);
            var rightPrerelease = ParsePrereleaseOrdinal(rightParts[1]);
            return rightPrerelease.CompareTo(leftPrerelease);
        }

        private static int ParsePrereleaseOrdinal(string value)
        {
            var match = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty, "(?:rc|preview|alpha|beta|nightly)[.-]?(\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out var ordinal) ? ordinal : 0;
        }

        private static string ReadChannelVersion(string managerRoot, string channel)
        {
            if (!string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(channel, "preview", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(channel, "nightly", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var channelPath = Path.Combine(managerRoot, "channels", channel + ".json");
            if (!File.Exists(channelPath))
            {
                return string.Empty;
            }

            try
            {
                var value = JsonUtility.FromJson<ChannelFile>(File.ReadAllText(channelPath));
                var version = value == null ? string.Empty : value.version?.Trim() ?? string.Empty;
                return IsSupportedSemanticVersion(version) ? version : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [Serializable]
        private sealed class RuntimeSelectionFile
        {
            public string runtimeMode;
            public string runtimeVersion;
            public string channel;
            public string managerHome;
        }

        [Serializable]
        private sealed class ChannelFile
        {
            public string version;
        }

        [Serializable]
        private sealed class ReleaseManifestFile
        {
            public string version;
            public string tag;
            public string gitTag;
            public string artifactUrl;
            public string commitSha;
            public string buildOrigin;
        }

        [Serializable]
        private sealed class PublishedRuntimeCatalogFile
        {
            public PublishedRuntimeCatalogEntry[] versions;
        }

        [Serializable]
        private sealed class PublishedRuntimeCatalogEntry
        {
            public string version;
            public string tag;
            public string artifactUrl;
            public string sourceArchiveUrl;
            public string commitSha;
        }
    }
}
