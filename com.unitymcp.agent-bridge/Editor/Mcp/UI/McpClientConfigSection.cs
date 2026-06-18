using System;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpClientConfigSection
    {
        private readonly string _fallbackTooltip;
        private ClientConfigTarget _selectedClient = ClientConfigTarget.Codex;
        private string _lastMessage = string.Empty;
        private MessageType _lastMessageType = MessageType.None;
        private bool _showPreview;

        public McpClientConfigSection(string fallbackTooltip)
        {
            _fallbackTooltip = fallbackTooltip ?? string.Empty;
        }

        internal bool IsCodexSelected => _selectedClient == ClientConfigTarget.Codex;

        public void Draw(IMcpClientConfigWriter codexWriter, IMcpClientConfigWriter claudeWriter, McpEditorSettings settings)
        {
            if (Event.current == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Step 2: Apply MCP Client Config", EditorStyles.boldLabel);

                var tabs = new[] { "Codex", "Claude Code" };
                _selectedClient = GUILayout.Toolbar(_selectedClient == ClientConfigTarget.ClaudeCode ? 1 : 0, tabs) == 1
                    ? ClientConfigTarget.ClaudeCode
                    : ClientConfigTarget.Codex;

                var activeWriter = _selectedClient == ClientConfigTarget.ClaudeCode ? claudeWriter : codexWriter;
                var scopeLabel = _selectedClient == ClientConfigTarget.ClaudeCode
                    ? "Project Scope (.mcp.json)"
                    : "Project Scope (.codex/config.toml)";

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Scope", scopeLabel);
                if (_selectedClient == ClientConfigTarget.Codex)
                {
                    EditorGUILayout.HelpBox("Direct MCP launcher plus project-local ToolsRoot is the recommended path. Start-Codex-With-UnityMcp.cmd has been removed from the supported workflow.", MessageType.Info);
                }
                EditorGUILayout.Space(4f);

                var preview = activeWriter != null ? activeWriter.Preview(settings) : string.Empty;
                _showPreview = EditorGUILayout.Foldout(_showPreview, "Advanced Config Preview", true);
                if (_showPreview)
                {
                    EditorGUILayout.TextArea(preview ?? string.Empty, GUILayout.MinHeight(120f));
                }

                if (_lastMessageType != MessageType.None && !string.IsNullOrEmpty(_lastMessage))
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(_lastMessage, _lastMessageType);
                }

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(90f)))
                    {
                        Apply(activeWriter, settings);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(90f)))
                    {
                        Remove(activeWriter);
                    }
                }

                if (activeWriter == null)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(_fallbackTooltip, MessageType.Info);
                }
            }
        }

        private void Apply(IMcpClientConfigWriter writer, McpEditorSettings settings)
        {
            if (writer == null)
            {
                SetMessage(_fallbackTooltip, MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm MCP Config Apply", "Apply the current project MCP configuration?", "Apply", "Cancel"))
            {
                return;
            }

            var result = writer.Apply(settings);
            HandleResult(result, "Configuration applied.");
        }

        private void Remove(IMcpClientConfigWriter writer)
        {
            if (writer == null)
            {
                SetMessage(_fallbackTooltip, MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm MCP Config Removal", "Remove the managed MCP configuration block from the current project?", "Remove", "Cancel"))
            {
                return;
            }

            var result = writer.Remove();
            HandleResult(result, "Managed configuration removed.");
        }

        private void HandleResult(ManagedBlockApplyResult result, string successMessage)
        {
            if (result == null)
            {
                SetMessage("Operation returned no result.", MessageType.Error);
                return;
            }

            if (result.Applied)
            {
                var message = string.IsNullOrEmpty(result.TargetPath)
                    ? successMessage
                    : successMessage + Environment.NewLine + result.TargetPath;

                if (!string.IsNullOrEmpty(result.BackupPath))
                {
                    message += Environment.NewLine + "Backup: " + result.BackupPath;
                }

                SetMessage(message, MessageType.Info);
                return;
            }

            var failure = string.IsNullOrEmpty(result.Reason) ? "Operation failed." : result.Reason;
            if (!string.IsNullOrEmpty(result.TargetPath))
            {
                failure += Environment.NewLine + result.TargetPath;
            }

            if (!string.IsNullOrEmpty(result.BackupPath))
            {
                failure += Environment.NewLine + "Backup: " + result.BackupPath;
            }

            SetMessage(failure, MessageType.Warning);
        }

        private void SetMessage(string message, MessageType type)
        {
            _lastMessage = message ?? string.Empty;
            _lastMessageType = type;
        }

        private enum ClientConfigTarget
        {
            Codex,
            ClaudeCode,
        }
    }
}
