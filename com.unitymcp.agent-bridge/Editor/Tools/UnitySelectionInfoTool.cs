using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.get_selection_info")]
    public sealed class UnitySelectionInfoTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.get_selection_info",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Inspect the current Unity Editor selection and reusable locators.",
            AllowedModes = ToolExecutionModes.EditAndPlay,
            SideEffect = ToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.get_selection_info.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
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
            var result = new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = $"Selection contains {selectedObjects.Length} item{(selectedObjects.Length == 1 ? string.Empty : "s")}.",
                metricsObjectJson = AssetQueryJson.Serialize(metrics)
            };
            result.reportPath = AgentBridgeReportWriter.WriteReport(context.Settings, context.Command.commandId, "get_selection_info", report);
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
