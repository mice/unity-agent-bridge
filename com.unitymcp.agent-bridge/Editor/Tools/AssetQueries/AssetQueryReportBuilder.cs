using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    internal static class AssetQueryReportBuilder
    {
        public static string Serialize(JObject payload)
        {
            return payload?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}";
        }

        public static JObject CreateAssetSearchReport(UnityAssetDatabaseSearchArgs args, AssetDatabaseSearchMetrics metrics, JArray assets)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = AssetQueryContract.AssetSearchContractVersion,
                ["generatedAt"] = AssetQueryContract.CreateGeneratedAtUtc(),
                ["request"] = JObject.FromObject(new
                {
                    query = args.query,
                    folders = args.folders,
                    offset = metrics.offset,
                    limit = metrics.limit,
                    includeDetails = args.includeDetails
                }),
                ["result"] = new JObject
                {
                    ["totalCount"] = metrics.totalCount,
                    ["returnedCount"] = metrics.returnedCount,
                    ["offset"] = metrics.offset,
                    ["limit"] = metrics.limit,
                    ["truncated"] = metrics.truncated,
                    ["nextOffset"] = metrics.nextOffset.HasValue ? JToken.FromObject(metrics.nextOffset.Value) : JValue.CreateNull()
                },
                ["assets"] = assets
            };
        }

        public static JObject CreateSelectionReport(int selectionCount, int? activeIndex, JArray items)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = AssetQueryContract.SelectionInfoContractVersion,
                ["generatedAt"] = AssetQueryContract.CreateGeneratedAtUtc(),
                ["selection"] = new JObject
                {
                    ["selectionCount"] = selectionCount,
                    ["activeIndex"] = activeIndex.HasValue ? JToken.FromObject(activeIndex.Value) : JValue.CreateNull()
                },
                ["items"] = items
            };
        }

        public static JObject CreateSelectionDetail(UnityEngine.Object target, int index, string kind, string locator)
        {
            var assetPath = AssetDatabase.GetAssetPath(target);
            var gameObject = target as GameObject ?? (target as Component)?.gameObject;
            var scenePath = gameObject != null && gameObject.scene.IsValid() ? gameObject.scene.path : null;
            var prefabAssetPath = gameObject != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) : null;
            var globalObjectId = target != null ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : null;

            return new JObject
            {
                ["index"] = index,
                ["kind"] = kind,
                ["name"] = target != null ? target.name : string.Empty,
                ["locator"] = ToToken(locator),
                ["type"] = target != null ? target.GetType().FullName : string.Empty,
                ["path"] = ToToken(gameObject != null ? GameObjectLocatorFormatter.GetHierarchyPath(gameObject) : (!string.IsNullOrWhiteSpace(assetPath) ? assetPath.Replace('\\', '/') : null)),
                ["assetPath"] = ToToken(!string.IsNullOrWhiteSpace(assetPath) ? assetPath.Replace('\\', '/') : null),
                ["hierarchyPath"] = ToToken(gameObject != null ? GameObjectLocatorFormatter.GetHierarchyPath(gameObject) : null),
                ["scenePath"] = ToToken(!string.IsNullOrWhiteSpace(scenePath) ? scenePath.Replace('\\', '/') : null),
                ["instanceId"] = target != null ? JToken.FromObject(target.GetInstanceID()) : JValue.CreateNull(),
                ["guid"] = ToToken(!string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.AssetPathToGUID(assetPath) : null),
                ["globalObjectId"] = ToToken(string.IsNullOrWhiteSpace(globalObjectId) ? null : globalObjectId),
                ["isPersistent"] = target != null ? JToken.FromObject(EditorUtility.IsPersistent(target)) : JValue.CreateNull(),
                ["isPrefabInstance"] = gameObject != null ? JToken.FromObject(PrefabUtility.IsPartOfPrefabInstance(gameObject)) : JValue.CreateNull(),
                ["prefabAssetPath"] = ToToken(!string.IsNullOrWhiteSpace(prefabAssetPath) ? prefabAssetPath.Replace('\\', '/') : null)
            };
        }

        public static JObject CreateGameObjectComponentReport(string mode, JObject target, JArray components)
        {
            return new JObject
            {
                ["schemaVersion"] = JsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = AssetQueryContract.GameObjectComponentInfoContractVersion,
                ["generatedAt"] = AssetQueryContract.CreateGeneratedAtUtc(),
                ["mode"] = mode,
                ["target"] = target,
                ["components"] = components
            };
        }

        public static JObject CreateGameObjectTargetDetail(GameObject gameObject)
        {
            var globalObjectId = gameObject != null ? GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString() : null;
            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            return new JObject
            {
                ["name"] = gameObject.name,
                ["locator"] = GameObjectLocatorFormatter.GetLocator(gameObject),
                ["path"] = GameObjectLocatorFormatter.GetHierarchyPath(gameObject),
                ["scenePath"] = ToToken(gameObject.scene.IsValid() ? gameObject.scene.path.Replace('\\', '/') : null),
                ["instanceId"] = gameObject.GetInstanceID(),
                ["globalObjectId"] = ToToken(string.IsNullOrWhiteSpace(globalObjectId) ? null : globalObjectId),
                ["prefabAssetPath"] = ToToken(!string.IsNullOrWhiteSpace(prefabAssetPath) ? prefabAssetPath.Replace('\\', '/') : null)
            };
        }

        public static JObject CreateComponentDetail(int index, string name, string type, string assemblyName, string scriptGuid, string scriptPath, JArray properties, bool truncated)
        {
            return new JObject
            {
                ["index"] = index,
                ["name"] = name,
                ["type"] = ToToken(type),
                ["assemblyName"] = ToToken(assemblyName),
                ["scriptGuid"] = ToToken(scriptGuid),
                ["scriptPath"] = ToToken(scriptPath),
                ["properties"] = properties,
                ["truncated"] = truncated
            };
        }

        private static JToken ToToken(string value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }
    }
}
