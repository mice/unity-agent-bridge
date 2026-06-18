using System;
using UnityMcp.Plugin;
using Newtonsoft.Json;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class AssetQueryJson
    {
        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson) || !rawArgsJson.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson) ?? new TArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            return value == null ? "{}" : JsonConvert.SerializeObject(value);
        }
    }
}
