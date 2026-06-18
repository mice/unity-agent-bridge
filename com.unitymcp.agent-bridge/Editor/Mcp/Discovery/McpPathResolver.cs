using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpPathResolver
    {
        private readonly Func<string> _projectRootProvider;

        public McpPathResolver()
            : this(null)
        {
        }

        internal McpPathResolver(Func<string> projectRootProvider)
        {
            _projectRootProvider = projectRootProvider;
        }

        public string GetProjectRoot()
        {
            var projectRoot = _projectRootProvider != null
                ? _projectRootProvider()
                : Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot) ? string.Empty : projectRoot;
        }

        public string GetWorkspaceRoot()
        {
            return GetWorkspaceRoot(null);
        }

        public string GetWorkspaceRoot(McpEditorSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.WorkspaceRoot))
            {
                var configured = settings.WorkspaceRoot.Trim();
                var projectRoot = GetProjectRoot();
                if (Directory.Exists(configured) && IsWorkspaceRootAllowedForProject(projectRoot, configured))
                {
                    return Path.GetFullPath(configured);
                }
            }

            return ResolveWorkspaceRoot(GetProjectRoot());
        }

        public string GetRepositoryRoot()
        {
            var projectRoot = GetProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            var repoRoot = Directory.GetParent(projectRoot)?.FullName;
            return string.IsNullOrEmpty(repoRoot) ? string.Empty : repoRoot;
        }

        public string ResolveToolsRoot(McpEditorSettings settings)
        {
            var packageToolsRoot = TryResolvePackageToolsRoot();

            if (settings != null && !string.IsNullOrWhiteSpace(settings.ToolsRoot))
            {
                var configured = settings.ToolsRoot.Trim();
                if (Directory.Exists(configured))
                {
                    var configuredFullPath = Path.GetFullPath(configured);
                    if (!string.IsNullOrEmpty(packageToolsRoot) && IsLegacyRepositoryToolsRoot(configuredFullPath))
                    {
                        return packageToolsRoot;
                    }

                    return configuredFullPath;
                }
            }

            if (!string.IsNullOrEmpty(packageToolsRoot))
            {
                return packageToolsRoot;
            }

            var projectRoot = GetProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            var projectToolsRoot = Path.Combine(projectRoot, "Tools");
            if (Directory.Exists(projectToolsRoot))
            {
                return projectToolsRoot;
            }

            var deliveryToolsRoot = TryResolveInstalledPackageDeliveryToolsRoot();
            if (!string.IsNullOrEmpty(deliveryToolsRoot))
            {
                return deliveryToolsRoot;
            }

            var repoRoot = GetRepositoryRoot();
            if (string.IsNullOrEmpty(repoRoot))
            {
                return string.Empty;
            }

            var repoToolsRoot = Path.Combine(repoRoot, "Tools");
            return Directory.Exists(repoToolsRoot) ? repoToolsRoot : string.Empty;
        }

        public string ResolveMcpServerRoot(McpEditorSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.McpServerRoot))
            {
                var configured = settings.McpServerRoot.Trim();
                if (Directory.Exists(configured))
                {
                    return Path.GetFullPath(configured);
                }

                return string.Empty;
            }

            var runtimeRoot = ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrEmpty(runtimeRoot))
            {
                return string.Empty;
            }

            var preparedRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            if (Directory.Exists(preparedRoot))
            {
                return preparedRoot;
            }

            return string.Empty;
        }

        public string ResolveCliRoot(McpEditorSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.CliExecutablePath))
            {
                var configured = settings.CliExecutablePath.Trim();
                if (File.Exists(configured))
                {
                    return Path.GetDirectoryName(Path.GetFullPath(configured)) ?? string.Empty;
                }
            }

            var runtimeRoot = ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrEmpty(runtimeRoot))
            {
                return string.Empty;
            }

            var preparedRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli");
            if (Directory.Exists(preparedRoot))
            {
                return preparedRoot;
            }

            return string.Empty;
        }

        public string ResolveLauncherPath(McpEditorSettings settings)
        {
            var runtimeRoot = ResolveWorkspaceRuntimeRoot(settings);
            if (string.IsNullOrEmpty(runtimeRoot))
            {
                return string.Empty;
            }

            var preparedLauncherPath = Path.Combine(runtimeRoot, "AgentBridge", "Start-UnityAgentBridge-Mcp.cmd");
            if (File.Exists(preparedLauncherPath))
            {
                return preparedLauncherPath;
            }

            return string.Empty;
        }

        public string ResolveWorkspaceRuntimeRoot(McpEditorSettings settings)
        {
            var projectRoot = GetProjectRoot();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            return Path.Combine(Path.GetFullPath(projectRoot), ".unitymcp", "runtime");
        }

        public string ResolveExecutablePath(string configuredPath, string executableName)
        {
            if (TryResolveConfiguredPath(configuredPath, out var resolvedConfiguredPath))
            {
                return resolvedConfiguredPath;
            }

            if (string.IsNullOrWhiteSpace(executableName))
            {
                return string.Empty;
            }

            foreach (var candidate in EnumeratePathCandidates(executableName.Trim()))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return string.Empty;
        }

        internal bool TryResolveConfiguredPath(string configuredPath, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return false;
            }

            var trimmed = configuredPath.Trim();
            if (LooksLikeShellSnippet(trimmed))
            {
                return false;
            }

            if (!File.Exists(trimmed))
            {
                return false;
            }

            resolvedPath = Path.GetFullPath(trimmed);
            return true;
        }

        internal IEnumerable<string> EnumeratePathCandidates(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName))
            {
                yield break;
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                yield break;
            }

            foreach (var segment in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                string baseDirectory;
                try
                {
                    baseDirectory = segment.Trim();
                    if (!Directory.Exists(baseDirectory))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var fileName in EnumerateExecutableNames(executableName))
                {
                    yield return Path.Combine(baseDirectory, fileName);
                }
            }
        }

        private static IEnumerable<string> EnumerateExecutableNames(string executableName)
        {
            var hasExtension = Path.HasExtension(executableName);
            if (hasExtension)
            {
                yield return executableName;
                yield break;
            }

            if (IsWindows())
            {
                yield return executableName + ".exe";
                yield return executableName + ".cmd";
                yield return executableName + ".bat";
                yield return executableName;
                yield break;
            }

            yield return executableName;
        }

        private static bool LooksLikeShellSnippet(string value)
        {
            return value.IndexOfAny(new[] { '|', '&', ';', '>', '<', '\r', '\n' }) >= 0;
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        private static string TryResolveInstalledPackageDeliveryToolsRoot()
        {
            try
            {
                var packageFilePath = new Uri(typeof(McpPathResolver).Assembly.CodeBase).LocalPath;
                var packageRoot = Directory.GetParent(packageFilePath);
                if (packageRoot == null)
                {
                    return string.Empty;
                }

                var current = packageRoot;
                while (current != null)
                {
                    var candidate = Path.Combine(current.FullName, "Tools");
                    if (Directory.Exists(candidate) &&
                        Directory.Exists(Path.Combine(candidate, "AgentBridge")) &&
                        Directory.Exists(Path.Combine(candidate, "UnityAgentBridge")))
                    {
                        return candidate;
                    }

                    current = current.Parent;
                }
            }
            catch
            {
                // Ignore resolution errors and allow fallback to repository discovery.
            }

            return string.Empty;
        }

        internal static string TryResolvePackageToolsRoot()
        {
            try
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(McpPathResolver).Assembly);
                var packageRoot = packageInfo != null ? packageInfo.resolvedPath : string.Empty;
                if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
                {
                    return string.Empty;
                }

                var toolsRoot = Path.Combine(packageRoot, "Tools~");
                if (!Directory.Exists(toolsRoot))
                {
                    return string.Empty;
                }

                var launcherPath = Path.Combine(toolsRoot, "AgentBridge", "Start-UnityAgentBridge-Mcp.cmd");
                var cliRoot = Path.Combine(toolsRoot, "UnityAgentBridge", "cli");
                return File.Exists(launcherPath) && Directory.Exists(cliRoot)
                    ? Path.GetFullPath(toolsRoot)
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static string ResolveWorkspaceRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            var current = new DirectoryInfo(Path.GetFullPath(projectRoot));
            while (current != null)
            {
                if (LooksLikeWorkspaceRoot(current.FullName))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return Path.GetFullPath(projectRoot);
        }

        internal static bool IsWorkspaceRootAllowedForProject(string projectRoot, string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(workspaceRoot))
            {
                return false;
            }

            var current = new DirectoryInfo(Path.GetFullPath(projectRoot));
            var target = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var remainingDepth = 3;
            while (current != null && remainingDepth >= 0)
            {
                var currentPath = current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(currentPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.Parent;
                remainingDepth--;
            }

            return false;
        }

        private static bool LooksLikeWorkspaceRoot(string candidateRoot)
        {
            if (string.IsNullOrWhiteSpace(candidateRoot) || !Directory.Exists(candidateRoot))
            {
                return false;
            }

            var fullCandidateRoot = Path.GetFullPath(candidateRoot);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) &&
                string.Equals(fullCandidateRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              Path.GetFullPath(userProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Directory.Exists(Path.Combine(candidateRoot, ".git")))
            {
                return true;
            }

            if (File.Exists(Path.Combine(candidateRoot, ".mcp.json")))
            {
                return true;
            }

            if (File.Exists(Path.Combine(candidateRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json")))
            {
                return true;
            }

            var codexDirectory = Path.Combine(candidateRoot, ".codex");
            return Directory.Exists(codexDirectory);
        }

        private bool IsLegacyRepositoryToolsRoot(string toolsRoot)
        {
            var repoRoot = GetRepositoryRoot();
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return false;
            }

            var legacyToolsRoot = Path.GetFullPath(Path.Combine(repoRoot, "Tools"));
            return string.Equals(
                toolsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                legacyToolsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
