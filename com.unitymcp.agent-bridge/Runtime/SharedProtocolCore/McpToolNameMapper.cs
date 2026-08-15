using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    public static class McpToolNameMapper
    {
        private static readonly IReadOnlyDictionary<string, string> ExistingBridgeToolNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["unity.ping"] = "unity_editor_ping",
                ["unity.project.get_info"] = "unity_project_get_info",
                ["unity.compile"] = "unity_project_compile",
                ["unity.get_console"] = "unity_console_get",
                ["unity.get_editor_state"] = "unity_editor_get_state",
                ["unity.open_scene"] = "unity_scene_open",
                ["unity.run_static_method"] = "unity_static_method_run",
                ["unity.run_diagnostic"] = "unity_diagnostic_run",
                ["unity.assetdatabase_search"] = "unity_asset_database_search",
                ["unity.get_hierarchy"] = "unity_hierarchy_get",
                ["unity.get_gameobject_component_info"] = "unity_gameobject_component_get_info",
                ["unity.get_selection_info"] = "unity_selection_get_info",
                ["unity.read_report"] = "unity_report_read",
                ["unity.run_editmode_tests"] = "unity_tests_run_edit_mode",
                ["unity.run_playmode_tests"] = "unity_tests_run_play_mode",
                ["unity.agent_bridge_self_test"] = "unity_agent_bridge_run_self_test",
                ["unity.execute_csharp"] = "unity_csharp_execute",
                ["unity.mono.find_script_guid_usages"] = "unity_monobehaviour_find_script_guid_usages",
                ["unity.lua.lint"] = "unity_lua_lint",
                ["unity.lua.compile"] = "unity_lua_compile",
                ["unity.aig.scan_fbx_import_issues"] = "unity_fbx_scan_import_issues"
            };

        private static readonly ISet<string> SupportedVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "get", "list", "find", "search", "read", "open", "run", "compile", "execute", "lint", "scan", "ping"
        };

        public static string ToCanonicalMcpName(string bridgeToolName)
        {
            if (string.IsNullOrWhiteSpace(bridgeToolName))
            {
                throw new ArgumentException("Bridge tool name is required.", nameof(bridgeToolName));
            }

            if (ExistingBridgeToolNames.TryGetValue(bridgeToolName, out var existingName))
            {
                return existingName;
            }

            var segments = bridgeToolName.Split('.');
            if (segments.Length < 3 || !string.Equals(segments[0], "unity", StringComparison.Ordinal) || !SupportedVerbs.Contains(segments[2]))
            {
                throw new ArgumentException(
                    "Future bridge tools must use unity.<domain>.<verb>[.<detail>] with an approved verb.",
                    nameof(bridgeToolName));
            }

            var canonicalName = bridgeToolName.Replace('.', '_');
            if (!IsCanonicalMcpName(canonicalName))
            {
                throw new ArgumentException("Canonical MCP tool names must use lower snake case and cannot contain double underscores.", nameof(bridgeToolName));
            }

            return canonicalName;
        }

        public static bool TryToCanonicalMcpName(string bridgeToolName, out string canonicalName)
        {
            try
            {
                canonicalName = ToCanonicalMcpName(bridgeToolName);
                return true;
            }
            catch (ArgumentException)
            {
                canonicalName = string.Empty;
                return false;
            }
        }

        public static bool IsCanonicalMcpName(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName) || toolName.Contains("__") || !toolName.StartsWith("unity_", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var character in toolName)
            {
                if ((character < 'a' || character > 'z') && character != '_' && (character < '0' || character > '9'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
