using System;
using Newtonsoft.Json;

namespace UnityMcp.AgentBridge
{
    internal static class SceneQueryJson
    {
        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out ToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            var trimmed = rawArgsJson.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson);
                if (args == null)
                {
                    args = new TArgs();
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            if (value == null)
            {
                return "{}";
            }

            return JsonConvert.SerializeObject(value);
        }
    }
}
