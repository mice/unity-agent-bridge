using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityMcp.AgentBridge
{
    public static class UnityMcpExternalPluginLifecycle
    {
        public static bool IsEnabled(AgentBridgeSettings settings, string pluginId)
        {
            if (settings == null || string.IsNullOrWhiteSpace(pluginId))
            {
                return false;
            }

            return (settings.externalPluginStates ?? new List<UnityMcpExternalPluginState>())
                .Where(item => item != null && string.Equals(item.pluginId, pluginId, StringComparison.Ordinal))
                .Select(item => item.enabled)
                .LastOrDefault();
        }

        public static bool SetEnabled(AgentBridgeSettings settings, string pluginId, bool enabled)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("A stable plugin ID is required.", nameof(pluginId));
            }

            settings.externalPluginStates ??= new List<UnityMcpExternalPluginState>();
            var existing = settings.externalPluginStates
                .Where(item => item != null && string.Equals(item.pluginId, pluginId, StringComparison.Ordinal))
                .ToArray();
            var changed = existing.Length != 1 || existing[0].enabled != enabled;
            settings.externalPluginStates.RemoveAll(item => item != null && string.Equals(item.pluginId, pluginId, StringComparison.Ordinal));
            settings.externalPluginStates.Add(new UnityMcpExternalPluginState
            {
                pluginId = pluginId,
                enabled = enabled
            });
            return changed;
        }

        public static AgentBridgeSettings PersistEnabled(string pluginId, bool enabled)
        {
            var load = AgentBridgeSettingsLoader.Load();
            var settings = load.Settings;
            if (settings == null || string.IsNullOrWhiteSpace(load.AssetPath))
            {
                settings = AgentBridgeSettingsLoader.CreateDefaultAsset();
            }

            if (SetEnabled(settings, pluginId, enabled))
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            AgentBridgeBootstrap.Reconfigure();
            return settings;
        }

        public static void Refresh()
        {
            AgentBridgeBootstrap.Reconfigure();
        }
    }
}
