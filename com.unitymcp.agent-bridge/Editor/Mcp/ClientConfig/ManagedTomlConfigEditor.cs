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
            string updated;
            try
            {
                var document = Parse(original);
                var generated = Parse(managedBlockFactory(string.Empty));
                var generatedServer = GetServer(generated);
                var server = GetServer(document);
                foreach (var pair in generatedServer.RawTable)
                {
                    if (pair.Key == "env" && pair.Value is TomlTable generatedEnv)
                    {
                        if (!server.HasKey("env")) server["env"] = new TomlTable();
                        if (!(server["env"] is TomlTable existingEnv)) throw new FormatException("env must be a table.");
                        foreach (var variable in generatedEnv.RawTable) existingEnv[variable.Key] = variable.Value;
                    }
                    else if (pair.Key == "command" || pair.Key == "args" || pair.Key == "cwd" || !server.HasKey(pair.Key))
                    {
                        server[pair.Key] = pair.Value;
                    }
                }

                var servers = (TomlTable)document["mcp_servers"];
                servers.Delete("unity_agent_bridge");
                if (servers.ChildrenCount == 0) document.Delete("mcp_servers");
                // Regenerate ownership markers; parsed comments may contain old markers.
                ClearManagedComments(document);
                ClearManagedComments(server);
                using var remainder = new StringWriter();
                document.WriteTo(remainder);
                using var block = new StringWriter();
                server.WriteTo(block, "mcp_servers.unity_agent_bridge");
                updated = _textEditor.Apply(remainder.ToString(), block.ToString());
                if (!TryParseToml(updated)) throw new FormatException("Invalid merged TOML.");
            }
            catch
            {
                return new ManagedBlockApplyResult
                {
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

        private static TomlTable Parse(string text)
        {
            using var reader = new StringReader(text ?? string.Empty);
            return TOML.Parse(reader);
        }

        private static bool TryParseToml(string text)
        {
            try { Parse(text); return true; }
            catch { return false; }
        }

        private static TomlTable GetServer(TomlTable document)
        {
            if (!document.HasKey("mcp_servers")) document["mcp_servers"] = new TomlTable();
            if (!(document["mcp_servers"] is TomlTable servers)) throw new FormatException("mcp_servers must be a table.");
            if (!servers.HasKey("unity_agent_bridge")) servers["unity_agent_bridge"] = new TomlTable();
            return servers["unity_agent_bridge"] as TomlTable ?? throw new FormatException("unity_agent_bridge must be a table.");
        }

        private static void ClearManagedComments(TomlNode node)
        {
            if (!string.IsNullOrEmpty(node.Comment))
                node.Comment = node.Comment.Replace("BEGIN UNITY AGENT BRIDGE MANAGED", string.Empty)
                    .Replace("END UNITY AGENT BRIDGE MANAGED", string.Empty).Trim();
            foreach (var child in node.Children) ClearManagedComments(child);
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
