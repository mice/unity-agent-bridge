using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class CodexProjectConfigWriter : IMcpClientConfigWriter
    {
        private readonly CodexTomlConfigEditor _configEditor;
        private readonly McpPathResolver _pathResolver;

        public CodexProjectConfigWriter()
            : this(new CodexTomlConfigEditor(), new McpPathResolver())
        {
        }

        internal CodexProjectConfigWriter(CodexTomlConfigEditor configEditor, McpPathResolver pathResolver)
        {
            _configEditor = configEditor ?? throw new ArgumentNullException(nameof(configEditor));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ManagedBlockApplyResult Apply(McpEditorSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!TryBuildExecutableCommand(settings, _pathResolver, out var executableCommand))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = GetTargetPath(settings, _pathResolver),
                    Reason = "cli_executable_missing",
                };
            }

            var targetPath = GetTargetPath(settings, _pathResolver);
            return _configEditor.Apply(targetPath, executableCommand, GetProjectRoot(_pathResolver));
        }

        public ManagedBlockApplyResult Remove()
        {
            var targetPath = GetTargetPath(_pathResolver);
            return _configEditor.Remove(targetPath);
        }

        public string Preview(McpEditorSettings settings)
        {
            if (!TryBuildExecutableCommand(settings, _pathResolver, out var executableCommand))
            {
                return "# cli_executable_missing" + Environment.NewLine +
                       "# Resolved unity_agent_bridge executable path does not exist." + Environment.NewLine +
                       "# Prepare the project-local MCP runtime before applying managed MCP config.";
            }

            return new ManagedBlockTextEditor().Apply(string.Empty, BuildManagedBlockBody(executableCommand, GetProjectRoot(_pathResolver), string.Empty));
        }

        internal static string GetTargetPath()
        {
            return GetTargetPath(new McpPathResolver());
        }

        internal static string GetTargetPath(McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            var workspaceRoot = resolver.GetWorkspaceRoot();
            return GetTargetPath(workspaceRoot);
        }

        internal static string GetTargetPath(McpEditorSettings settings, McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            var workspaceRoot = resolver.GetWorkspaceRoot(settings);
            return GetTargetPath(workspaceRoot);
        }

        internal static string GetTargetPath(string workspaceRoot)
        {
            if (string.IsNullOrEmpty(workspaceRoot))
            {
                throw new InvalidOperationException("Unable to determine MCP workspace root.");
            }

            return Path.Combine(workspaceRoot, ".codex", "config.toml");
        }

        internal static string BuildManagedBlockBody(string executableCommand)
        {
            return BuildManagedBlockBody(executableCommand, string.Empty, string.Empty, false);
        }

        internal static string BuildManagedBlockBody(string executableCommand, string preservedChildSections)
        {
            return BuildManagedBlockBody(executableCommand, string.Empty, preservedChildSections, false);
        }

        internal static string BuildManagedBlockBody(string executableCommand, string projectRoot, string preservedChildSections)
        {
            return BuildManagedBlockBody(executableCommand, projectRoot, preservedChildSections, false);
        }

        internal static string BuildManagedBlockBody(string executableCommand, string projectRoot, string preservedChildSections, bool executableCommandIsBody)
        {
            var executable = executableCommandIsBody ? executableCommand : EscapeTomlString(executableCommand);
            var body = "[mcp_servers.unity_agent_bridge]" + Environment.NewLine +
                       "command = \"" + executable + "\"" + Environment.NewLine +
                       "args = [\"mcp-server\"]" + Environment.NewLine +
                       "cwd = \".\"" + Environment.NewLine +
                       "startup_timeout_sec = 20" + Environment.NewLine +
                       "tool_timeout_sec = 300" + Environment.NewLine +
                       "required = false";

            var unityProjectPath = NormalizeProjectRoot(projectRoot);
            if (!string.IsNullOrEmpty(unityProjectPath))
            {
                body += Environment.NewLine + Environment.NewLine +
                        "[mcp_servers.unity_agent_bridge.env]" + Environment.NewLine +
                        "UNITY_AGENT_BRIDGE_PROJECT_PATH = \"" + EscapeTomlString(unityProjectPath) + "\"";
            }

            var preserved = NormalizeLineEndings(preservedChildSections).Trim();
            if (string.IsNullOrEmpty(preserved))
            {
                return body;
            }

            return body + Environment.NewLine + Environment.NewLine + preserved;
        }

        internal static string GetProjectRoot(McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            return NormalizeProjectRoot(resolver.GetProjectRoot());
        }

        internal static string BuildExecutableCommand(McpEditorSettings settings, McpPathResolver pathResolver)
        {
            var resolver = pathResolver ?? new McpPathResolver();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.CliExecutablePath) && File.Exists(settings.CliExecutablePath))
            {
                return Path.GetFullPath(settings.CliExecutablePath);
            }

            var cliRoot = resolver.ResolveCliRoot(settings);
            if (!string.IsNullOrEmpty(cliRoot))
            {
                var executableName = McpRuntimeInitializer.GetProductExecutableName();
                var ridPath = Path.Combine(cliRoot, "out", McpRuntimeInitializer.GetCurrentRid(), executableName);
                if (File.Exists(ridPath))
                {
                    return Path.GetFullPath(ridPath);
                }

                var rootPath = Path.Combine(cliRoot, executableName);
                if (File.Exists(rootPath))
                {
                    return Path.GetFullPath(rootPath);
                }
            }

            if (settings != null && !string.IsNullOrWhiteSpace(settings.ToolsRoot))
            {
                var runtimeRoot = resolver.ResolveWorkspaceRuntimeRoot(settings);
                if (!string.IsNullOrEmpty(runtimeRoot))
                {
                    return Path.Combine(
                        runtimeRoot,
                        "UnityAgentBridge",
                        "cli",
                        "out",
                        McpRuntimeInitializer.GetCurrentRid(),
                        McpRuntimeInitializer.GetProductExecutableName());
                }
            }

            return string.Empty;
        }

        internal static bool TryBuildExecutableCommand(McpEditorSettings settings, McpPathResolver pathResolver, out string executableCommand)
        {
            executableCommand = BuildExecutableCommand(settings, pathResolver);
            return !string.IsNullOrWhiteSpace(executableCommand) && File.Exists(executableCommand);
        }

        private static string EscapeTomlString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            return Path.GetFullPath(projectRoot.Trim());
        }

        internal static bool IsPathUnderRoot(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var fullCandidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasUnmanagedUnityAgentBridgeSection(string originalText)
        {
            var unmanagedOnly = new ManagedBlockTextEditor().Remove(originalText ?? string.Empty);
            return unmanagedOnly.IndexOf("[mcp_servers.unity_agent_bridge]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string ApplyManagedContent(string originalText, string managedBlockBody, ManagedBlockTextEditor textEditor)
        {
            var editor = textEditor ?? new ManagedBlockTextEditor();
            var original = originalText ?? string.Empty;
            if (!HasManagedBlock(original) && TryReplaceStandaloneUnityAgentBridgeSection(original, managedBlockBody, out var replaced))
            {
                return replaced;
            }

            return editor.Apply(original, managedBlockBody);
        }

        internal static bool ValidateManagedTomlResult(string updatedText)
        {
            var normalized = NormalizeLineEndings(updatedText ?? string.Empty);
            if (CountOccurrences(normalized, ManagedBlockTextEditor.BeginMarker) != CountOccurrences(normalized, ManagedBlockTextEditor.EndMarker))
            {
                return false;
            }

            var unmanagedOnly = new ManagedBlockTextEditor().Remove(normalized);
            return CountOccurrences(unmanagedOnly, "[mcp_servers.unity_agent_bridge]") == 0;
        }

        private static bool TryReplaceStandaloneUnityAgentBridgeSection(string originalText, string managedBlockBody, out string updatedText)
        {
            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            const string header = "[mcp_servers.unity_agent_bridge]";
            var headerIndex = normalized.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (headerIndex < 0)
            {
                updatedText = string.Empty;
                return false;
            }

            var sectionStart = FindLineStart(normalized, headerIndex);
            var sectionEnd = FindUnityAgentBridgeSectionEnd(normalized, sectionStart);
            var before = normalized.Substring(0, sectionStart).TrimEnd('\n');
            var after = normalized.Substring(sectionEnd).TrimStart('\n');
            var managedBlock = WrapManagedBlock(managedBlockBody);

            if (string.IsNullOrEmpty(before) && string.IsNullOrEmpty(after))
            {
                updatedText = managedBlock + Environment.NewLine;
                return true;
            }

            if (string.IsNullOrEmpty(before))
            {
                updatedText = managedBlock + Environment.NewLine + Environment.NewLine + after;
                return true;
            }

            if (string.IsNullOrEmpty(after))
            {
                updatedText = before + Environment.NewLine + Environment.NewLine + managedBlock + Environment.NewLine;
                return true;
            }

            updatedText = before + Environment.NewLine + Environment.NewLine + managedBlock + Environment.NewLine + Environment.NewLine + after;
            return true;
        }

        private static bool HasManagedBlock(string originalText)
        {
            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            return normalized.IndexOf(ManagedBlockTextEditor.BeginMarker, StringComparison.Ordinal) >= 0
                   && normalized.IndexOf(ManagedBlockTextEditor.EndMarker, StringComparison.Ordinal) >= 0;
        }

        private static string WrapManagedBlock(string managedBlockBody)
        {
            var body = NormalizeLineEndings(managedBlockBody ?? string.Empty).Trim();
            return ManagedBlockTextEditor.BeginMarker + Environment.NewLine +
                   body + Environment.NewLine +
                   ManagedBlockTextEditor.EndMarker;
        }

        private static int FindLineStart(string text, int index)
        {
            var start = index;
            while (start > 0 && text[start - 1] != '\n')
            {
                start--;
            }

            return start;
        }

        private static int FindUnityAgentBridgeSectionEnd(string text, int sectionStart)
        {
            var scanIndex = text.IndexOf('\n', sectionStart);
            if (scanIndex < 0)
            {
                return text.Length;
            }

            scanIndex++;
            while (scanIndex < text.Length)
            {
                var lineEnd = text.IndexOf('\n', scanIndex);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                var line = text.Substring(scanIndex, lineEnd - scanIndex);
                var trimmed = line.TrimStart();
                if (IsSectionHeader(trimmed) && !IsUnityAgentBridgeHeader(trimmed) && !IsUnityAgentBridgeChildHeader(trimmed))
                {
                    return scanIndex;
                }

                scanIndex = lineEnd < text.Length ? lineEnd + 1 : text.Length;
            }

            return text.Length;
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
            {
                return 0;
            }

            var count = 0;
            var startIndex = 0;
            while (true)
            {
                var index = text.IndexOf(token, startIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return count;
                }

                count++;
                startIndex = index + token.Length;
            }
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        internal static string ExtractUnityAgentBridgeChildSections(string originalText)
        {
            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            var collected = string.Empty;
            var scanIndex = 0;
            while (scanIndex < normalized.Length)
            {
                var lineEnd = normalized.IndexOf('\n', scanIndex);
                if (lineEnd < 0)
                {
                    lineEnd = normalized.Length;
                }

                var line = normalized.Substring(scanIndex, lineEnd - scanIndex);
                var trimmed = line.TrimStart();
                if (IsUnityAgentBridgeChildHeader(trimmed))
                {
                    var sectionEnd = FindNextSectionStart(normalized, scanIndex);
                    var section = normalized.Substring(scanIndex, sectionEnd - scanIndex).Trim();
                    if (!string.IsNullOrEmpty(section))
                    {
                        collected = string.IsNullOrEmpty(collected)
                            ? section
                            : collected + Environment.NewLine + Environment.NewLine + section;
                    }

                    scanIndex = sectionEnd;
                    continue;
                }

                scanIndex = lineEnd < normalized.Length ? lineEnd + 1 : normalized.Length;
            }

            return collected;
        }

        private static int FindNextSectionStart(string text, int startIndex)
        {
            var scanIndex = text.IndexOf('\n', startIndex);
            if (scanIndex < 0)
            {
                return text.Length;
            }

            scanIndex++;
            while (scanIndex < text.Length)
            {
                var lineEnd = text.IndexOf('\n', scanIndex);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                var line = text.Substring(scanIndex, lineEnd - scanIndex);
                if (IsSectionHeader(line.TrimStart()))
                {
                    return scanIndex;
                }

                scanIndex = lineEnd < text.Length ? lineEnd + 1 : text.Length;
            }

            return text.Length;
        }

        private static bool IsSectionHeader(string trimmedLine)
        {
            return trimmedLine.StartsWith("[", StringComparison.Ordinal);
        }

        private static bool IsUnityAgentBridgeHeader(string trimmedLine)
        {
            return trimmedLine.StartsWith("[mcp_servers.unity_agent_bridge]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnityAgentBridgeChildHeader(string trimmedLine)
        {
            return trimmedLine.StartsWith("[mcp_servers.unity_agent_bridge.", StringComparison.OrdinalIgnoreCase)
                   || trimmedLine.StartsWith("[[mcp_servers.unity_agent_bridge.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
