using System;
using UnityMcp.Plugin;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class SceneQueryReportBuilder
    {
        public static JObject CreateHierarchyReport(UnityGetHierarchyArgs args, HierarchyMetrics metrics)
        {
            return new JObject
            {
                ["schemaVersion"] = UnityQueriesJsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = SceneQueryContract.HierarchyContractVersion,
                ["generatedAt"] = SceneQueryContract.CreateGeneratedAtUtc(),
                ["request"] = new JObject
                {
                    ["locator"] = ToToken(args != null ? args.locator : null),
                    ["maxDepth"] = metrics.maxDepth,
                    ["limit"] = metrics.limit,
                    ["includeComponents"] = args != null && args.includeComponents
                },
                ["target"] = metrics.target != null ? JToken.FromObject(metrics.target) : JValue.CreateNull(),
                ["result"] = JToken.FromObject(metrics),
                ["boundedCompleteness"] = new JObject
                {
                    ["completeWithinAppliedBounds"] = true,
                    ["truncated"] = metrics.truncated
                }
            };
        }

        public static JObject CreateHierarchyFailureReport(UnityGetHierarchyArgs args, UnityMcpToolResult result, object target, object metrics)
        {
            return new JObject
            {
                ["schemaVersion"] = UnityQueriesJsonUtil.CurrentSchemaVersion,
                ["payloadVersion"] = SceneQueryContract.HierarchyContractVersion,
                ["generatedAt"] = SceneQueryContract.CreateGeneratedAtUtc(),
                ["request"] = new JObject
                {
                    ["locator"] = ToToken(args != null ? args.locator : null),
                    ["maxDepth"] = args != null ? args.maxDepth : SceneQueryContract.DefaultHierarchyMaxDepth,
                    ["limit"] = args != null ? args.limit : SceneQueryContract.DefaultHierarchyLimit,
                    ["includeComponents"] = args != null && args.includeComponents
                },
                ["target"] = target != null ? JToken.FromObject(target) : JValue.CreateNull(),
                ["status"] = result != null ? ToToken(result.Status) : JValue.CreateNull(),
                ["success"] = result != null && result.Success,
                ["summary"] = result != null ? ToToken(result.Summary) : JValue.CreateNull(),
                ["errors"] = result != null && result.Errors != null ? JToken.FromObject(result.Errors) : new JArray(),
                ["metrics"] = metrics != null ? JToken.FromObject(metrics) : JValue.CreateNull()
            };
        }

        public static HierarchyTargetRecord CreateTargetRecord(HierarchyTargetResolution resolution)
        {
            if (resolution == null)
            {
                return null;
            }

            var record = new HierarchyTargetRecord();
            record.locator = resolution.locator;
            record.targetKind = resolution.targetKind;
            record.scenePath = resolution.scene.IsValid() && !string.IsNullOrWhiteSpace(resolution.scene.path)
                ? resolution.scene.path.Replace('\\', '/')
                : null;
            record.path = resolution.gameObject != null ? GameObjectLocatorFormatter.GetHierarchyPath(resolution.gameObject) : null;
            record.name = resolution.gameObject != null ? resolution.gameObject.name : resolution.scene.name;
            record.instanceId = resolution.gameObject != null ? (int?)resolution.gameObject.GetInstanceID() : null;
            return record;
        }

        public static HierarchyNodeRecord CreateNodeRecord(GameObject gameObject, HierarchyTargetResolution resolution, int nodeIndex, int? parentIndex, int depth, bool includeComponents)
        {
            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            HierarchyComponentSummaryRecord[] components = null;
            bool? componentsTruncated = null;
            var componentCount = 0;
            if (includeComponents)
            {
                components = CreateComponentSummaries(gameObject, out componentCount, out var truncated);
                componentsTruncated = truncated;
            }
            else
            {
                componentCount = gameObject.GetComponents<Component>().Length;
            }

            var record = new HierarchyNodeRecord();
            record.nodeIndex = nodeIndex;
            record.parentIndex = parentIndex;
            record.name = gameObject.name;
            record.locator = GetTargetScopedLocator(gameObject, resolution);
            record.path = GameObjectLocatorFormatter.GetHierarchyPath(gameObject);
            record.scenePath = gameObject.scene.IsValid() ? gameObject.scene.path.Replace('\\', '/') : null;
            record.instanceId = gameObject.GetInstanceID();
            record.depth = depth;
            record.siblingIndex = gameObject.transform.GetSiblingIndex();
            record.activeSelf = gameObject.activeSelf;
            record.activeInHierarchy = gameObject.activeInHierarchy;
            record.childCount = gameObject.transform.childCount;
            record.componentCount = componentCount;
            record.isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(gameObject);
            record.prefabAssetPath = string.IsNullOrWhiteSpace(prefabAssetPath) ? null : prefabAssetPath.Replace('\\', '/');
            record.components = components;
            record.componentsTruncated = componentsTruncated;
            record.hasMissingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0;
            return record;
        }

        private static string GetTargetScopedLocator(GameObject gameObject, HierarchyTargetResolution resolution)
        {
            if (gameObject == null)
            {
                return null;
            }

            if (resolution != null &&
                string.Equals(resolution.targetKind, "scene_root", System.StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(resolution.locator) &&
                resolution.locator.EndsWith(".unity", System.StringComparison.Ordinal))
            {
                return resolution.locator.Replace('\\', '/') + "#" + GameObjectLocatorFormatter.GetHierarchyPath(gameObject);
            }

            return GameObjectLocatorFormatter.GetLocator(gameObject);
        }

        private static HierarchyComponentSummaryRecord[] CreateComponentSummaries(GameObject gameObject, out int componentCount, out bool componentsTruncated)
        {
            var components = gameObject.GetComponents<Component>();
            componentCount = components.Length;
            componentsTruncated = componentCount > SceneQueryContract.HierarchyComponentSummaryLimit;
            var returnedCount = Math.Min(componentCount, SceneQueryContract.HierarchyComponentSummaryLimit);
            var summaries = new HierarchyComponentSummaryRecord[returnedCount];
            for (var index = 0; index < returnedCount; index++)
            {
                var component = components[index];
                var componentType = component != null ? component.GetType() : null;

                var summary = new HierarchyComponentSummaryRecord();
                summary.index = index;
                summary.type = componentType != null ? componentType.FullName : null;
                summaries[index] = summary;
            }

            return summaries;
        }

        private static JToken ToToken(string value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }
    }
}
