using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityMcpPluginCatalog
    {
        public const int CurrentVersion = 1;
        public const int MaxTools = 64;
        public const int MaxUtf8Bytes = 65536;

        public int version = CurrentVersion;
        public List<UnityMcpPluginCatalogTool> tools = new List<UnityMcpPluginCatalogTool>();
    }

    [Serializable]
    public sealed class UnityMcpPluginCatalogTool
    {
        public string pluginId;
        public string pluginVersion;
        public string assemblyName;
        public string bridgeTool;
        public string mcpName;
        public string title;
        public string description;
        public int defaultTimeoutMs;
        public string allowedRuntimeModes;
        public string sideEffect;
        public bool mayTriggerDomainReload;
        public string inputSchemaJson;
    }

    internal static class UnityMcpPluginCatalogV1Validator
    {
        private const int MaxSchemaUtf8Bytes = 1048576;
        private static readonly Regex PluginIdPattern = new Regex("^[a-z0-9][a-z0-9-]*(?:\\.[a-z0-9][a-z0-9-]*)+$", RegexOptions.CultureInvariant);
        private static readonly Regex PluginVersionPattern = new Regex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);
        private static readonly Regex AssemblyNamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex BridgeToolPattern = new Regex("^unity(?:\\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant);
        private static readonly Regex McpNamePattern = new Regex("^unity_[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        public static void ValidateTool(UnityMcpPluginCatalogTool tool)
        {
            if (tool == null ||
                !IsMatch(tool.pluginId, 3, 255, PluginIdPattern) ||
                !IsMatch(tool.pluginVersion, 5, 64, PluginVersionPattern) ||
                !IsMatch(tool.assemblyName, 1, 255, AssemblyNamePattern) ||
                !IsMatch(tool.bridgeTool, 7, 255, BridgeToolPattern) ||
                !IsMatch(tool.mcpName, 7, 255, McpNamePattern) ||
                !IsBounded(tool.title, 1, 256) ||
                !IsBounded(tool.description, 1, 4096) ||
                tool.defaultTimeoutMs <= 0 ||
                !IsRuntimeMode(tool.allowedRuntimeModes) ||
                !IsSideEffect(tool.sideEffect) ||
                string.IsNullOrWhiteSpace(tool.inputSchemaJson) ||
                Encoding.UTF8.GetByteCount(tool.inputSchemaJson) > MaxSchemaUtf8Bytes ||
                JToken.Parse(tool.inputSchemaJson).Type != JTokenType.Object)
            {
                throw new ArgumentException("Plugin catalog tool violates the frozen Catalog V1 contract.", nameof(tool));
            }
        }

        private static bool IsMatch(string value, int minLength, int maxLength, Regex pattern)
        {
            return IsBounded(value, minLength, maxLength) && pattern.IsMatch(value);
        }

        private static bool IsBounded(string value, int minLength, int maxLength)
        {
            return value != null && value.Length >= minLength && value.Length <= maxLength;
        }

        private static bool IsRuntimeMode(string value)
        {
            return value == "Edit" || value == "Play" || value == "EditAndPlay";
        }

        private static bool IsSideEffect(string value)
        {
            return value == "None" || value == "ReadsProject" || value == "MutatesProject" || value == "RunsUserCode";
        }
    }
}
