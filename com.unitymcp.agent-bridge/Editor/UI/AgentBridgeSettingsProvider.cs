using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    public static class AgentBridgeSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Agent Bridge", SettingsScope.Project)
            {
                label = "Agent Bridge",
                guiHandler = _ =>
                {
                    var result = AgentBridgeSettingsLoader.Load();
                    var settings = result.Settings;
                    if (settings == null)
                    {
                        EditorGUILayout.HelpBox(result.WarningMessage ?? "Settings could not be loaded.", MessageType.Warning);
                        if (GUILayout.Button("Create Settings Asset"))
                        {
                            AgentBridgeSettingsLoader.CreateDefaultAsset();
                            AssetDatabase.Refresh();
                        }

                        return;
                    }

                    EditorGUI.BeginChangeCheck();
                    settings.enabled = EditorGUILayout.Toggle("Enabled", settings.enabled);
                    settings.roslynExecutionEnabled = EditorGUILayout.Toggle("Enable Roslyn Execution", settings.roslynExecutionEnabled);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssets();
                        AgentBridgeBootstrap.Reconfigure();
                    }

                    EditorGUILayout.HelpBox(
                        "Roslyn execution is trusted local automation. Submitted code runs inside the Unity Editor process, can mutate project state, and MVP does not guarantee interruption of dead loops or blocking calls.",
                        MessageType.Warning);

                    if (GUILayout.Button("Ping Reconfigure"))
                    {
                        AgentBridgeBootstrap.Reconfigure();
                    }
                }
            };
        }
    }
}
