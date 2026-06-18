using System;
using Newtonsoft.Json;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    internal static class EditorBasicsJson
    {
        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            var trimmed = rawArgsJson.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson) ?? new TArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            return value == null ? "{}" : JsonConvert.SerializeObject(value, Formatting.None);
        }
    }
}
