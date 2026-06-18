using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class ManagedJsonMerger
    {
        private const string ManagedKey = "unity_agent_bridge";
        private const string DefaultContainerName = "mcpServers";

        public ManagedBlockApplyResult Apply(string targetPath, string managedJson)
        {
            return Apply(targetPath, managedJson, DefaultContainerName);
        }

        public ManagedBlockApplyResult Apply(string targetPath, string managedJson, string containerName)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("targetPath is required.", nameof(targetPath));
            }

            if (string.IsNullOrWhiteSpace(managedJson))
            {
                throw new ArgumentException("managedJson is required.", nameof(managedJson));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);

            string originalText = null;
            if (File.Exists(targetPath))
            {
                originalText = File.ReadAllText(targetPath);
                if (!LooksLikeJsonObject(originalText))
                {
                    var backupPath = TryBackupBrokenJson(targetPath, originalText, out var backupFailed);
                    return new ManagedBlockApplyResult
                    {
                        Applied = false,
                        TargetPath = targetPath,
                        BackupPath = backupPath,
                        Reason = backupFailed ? "backup_failed" : "parse_failed",
                    };
                }
            }

            var merged = MergeJsonText(originalText, managedJson, NormalizeContainerName(containerName));
            File.WriteAllText(targetPath, merged);
            return new ManagedBlockApplyResult
            {
                Applied = true,
                TargetPath = targetPath,
            };
        }

        public ManagedBlockApplyResult Remove(string targetPath)
        {
            return Remove(targetPath, DefaultContainerName);
        }

        public ManagedBlockApplyResult Remove(string targetPath, string containerName)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("targetPath is required.", nameof(targetPath));
            }

            if (!File.Exists(targetPath))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    Reason = "missing_target",
                };
            }

            var originalText = File.ReadAllText(targetPath);
            if (!LooksLikeJsonObject(originalText))
            {
                var backupPath = TryBackupBrokenJson(targetPath, originalText, out var backupFailed);
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    BackupPath = backupPath,
                    Reason = backupFailed ? "backup_failed" : "parse_failed",
                };
            }

            var updated = RemoveManagedServer(originalText, NormalizeContainerName(containerName));
            File.WriteAllText(targetPath, updated);
            return new ManagedBlockApplyResult
            {
                Applied = true,
                TargetPath = targetPath,
            };
        }

        private static string MergeJsonText(string originalText, string managedJson, string containerName)
        {
            var managedEntry = "\"unity_agent_bridge\": " + managedJson.Trim();

            if (string.IsNullOrWhiteSpace(originalText))
            {
                return "{\n  \"" + containerName + "\": {\n    " + managedEntry + "\n  }\n}";
            }

            var existing = originalText.Trim();
            if (existing == "{}")
            {
                return "{\n  \"" + containerName + "\": {\n    " + managedEntry + "\n  }\n}";
            }

            var containerIndex = existing.IndexOf("\"" + containerName + "\"", StringComparison.Ordinal);
            if (containerIndex < 0)
            {
                var insertIndex = existing.LastIndexOf('}');
                if (insertIndex < 0)
                {
                    return "{\n  \"" + containerName + "\": {\n    " + managedEntry + "\n  }\n}";
                }

                var prefix = existing.Substring(0, insertIndex).TrimEnd();
                var needsComma = !prefix.EndsWith("{", StringComparison.Ordinal);
                var builder = new StringBuilder();
                builder.Append(prefix);
                if (needsComma)
                {
                    builder.Append(',');
                }

                builder.Append("\n  \"");
                builder.Append(containerName);
                builder.Append("\": {\n    ");
                builder.Append(managedEntry);
                builder.Append("\n  }\n}");
                return builder.ToString();
            }

            var openBraceIndex = existing.IndexOf('{', containerIndex);
            var closeBraceIndex = FindMatchingBrace(existing, openBraceIndex);
            if (openBraceIndex < 0 || closeBraceIndex < 0)
            {
                return existing;
            }

            var sectionContent = existing.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1).Trim();
            sectionContent = RemoveManagedEntry(sectionContent);

            var builderWithSection = new StringBuilder();
            builderWithSection.Append(existing.Substring(0, openBraceIndex + 1));
            if (!string.IsNullOrWhiteSpace(sectionContent))
            {
                builderWithSection.Append('\n');
                builderWithSection.Append(sectionContent.Trim());
                builderWithSection.Append(",\n    ");
            }
            else
            {
                builderWithSection.Append("\n    ");
            }

            builderWithSection.Append(managedEntry);
            builderWithSection.Append('\n');
            builderWithSection.Append(existing.Substring(closeBraceIndex));
            return builderWithSection.ToString();
        }

        private static string RemoveManagedServer(string originalText, string containerName)
        {
            var existing = originalText.Trim();
            var containerIndex = existing.IndexOf("\"" + containerName + "\"", StringComparison.Ordinal);
            if (containerIndex < 0)
            {
                return existing;
            }

            var openBraceIndex = existing.IndexOf('{', containerIndex);
            var closeBraceIndex = FindMatchingBrace(existing, openBraceIndex);
            if (openBraceIndex < 0 || closeBraceIndex < 0)
            {
                return existing;
            }

            var sectionContent = existing.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
            sectionContent = RemoveManagedEntry(sectionContent).Trim();

            var builder = new StringBuilder();
            builder.Append(existing.Substring(0, openBraceIndex + 1));
            if (!string.IsNullOrEmpty(sectionContent))
            {
                builder.Append('\n');
                builder.Append(sectionContent);
                builder.Append('\n');
            }

            builder.Append(existing.Substring(closeBraceIndex));
            return builder.ToString();
        }

        private static string RemoveManagedEntry(string sectionContent)
        {
            var keyIndex = sectionContent.IndexOf("\"" + ManagedKey + "\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return sectionContent;
            }

            var colonIndex = sectionContent.IndexOf(':', keyIndex);
            var objectStart = sectionContent.IndexOf('{', colonIndex);
            var objectEnd = FindMatchingBrace(sectionContent, objectStart);
            if (colonIndex < 0 || objectStart < 0 || objectEnd < 0)
            {
                return sectionContent;
            }

            var entryStart = keyIndex;
            while (entryStart > 0 && char.IsWhiteSpace(sectionContent[entryStart - 1]))
            {
                entryStart--;
            }

            if (entryStart > 0 && sectionContent[entryStart - 1] == ',')
            {
                entryStart--;
            }

            var entryEnd = objectEnd + 1;
            while (entryEnd < sectionContent.Length && char.IsWhiteSpace(sectionContent[entryEnd]))
            {
                entryEnd++;
            }

            if (entryEnd < sectionContent.Length && sectionContent[entryEnd] == ',')
            {
                entryEnd++;
            }

            return sectionContent.Remove(entryStart, entryEnd - entryStart).Trim().Trim(',');
        }

        private static string NormalizeContainerName(string containerName)
        {
            return string.IsNullOrWhiteSpace(containerName)
                ? DefaultContainerName
                : containerName.Trim();
        }

        private static int FindMatchingBrace(string text, int openBraceIndex)
        {
            if (openBraceIndex < 0 || openBraceIndex >= text.Length || text[openBraceIndex] != '{')
            {
                return -1;
            }

            var depth = 0;
            for (var index = openBraceIndex; index < text.Length; index++)
            {
                if (text[index] == '{')
                {
                    depth++;
                }
                else if (text[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        private static bool LooksLikeJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var trimmed = text.Trim();
            return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
        }

        private static string TryBackupBrokenJson(string targetPath, string originalText, out bool backupFailed)
        {
            backupFailed = false;
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    backupFailed = true;
                    return string.Empty;
                }

                var backupDirectory = Path.Combine(projectRoot, "Library", "AgentBridge", "backups");
                Directory.CreateDirectory(backupDirectory);

                var bytes = Encoding.UTF8.GetBytes(originalText ?? string.Empty);
                var hash = ComputeShortHash(bytes);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var backupPath = Path.Combine(backupDirectory, "mcp.json.broken-" + timestamp + "-" + hash + ".bak");
                File.WriteAllBytes(backupPath, bytes);

                var logPath = Path.Combine(projectRoot, "Library", "AgentBridge", "logs", "mcp-setup.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? string.Empty);
                File.AppendAllText(logPath, "event=mcp_json_parse_failed backup=" + backupPath + " size=" + bytes.Length + " sha256=" + ComputeSha256(bytes) + Environment.NewLine);
                return backupPath;
            }
            catch (Exception)
            {
                backupFailed = true;
                return string.Empty;
            }
        }

        private static string ComputeShortHash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
