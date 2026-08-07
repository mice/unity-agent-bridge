using System;
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
                    var roslynPolicies = new[] { "trusted", "query_only" };
                    var currentPolicyIndex = Array.IndexOf(roslynPolicies, settings.roslynExecutionDefaultPolicy);
                    if (currentPolicyIndex < 0)
                    {
                        currentPolicyIndex = 0;
                    }

                    var selectedPolicyIndex = EditorGUILayout.Popup("Roslyn Default Policy", currentPolicyIndex, roslynPolicies);
                    settings.roslynExecutionDefaultPolicy = roslynPolicies[selectedPolicyIndex];
                    settings.monoBehaviourFindReference2ProviderEnabled = EditorGUILayout.Toggle(
                        "Enable FindReference2 Provider",
                        settings.monoBehaviourFindReference2ProviderEnabled);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssets();
                        AgentBridgeBootstrap.Reconfigure();
                    }

                    EditorGUILayout.HelpBox(
                        "Roslyn execution runs submitted code inside the Unity Editor process. Trusted mode can mutate project state. Query-only mode rejects known side-effect and escape-hatch APIs but is not a security sandbox and does not guarantee interruption of dead loops or blocking calls.",
                        MessageType.Warning);

                    EditorGUILayout.HelpBox(
                        "FindReference2 provider integration is optional local automation for MonoBehaviour Semantics. It is disabled by default and only probes FindReference2 through reflection after explicit enablement.",
                        MessageType.Info);

                    if (GUILayout.Button("Ping Reconfigure"))
                    {
                        AgentBridgeBootstrap.Reconfigure();
                    }
                }
            };
        }
    }
}
