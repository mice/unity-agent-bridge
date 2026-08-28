using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    internal sealed class UnityMcpPluginSection
    {
        private readonly Func<IReadOnlyList<UnityMcpInstalledPlugin>> _discover;
        private readonly Action<string, bool> _setEnabled;
        private readonly Action _refreshRuntime;
        private IReadOnlyList<UnityMcpInstalledPlugin> _plugins = Array.Empty<UnityMcpInstalledPlugin>();

        public UnityMcpPluginSection(
            Func<IReadOnlyList<UnityMcpInstalledPlugin>> discover,
            Action<string, bool> setEnabled,
            Action refreshRuntime)
        {
            _discover = discover ?? throw new ArgumentNullException(nameof(discover));
            _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
            _refreshRuntime = refreshRuntime ?? throw new ArgumentNullException(nameof(refreshRuntime));
        }

        internal IReadOnlyList<UnityMcpInstalledPlugin> Plugins => _plugins;

        public void Refresh()
        {
            _plugins = (_discover() ?? Array.Empty<UnityMcpInstalledPlugin>())
                .OrderBy(item => item?.DisplayName ?? item?.PluginId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void SetEnabled(string pluginId, bool enabled)
        {
            _setEnabled(pluginId, enabled);
            Refresh();
        }

        public void RefreshRuntime()
        {
            _refreshRuntime();
            Refresh();
        }

        public void Draw()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Installed UnityMCP Plugins", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Refresh", "Reconcile Unity-resolved plugin packages and rebuild the command catalog."), GUILayout.Width(80f)))
                    {
                        RefreshRuntime();
                    }
                }

                if (_plugins.Count == 0)
                {
                    EditorGUILayout.LabelField("No external UnityMCP plugins installed.", EditorStyles.wordWrappedLabel);
                    return;
                }

                foreach (var plugin in _plugins)
                {
                    DrawPlugin(plugin);
                }
            }
        }

        internal static string GetStateSummary(UnityMcpInstalledPlugin plugin)
        {
            if (plugin == null)
            {
                return "Installed: No | Enabled: No | Ready: No | Exposed: No";
            }

            return $"Installed: {YesNo(plugin.Installed)} | Enabled: {YesNo(plugin.Enabled)} | Ready: {YesNo(plugin.Ready)} | Exposed: {YesNo(plugin.Exposed)}";
        }

        private void DrawPlugin(UnityMcpInstalledPlugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            EditorGUILayout.Space(5f);
            using (new EditorGUILayout.HorizontalScope())
            {
                var displayName = string.IsNullOrWhiteSpace(plugin.DisplayName) ? plugin.PluginId : plugin.DisplayName;
                EditorGUILayout.LabelField($"{displayName}  {plugin.Version}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(plugin.PluginId)))
                {
                    var label = plugin.Enabled ? "Disable" : "Enable";
                    if (GUILayout.Button(label, GUILayout.Width(72f)))
                    {
                        SetEnabled(plugin.PluginId, !plugin.Enabled);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.LabelField(plugin.PluginId ?? string.Empty, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(GetStateSummary(plugin), EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrWhiteSpace(plugin.DiagnosticCode) || !string.IsNullOrWhiteSpace(plugin.DiagnosticMessage))
            {
                var diagnostic = string.IsNullOrWhiteSpace(plugin.DiagnosticCode)
                    ? plugin.DiagnosticMessage
                    : $"{plugin.DiagnosticCode}: {plugin.DiagnosticMessage}";
                EditorGUILayout.HelpBox(diagnostic, plugin.Ready ? MessageType.Info : MessageType.Warning);
            }
        }

        private static string YesNo(bool value)
        {
            return value ? "Yes" : "No";
        }
    }
}
