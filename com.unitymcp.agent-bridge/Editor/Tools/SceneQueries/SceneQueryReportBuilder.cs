using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    internal static class SceneQueryReportBuilder
    {
        public static JObject CreateEditorStateReport(EditorStateSnapshot snapshot)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = SceneQueryContract.EditorStateContractVersion,
                ["generatedAt"] = SceneQueryContract.CreateGeneratedAtUtc(),
                ["editorState"] = JToken.FromObject(snapshot)
            };
        }

        public static JObject CreateOpenSceneReport(UnityOpenSceneArgs args, OpenSceneMetrics metrics)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = SceneQueryContract.OpenSceneContractVersion,
                ["generatedAt"] = SceneQueryContract.CreateGeneratedAtUtc(),
                ["request"] = new JObject
                {
                    ["scenePath"] = ToToken(args != null ? args.scenePath : null),
                    ["mode"] = ToToken(args != null ? args.mode : null),
                    ["setActive"] = args != null && args.setActive,
                    ["saveModifiedScenes"] = args != null && args.saveModifiedScenes
                },
                ["result"] = JToken.FromObject(metrics)
            };
        }

        public static JObject CreateOpenSceneFailureReport(UnityOpenSceneArgs args, ToolResult result, object metrics)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = SceneQueryContract.OpenSceneContractVersion,
                ["generatedAt"] = SceneQueryContract.CreateGeneratedAtUtc(),
                ["request"] = new JObject
                {
                    ["scenePath"] = ToToken(args != null ? args.scenePath : null),
                    ["mode"] = ToToken(args != null ? args.mode : null),
                    ["setActive"] = args == null || args.setActive,
                    ["saveModifiedScenes"] = args != null && args.saveModifiedScenes
                },
                ["status"] = result != null ? ToToken(result.status) : JValue.CreateNull(),
                ["success"] = result != null && result.success,
                ["summary"] = result != null ? ToToken(result.summary) : JValue.CreateNull(),
                ["errors"] = result != null && result.errors != null ? JToken.FromObject(result.errors) : new JArray(),
                ["metrics"] = metrics != null ? JToken.FromObject(metrics) : JValue.CreateNull()
            };
        }

        private static JToken ToToken(string value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }
    }
}
