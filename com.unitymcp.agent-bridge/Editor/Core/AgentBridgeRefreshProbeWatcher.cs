using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class AgentBridgeRefreshProbeWatcher
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        static AgentBridgeRefreshProbeWatcher()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var triggerPath = Path.Combine(projectRoot, "Temp", "AgentBridge", "refresh.trigger");
            if (!File.Exists(triggerPath))
            {
                return;
            }

            File.Delete(triggerPath);

            var assetPath = Path.Combine(projectRoot, "Assets", "AgentBridgeRefreshProbe.txt");
            File.WriteAllText(assetPath, "refresh-probe " + DateTime.UtcNow.ToString("O"), Utf8NoBom);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("AgentBridgeRefreshProbeWatcher:DONE");
        }
    }
}
