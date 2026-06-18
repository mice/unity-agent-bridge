using System.IO;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    public static class AgentBridgeLocalPreferences
    {
        private const string BridgeEnabledKey = "UnityMcp.AgentBridge.Mcp.BridgeEnabled";

        public static bool BridgeEnabled
        {
            get => PlayerPrefs.GetInt(GetProjectScopedKey(BridgeEnabledKey), 1) != 0;
            set => SetBool(GetProjectScopedKey(BridgeEnabledKey), value);
        }

        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static string GetProjectScopedKey(string baseKey)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return baseKey;
            }

            var normalized = Path.GetFullPath(projectRoot).Replace('\\', '/').ToLowerInvariant();
            unchecked
            {
                uint hash = 2166136261;
                for (var i = 0; i < normalized.Length; i++)
                {
                    hash ^= normalized[i];
                    hash *= 16777619;
                }

                return $"{baseKey}.{hash:x8}";
            }
        }
    }
}
