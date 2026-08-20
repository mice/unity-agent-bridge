using System;
using System.IO;
using System.Text;
using Tommy;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class ManagedTomlConfigEditor
    {
        private readonly ManagedBlockTextEditor _textEditor;

        public ManagedTomlConfigEditor()
            : this(new ManagedBlockTextEditor())
        {
        }

        internal ManagedTomlConfigEditor(ManagedBlockTextEditor textEditor)
        {
            _textEditor = textEditor ?? throw new ArgumentNullException(nameof(textEditor));
        }

        public ManagedBlockApplyResult Apply(
            string targetPath,
            Func<string, string> managedBlockFactory,
            bool createBackup)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("targetPath must not be empty.", nameof(targetPath));
            }

            if (managedBlockFactory == null)
            {
                throw new ArgumentNullException(nameof(managedBlockFactory));
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var original = File.Exists(targetPath) ? File.ReadAllText(targetPath) : string.Empty;
            var preservedChildSections = ExcludeOwnedChildSections(
                CodexProjectConfigWriter.ExtractUnityAgentBridgeChildSections(original));
            var managedBlockBody = managedBlockFactory(preservedChildSections);
            var updated = CodexProjectConfigWriter.ApplyManagedContent(
                NormalizeLineEndings(original),
                managedBlockBody,
                _textEditor);

            if (!ValidateManagedResult(updated))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    Reason = "format_validation_failed",
                };
            }

            return WriteResult(targetPath, updated, createBackup);
        }

        public ManagedBlockApplyResult Remove(string targetPath, bool createBackup)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("targetPath must not be empty.", nameof(targetPath));
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

            var updated = _textEditor.Remove(File.ReadAllText(targetPath));
            if (!TryParseToml(updated))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    Reason = "format_validation_failed",
                };
            }

            return WriteResult(targetPath, updated, createBackup);
        }

        internal static string AppendPreservedChildSections(string managedBlockBody, string preservedChildSections)
        {
            var body = NormalizeLineEndings(managedBlockBody ?? string.Empty).Trim();
            var preserved = NormalizeLineEndings(preservedChildSections ?? string.Empty).Trim();
            return string.IsNullOrEmpty(preserved)
                ? body
                : body + Environment.NewLine + Environment.NewLine + preserved;
        }

        private ManagedBlockApplyResult WriteResult(string targetPath, string updated, bool createBackup)
        {
            var backupPath = string.Empty;
            if (createBackup && File.Exists(targetPath))
            {
                backupPath = targetPath + ".bak";
                try
                {
                    File.Copy(targetPath, backupPath, true);
                }
                catch (Exception)
                {
                    return new ManagedBlockApplyResult
                    {
                        Applied = false,
                        TargetPath = targetPath,
                        BackupPath = backupPath,
                        Reason = "backup_failed",
                    };
                }
            }

            try
            {
                WriteAtomically(targetPath, updated);
                return new ManagedBlockApplyResult
                {
                    Applied = true,
                    TargetPath = targetPath,
                    BackupPath = backupPath,
                };
            }
            catch (Exception)
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    BackupPath = backupPath,
                    Reason = "write_failed",
                };
            }
        }

        private static void WriteAtomically(string targetPath, string updated)
        {
            var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, updated ?? string.Empty, new UTF8Encoding(false));
                if (File.Exists(targetPath))
                {
                    File.Replace(temporaryPath, targetPath, null);
                }
                else
                {
                    File.Move(temporaryPath, targetPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool ValidateManagedResult(string updated)
        {
            return CodexProjectConfigWriter.ValidateManagedTomlResult(updated) && TryParseToml(updated);
        }

        private static bool TryParseToml(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            try
            {
                using var reader = new StringReader(text);
                return TOML.Parse(reader) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string ExcludeOwnedChildSections(string childSections)
        {
            var normalized = NormalizeLineEndings(childSections ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var scanIndex = 0;
            while (scanIndex < normalized.Length)
            {
                var sectionEnd = FindNextSectionStart(normalized, scanIndex);
                var section = normalized.Substring(scanIndex, sectionEnd - scanIndex).Trim();
                var lineEnd = section.IndexOf('\n');
                var header = (lineEnd < 0 ? section : section.Substring(0, lineEnd)).Trim();
                if (!IsOwnedEnvironmentHeader(header) && !string.IsNullOrEmpty(section))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(Environment.NewLine);
                        builder.Append(Environment.NewLine);
                    }

                    builder.Append(section);
                }

                scanIndex = sectionEnd;
            }

            return builder.ToString();
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

                if (text.Substring(scanIndex, lineEnd - scanIndex).TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    return scanIndex;
                }

                scanIndex = lineEnd < text.Length ? lineEnd + 1 : text.Length;
            }

            return text.Length;
        }

        private static bool IsOwnedEnvironmentHeader(string header)
        {
            return string.Equals(
                header,
                "[mcp_servers.unity_agent_bridge.env]",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
