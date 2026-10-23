using System;
using System.IO;

namespace UnityMcp.AgentBridge.Mcp
{
    // Used only when generating new managed configuration. Legacy builders remain readable.
    internal static class McpMultiProjectConfig
    {
        internal static string BuildJson(string command, string projectRoot, string workspaceRoot, bool launcher)
        {
            var args = launcher
                ? "[\"/d\", \"/s\", \"/c\", " + Quote(command) + "]"
                : "[\"mcp-server\", \"--routing-mode\", \"multi-project\"]";
            return "{\n      \"command\": " + Quote(launcher ? "cmd" : command) +
                   ",\n      \"args\": " + args +
                   ",\n      \"cwd\": " + Quote(Absolute(workspaceRoot)) +
                   ",\n      \"env\": {\n        \"UNITY_AGENT_BRIDGE_ROUTING_MODE\": \"multi-project\"," +
                   "\n        \"UNITY_AGENT_BRIDGE_WORKSPACE_PATH\": " + Quote(Absolute(workspaceRoot)) +
                   ",\n        \"UNITY_AGENT_BRIDGE_PROJECT_PATH\": " + Quote(Absolute(projectRoot)) +
                   "\n      }\n    }";
        }

        internal static string BuildToml(string command, string projectRoot, string workspaceRoot, bool launcher, bool grok = false)
        {
            var args = launcher
                ? "[\"/d\", \"/s\", \"/c\", " + Quote(command) + "]"
                : "[\"mcp-server\", \"--routing-mode\", \"multi-project\"]";
            return string.Join(Environment.NewLine, new[]
            {
                "[mcp_servers.unity_agent_bridge]",
                "command = " + Quote(launcher ? "cmd" : command),
                "args = " + args,
                "cwd = " + Quote(Absolute(workspaceRoot)),
                grok ? "enabled = true" : "required = false",
                grok ? "startup_timeout_sec = 30" : "startup_timeout_sec = 20",
                "tool_timeout_sec = 300",
                "",
                "[mcp_servers.unity_agent_bridge.env]",
                "UNITY_AGENT_BRIDGE_ROUTING_MODE = \"multi-project\"",
                "UNITY_AGENT_BRIDGE_WORKSPACE_PATH = " + Quote(Absolute(workspaceRoot)),
                "UNITY_AGENT_BRIDGE_PROJECT_PATH = " + Quote(Absolute(projectRoot)),
            });
        }

        private static string Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A project and connection workspace are required when generating configuration.");
            return Path.GetFullPath(path.Trim());
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }
    }
}
