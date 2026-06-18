using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    public sealed class UnitySelectionInfoTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.get_selection_info",
            Title = "Unity Selection Info",
            Description = "Inspect the current Unity Editor selection and reusable locators.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = UnityQueriesSchemas.GetSelectionInfo
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!AssetQueryJson.TryDeserializeArgs<UnityGetSelectionInfoArgs>(context.RawArgsJson, out _, out var failure))
            {
                return failure;
            }

            var selectedObjects = Selection.objects ?? new Object[0];
            var activeObject = Selection.activeObject;
            var summaries = new List<SelectionSummaryRecord>(selectedObjects.Length);
            var detailItems = new JArray();
            var counts = new SelectionKindCounts();
            int? activeIndex = null;

            for (var index = 0; index < selectedObjects.Length; index++)
            {
                cancellation?.ThrowIfCancellationRequested();
                var selectedObject = selectedObjects[index];
                var kind = ClassifySelectionKind(selectedObject, out var locator);
                IncrementCount(counts, kind);

                var summary = new SelectionSummaryRecord
                {
                    index = index,
                    kind = kind,
                    name = selectedObject != null ? selectedObject.name : string.Empty,
                    locator = locator,
                    type = selectedObject != null ? selectedObject.GetType().FullName : string.Empty
                };
                summaries.Add(summary);
                detailItems.Add(AssetQueryReportBuilder.CreateSelectionDetail(selectedObject, index, kind, locator));

                if (activeIndex == null && selectedObject == activeObject)
                {
                    activeIndex = index;
                }
            }

            var activeSummary = activeIndex.HasValue ? summaries[activeIndex.Value] : null;
            var metrics = new SelectionInfoMetrics
            {
                selectionCount = selectedObjects.Length,
                active = activeSummary,
                counts = counts,
                items = summaries.ToArray()
            };

            var report = AssetQueryReportBuilder.CreateSelectionReport(selectedObjects.Length, activeIndex, detailItems);
            var result = new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = $"Selection contains {selectedObjects.Length} item{(selectedObjects.Length == 1 ? string.Empty : "s")}.",
                MetricsObjectJson = AssetQueryJson.Serialize(metrics)
            };
            result.ReportPath = UnityQueriesReportWriter.WriteReport(context, context.CommandId, "get_selection_info", report);
            return result;
        }

        private static string ClassifySelectionKind(Object selectedObject, out string locator)
        {
            locator = null;
            if (selectedObject == null)
            {
                return "other";
            }

            if (selectedObject is Component component)
            {
                locator = GameObjectLocatorFormatter.GetLocator(component.gameObject);
                return "component";
            }

            if (selectedObject is GameObject gameObject && !EditorUtility.IsPersistent(gameObject))
            {
                locator = GameObjectLocatorFormatter.GetLocator(gameObject);
                return "sceneObject";
            }

            if (EditorUtility.IsPersistent(selectedObject))
            {
                locator = AssetDatabase.GetAssetPath(selectedObject)?.Replace('\\', '/');
                return "asset";
            }

            return "other";
        }

        private static void IncrementCount(SelectionKindCounts counts, string kind)
        {
            switch (kind)
            {
                case "asset":
                    counts.assets++;
                    break;
                case "sceneObject":
                    counts.sceneObjects++;
                    break;
                case "component":
                    counts.components++;
                    break;
                default:
                    counts.other++;
                    break;
            }
        }
    }
}
