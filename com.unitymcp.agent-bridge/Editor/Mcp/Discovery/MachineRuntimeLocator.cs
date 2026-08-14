using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class MachineRuntimeLocator
    {
        internal const string MachineMode = "machine";
        internal const string RuntimeSelectionFileName = "runtime-selection.json";

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
                Path.Combine(ResolveManagerRoot(settings), "bin", "agent-bridge-mcp.cmd"),
                Path.Combine(root, "launcher", "Start-UnityAgentBridge-Mcp.cmd"),
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
            var root = ResolveRoot(settings);
            return string.IsNullOrEmpty(root) ? string.Empty : Path.GetFileName(root);
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
    }
}
