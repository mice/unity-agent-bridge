using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpCommandCatalogWindow : EditorWindow
    {
        internal const string WindowTitle = "Unity MCP Command List";

        private readonly List<ToolDescriptor> _descriptors = new List<ToolDescriptor>();
        private Vector2 _scrollPosition;

        internal static McpCommandCatalogWindow ShowWindow(IReadOnlyList<ToolDescriptor> descriptors)
        {
            var window = GetWindow<McpCommandCatalogWindow>(true, WindowTitle, true);
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(720f, 480f);
            window.SetDescriptors(descriptors);
            window.Show();
            return window;
        }

        internal void SetDescriptors(IReadOnlyList<ToolDescriptor> descriptors)
        {
            _descriptors.Clear();
            if (descriptors != null)
            {
                for (var index = 0; index < descriptors.Count; index++)
                {
                    var descriptor = descriptors[index];
                    if (descriptor != null)
                    {
                        _descriptors.Add(descriptor);
                    }
                }
            }

            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This list is generated from the governed Unity tool descriptors used by runtime mode blocking.",
                MessageType.Info);
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Available Commands", _descriptors.Count.ToString());
            EditorGUILayout.Space(6f);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            if (_descriptors.Count == 0)
            {
                EditorGUILayout.HelpBox("No Unity MCP commands are currently registered.", MessageType.Info);
            }
            else
            {
                for (var index = 0; index < _descriptors.Count; index++)
                {
                    DrawDescriptor(_descriptors[index]);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawDescriptor(ToolDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(descriptor.Name ?? string.Empty, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Purpose", descriptor.Description ?? string.Empty, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Modes", ToolDescriptorDisplay.GetAllowedModeSummary(descriptor.AllowedModes));
                EditorGUILayout.LabelField("Side Effect", ToolDescriptorDisplay.GetSideEffectLabel(descriptor.SideEffect));
                EditorGUILayout.LabelField(
                    "Domain Reload Risk",
                    descriptor.MayTriggerDomainReload ? "May reload domain" : "No domain reload expected");
            }
        }
    }
}
