using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [CreateAssetMenu(fileName = "AgentBridgeSettings", menuName = "UnityMcp/Agent Bridge Settings")]
    public sealed class AgentBridgeSettings : ScriptableObject
    {
        public bool enabled = true;
        public bool roslynExecutionEnabled;
        public bool monoBehaviourFindReference2ProviderEnabled;
        public int pollIntervalMs = 200;
        public int maxPollIntervalMs = 2000;
        public int compileBackoffMs = 1000;
        public int maxConcurrent = 1;
        public string tempRoot = "Temp/AgentBridge";
        public string logRoot = "Library/AgentBridge/logs";
        public string metricsPath = "Library/AgentBridge/metrics.json";
        public string logLevel = "info";
        public int mainThreadWarnAfterMs = 5000;
        public int maxToolDurationMs = 300000;
        public int metricsRetentionDays = 7;
        public int logRetentionDays = 7;
        public string pluginCatalogPath = "Library/AgentBridge/plugin-catalog.json";
        public List<AllowedStaticMethodEntry> allowedStaticMethods = new List<AllowedStaticMethodEntry>();
        public List<UnityMcpPluginRegistration> pluginRegistrations = new List<UnityMcpPluginRegistration>();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!AssetDatabase.Contains(this))
            {
                return;
            }

            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= ReconfigureAfterValidation;
                EditorApplication.delayCall += ReconfigureAfterValidation;
            }
        }

        private static void ReconfigureAfterValidation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            AgentBridgeBootstrap.Reconfigure();
        }
#endif
    }

    [Serializable]
    public sealed class AllowedStaticMethodEntry
    {
        public string id;
        public string typeName;
        public string methodName;
        public string argsSchemaPath;
        public string parameterDtoTypeName;
        public bool requiresMainThread = true;
        public int maxDurationMs = 60000;
        public string sideEffects = "read";
        public bool allowAssetDatabaseRefresh;
        public bool allowAssetDatabaseSaveAssets;
        public string doneLogPattern;
    }

    public enum UnityMcpPluginRegistrationKind
    {
        AsmdefAssembly = 0,
        ManagedDll = 1
    }

    [Serializable]
    public sealed class UnityMcpPluginRegistration
    {
        public bool enabled = true;
        public UnityMcpPluginRegistrationKind kind = UnityMcpPluginRegistrationKind.AsmdefAssembly;
        public string assemblyName;
        public string dllPath;
        public string providerTypeName;
    }
}
