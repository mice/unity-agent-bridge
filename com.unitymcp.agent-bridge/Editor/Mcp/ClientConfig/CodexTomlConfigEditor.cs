using System;
using System.IO;
using System.Text;
using Tommy;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class CodexTomlConfigEditor
    {
        private readonly ManagedBlockTextEditor _textEditor;

        public CodexTomlConfigEditor()
            : this(new ManagedBlockTextEditor())
        {
        }

        internal CodexTomlConfigEditor(ManagedBlockTextEditor textEditor)
        {
            _textEditor = textEditor ?? throw new ArgumentNullException(nameof(textEditor));
        }

        public ManagedBlockApplyResult Apply(string targetPath, string executableCommand)
        {
            return Apply(targetPath, executableCommand, string.Empty);
        }

        public ManagedBlockApplyResult Apply(string targetPath, string executableCommand, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("targetPath must not be empty.", nameof(targetPath));
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var original = File.Exists(targetPath) ? File.ReadAllText(targetPath) : string.Empty;
            var updated = ApplyTomlManagedContent(original, executableCommand, projectRoot);
            if (!CodexProjectConfigWriter.ValidateManagedTomlResult(updated))
            {
                return new ManagedBlockApplyResult
                {
                    Applied = false,
                    TargetPath = targetPath,
                    Reason = "format_validation_failed",
                };
            }

            File.WriteAllText(targetPath, updated);
            return new ManagedBlockApplyResult
            {
                Applied = true,
                TargetPath = targetPath,
            };
        }

        public ManagedBlockApplyResult Remove(string targetPath)
        {
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
            File.WriteAllText(targetPath, updated);
            return new ManagedBlockApplyResult
            {
                Applied = true,
                TargetPath = targetPath,
            };
        }

        private string ApplyTomlManagedContent(string originalText, string executableCommand, string projectRoot)
        {
            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return _textEditor.Apply(string.Empty, CodexProjectConfigWriter.BuildManagedBlockBody(executableCommand, projectRoot, string.Empty));
            }

            if (TryApplyWithToml(normalized, executableCommand, projectRoot, out var updated))
            {
                return updated;
            }

            var preservedChildSections = CodexProjectConfigWriter.ExtractUnityAgentBridgeChildSections(normalized);
            var mergedBlock = CodexProjectConfigWriter.BuildManagedBlockBody(executableCommand, projectRoot, preservedChildSections);
            return CodexProjectConfigWriter.ApplyManagedContent(normalized, mergedBlock, _textEditor);
        }

        private bool TryApplyWithToml(string originalText, string executableCommand, string projectRoot, out string updatedText)
        {
            updatedText = null;

            if (!TryParseTomlExcludingManagedBlock(originalText, out var root, out var parseRemainder))
            {
                return false;
            }

            var childTableText = CodexProjectConfigWriter.ExtractUnityAgentBridgeChildSections(parseRemainder);
            TomlTable childRoot = null;
            if (!string.IsNullOrWhiteSpace(childTableText))
            {
                if (!TryParseTomlExcludingManagedBlock(childTableText, out childRoot, out _))
                {
                    return false;
                }
            }

            EnsureTable(root, "mcp_servers");
            var mcpServers = root["mcp_servers"].AsTable;
            mcpServers.Delete("unity_agent_bridge");

            var bridgeTable = new TomlTable();
            bridgeTable["command"] = executableCommand;
            bridgeTable["args"] = new TomlArray
            {
                "mcp-server",
            };
            bridgeTable["cwd"] = ".";
            bridgeTable["startup_timeout_sec"] = 20L;
            bridgeTable["tool_timeout_sec"] = 300L;
            bridgeTable["required"] = false;
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                var envTable = new TomlTable();
                envTable["UNITY_AGENT_BRIDGE_PROJECT_PATH"] = Path.GetFullPath(projectRoot.Trim());
                bridgeTable["env"] = envTable;
            }

            if (childRoot != null
                && childRoot.TryGetNode("mcp_servers", out var childMcpServersNode)
                && childMcpServersNode is TomlTable childMcpServers
                && childMcpServers.TryGetNode("unity_agent_bridge", out var childBridgeNode)
                && childBridgeNode is TomlTable childBridgeTable)
            {
                foreach (var key in childBridgeTable.Keys)
                {
                    if (bridgeTable.HasKey(key))
                    {
                        continue;
                    }

                    bridgeTable[key] = childBridgeTable[key];
                }
            }

            mcpServers["unity_agent_bridge"] = bridgeTable;

            var serialized = SerializeToml(root).Trim();
            updatedText = _textEditor.Apply(string.Empty, serialized);
            return true;
        }

        private static bool TryParseTomlExcludingManagedBlock(string text, out TomlTable root, out string unmanagedOnly)
        {
            unmanagedOnly = new ManagedBlockTextEditor().Remove(text ?? string.Empty);
            root = null;
            try
            {
                using var reader = new StringReader(unmanagedOnly);
                root = TOML.Parse(reader);
                return root != null;
            }
            catch
            {
                root = null;
                return false;
            }
        }

        private static void EnsureTable(TomlTable root, string key)
        {
            if (root.HasKey(key) && root[key] is TomlTable)
            {
                return;
            }

            root[key] = new TomlTable();
        }

        private static string SerializeToml(TomlTable root)
        {
            using var writer = new StringWriter(new StringBuilder());
            root.WriteTo(writer);
            return NormalizeLineEndings(writer.ToString());
        }

        private static string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
