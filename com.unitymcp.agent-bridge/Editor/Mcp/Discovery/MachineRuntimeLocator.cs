using System;
using System.Collections.Generic;
using System.IO;
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
    }

    internal sealed class MachineRuntimeLocator
    {
        internal const string MachineMode = "machine";
        internal const string RuntimeSelectionFileName = "runtime-selection.json";
        internal const string MinimumPublishedRuntimeVersion = "1.2.12-rc.3";

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
            if (settings == null)
            {
                return Array.Empty<PublishedMachineRuntimeVersion>();
            }

            var managerRoot = ResolveManagerRoot(settings);
            var releasesRoot = Path.Combine(managerRoot, "releases");
            if (!Directory.Exists(releasesRoot))
            {
                return IsDefaultManagerRoot(managerRoot)
                    ? new List<PublishedMachineRuntimeVersion>(ReadPackagedPublishedVersions(managerRoot))
                    : Array.Empty<PublishedMachineRuntimeVersion>();
            }

            var publishedVersions = new List<PublishedMachineRuntimeVersion>();
            try
            {
                foreach (var releaseRoot in Directory.GetDirectories(releasesRoot))
                {
                    var version = Path.GetFileName(releaseRoot);
                    if (!IsSupportedPublishedVersion(version))
                    {
                        continue;
                    }

                    var manifestPath = Path.Combine(releaseRoot, "release-manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    var manifest = JsonUtility.FromJson<ReleaseManifestFile>(File.ReadAllText(manifestPath));
                    if (manifest == null || !string.Equals(manifest.version?.Trim(), version, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    publishedVersions.Add(new PublishedMachineRuntimeVersion
                    {
                        Version = version,
                        Tag = string.IsNullOrWhiteSpace(manifest.tag) ? "v" + version : manifest.tag.Trim(),
                        ArtifactUrl = NormalizeArtifactUrl(version, manifest.artifactUrl),
                        SourceArchiveUrl = CreateGitHubSourceArchiveUrl(string.IsNullOrWhiteSpace(manifest.tag) ? "v" + version : manifest.tag.Trim()),
                        CommitSha = manifest.commitSha?.Trim() ?? string.Empty,
                        IsInstalled = IsUsableInstalledVersion(Path.Combine(managerRoot, "versions", version), version),
                    });
                }
            }
            catch (IOException)
            {
                return Array.Empty<PublishedMachineRuntimeVersion>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<PublishedMachineRuntimeVersion>();
            }

            if (publishedVersions.Count == 0 && IsDefaultManagerRoot(managerRoot))
            {
                publishedVersions.AddRange(ReadPackagedPublishedVersions(managerRoot));
            }

            publishedVersions.Sort((left, right) => CompareSemanticVersionsDescending(left.Version, right.Version));
            return publishedVersions;
        }

        private static bool IsDefaultManagerRoot(string managerRoot)
        {
            return !string.IsNullOrWhiteSpace(managerRoot) &&
                   string.Equals(
                       Path.GetFullPath(managerRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       Path.GetFullPath(ResolveDefaultManagerRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<PublishedMachineRuntimeVersion> ReadPackagedPublishedVersions(string managerRoot)
        {
            var catalogPath = string.Empty;
            try
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(MachineRuntimeLocator).Assembly);
                if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                {
                    catalogPath = Path.Combine(packageInfo.resolvedPath, "Documentation~", "published-runtime-versions.json");
                }

                if (!File.Exists(catalogPath))
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
                return CreateBuiltInPublishedVersions(managerRoot);
            }

            if (!File.Exists(catalogPath))
            {
                return CreateBuiltInPublishedVersions(managerRoot);
            }

            try
            {
                var catalog = JsonUtility.FromJson<PublishedRuntimeCatalogFile>(File.ReadAllText(catalogPath));
                if (catalog?.versions == null)
                {
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
                return CreateBuiltInPublishedVersions(managerRoot);
            }
        }

        internal static IReadOnlyList<PublishedMachineRuntimeVersion> CreateBuiltInPublishedVersions(string managerRoot)
        {
            var versions = new[]
            {
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
                    ArtifactUrl = version == "1.2.12-rc.3" ? string.Empty : CreateGitHubArtifactUrl(version),
                    SourceArchiveUrl = CreateGitHubSourceArchiveUrl(tag),
                    CommitSha = version == "1.2.12-rc.3"
                        ? "7affaffda8c3ddb7c47dd63831d3a5d67863cbc8"
                        : "fa667ca009bab9e5621e16751ab86d014e4ee80b",
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

        private static string ResolveManagerRoot(McpEditorSettings settings)
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
                "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?$");
        }

        private static bool IsSupportedPublishedVersion(string value)
        {
            return IsSupportedSemanticVersion(value) &&
                   CompareSemanticVersionsDescending(value, MinimumPublishedRuntimeVersion) <= 0;
        }

        private static bool IsUsableInstalledVersion(string versionRoot, string version)
        {
            var manifestPath = Path.Combine(versionRoot, "release-manifest.json");
            if (!File.Exists(manifestPath) || string.IsNullOrWhiteSpace(FindRuntimeExecutablePath(Path.Combine(versionRoot, "runtime"))))
            {
                return false;
            }

            try
            {
                var manifest = JsonUtility.FromJson<ReleaseManifestFile>(File.ReadAllText(manifestPath));
                return manifest != null && string.Equals(manifest.version?.Trim(), version, StringComparison.Ordinal);
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

            return string.Compare(right, left, StringComparison.Ordinal);
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
            public string artifactUrl;
            public string commitSha;
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
