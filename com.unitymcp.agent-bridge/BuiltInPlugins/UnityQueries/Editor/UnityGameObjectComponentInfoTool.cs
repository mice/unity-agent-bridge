using System;
using UnityMcp.Plugin;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    public sealed class UnityGameObjectComponentInfoTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.get_gameobject_component_info",
            Title = "Unity GameObject Component Info",
            Description = "Inspect GameObject components and bounded serialized property detail through reusable locators.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = UnityQueriesSchemas.GetGameObjectComponentInfo
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!AssetQueryJson.TryDeserializeArgs<UnityGetGameObjectComponentInfoArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new UnityGetGameObjectComponentInfoArgs();
            args.propertyMode = string.IsNullOrWhiteSpace(args.propertyMode) ? AssetQueryContract.DefaultPropertyMode : args.propertyMode;
            if (!string.Equals(args.propertyMode, AssetQueryContract.DefaultPropertyMode, StringComparison.Ordinal) &&
                !string.Equals(args.propertyMode, AssetQueryContract.SerializedPropertyMode, StringComparison.Ordinal))
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_PROPERTY_MODE_INVALID", "propertyMode must be one of: debug, serialized.");
            }

            if (args.propertyLimit < 0 || args.propertyLimit > AssetQueryContract.MaxPropertyLimit)
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_PROPERTY_LIMIT_INVALID", $"propertyLimit must be in the range 0..{AssetQueryContract.MaxPropertyLimit}.");
            }

            if (args.arrayElementLimit < 0 || args.arrayElementLimit > AssetQueryContract.MaxArrayElementLimit)
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARRAY_ELEMENT_LIMIT_INVALID", $"arrayElementLimit must be in the range 0..{AssetQueryContract.MaxArrayElementLimit}.");
            }

            if (args.stringMaxLength < AssetQueryContract.MinStringMaxLength || args.stringMaxLength > AssetQueryContract.MaxStringMaxLength)
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_STRING_MAX_LENGTH_INVALID", $"stringMaxLength must be in the range {AssetQueryContract.MinStringMaxLength}..{AssetQueryContract.MaxStringMaxLength}.");
            }

            if (args.componentIndex < -1)
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_COMPONENT_INDEX_OUT_OF_RANGE", "componentIndex must be greater than or equal to 0.");
            }

            if (!GameObjectLocatorResolver.TryResolve(args.locator, out var gameObject, out failure))
            {
                return failure;
            }

            var components = gameObject.GetComponents<Component>();
            var targetMetrics = new GameObjectTargetRecord
            {
                name = gameObject.name,
                locator = GameObjectLocatorFormatter.GetLocator(gameObject),
                path = GameObjectLocatorFormatter.GetHierarchyPath(gameObject),
                scenePath = gameObject.scene.IsValid() ? gameObject.scene.path.Replace('\\', '/') : null,
                instanceId = gameObject.GetInstanceID()
            };
            var targetReport = AssetQueryReportBuilder.CreateGameObjectTargetDetail(gameObject);

            var componentSummaries = new List<ComponentSummaryRecord>(components.Length);
            var componentDetails = new JArray();
            var matchedComponents = new List<(int index, Component component)>();
            for (var index = 0; index < components.Length; index++)
            {
                cancellation?.ThrowIfCancellationRequested();
                var component = components[index];
                var componentType = component != null ? component.GetType() : null;
                var scriptPath = component != null ? MonoScript.FromMonoBehaviour(component as MonoBehaviour) : null;
                componentSummaries.Add(new ComponentSummaryRecord
                {
                    index = index,
                    name = component != null ? ObjectNames.GetInspectorTitle(component) : "Missing Script",
                    type = componentType != null ? componentType.FullName : null,
                    scriptPath = scriptPath != null ? AssetDatabase.GetAssetPath(scriptPath)?.Replace('\\', '/') : null
                });

                if (MatchesComponent(component, componentSummaries[index].name, componentType, args.componentName, index, args.componentIndex))
                {
                    matchedComponents.Add((index, component));
                }
            }

            if (args.componentIndex >= components.Length)
            {
                return UnityQueriesResult.InvalidArgs("AGENTBRIDGE_COMPONENT_INDEX_OUT_OF_RANGE", "componentIndex is outside the target GameObject component list.");
            }

            var inspectMode = !string.IsNullOrWhiteSpace(args.componentName) || args.componentIndex >= 0;
            var propertyCount = 0;
            var returnedPropertyCount = 0;
            var truncated = false;

            if (inspectMode)
            {
                foreach (var matchedComponent in matchedComponents)
                {
                    var component = matchedComponent.component;
                    if (component == null)
                    {
                        componentDetails.Add(AssetQueryReportBuilder.CreateComponentDetail(matchedComponent.index, "Missing Script", null, null, null, null, new JArray(), false));
                        continue;
                    }

                    var sample = SerializedComponentSampler.Sample(component, args.propertyMode, args.propertyLimit, args.arrayElementLimit, args.stringMaxLength, cancellation);
                    propertyCount += sample.PropertyCount;
                    returnedPropertyCount += sample.ReturnedPropertyCount;
                    truncated |= sample.Truncated;

                    var scriptAsset = MonoScript.FromMonoBehaviour(component as MonoBehaviour);
                    var scriptPath = scriptAsset != null ? AssetDatabase.GetAssetPath(scriptAsset)?.Replace('\\', '/') : null;
                    var scriptGuid = !string.IsNullOrWhiteSpace(scriptPath) ? AssetDatabase.AssetPathToGUID(scriptPath) : null;
                    var properties = new JArray(sample.Properties.Select(SerializedComponentSampler.ToJson));
                    componentDetails.Add(AssetQueryReportBuilder.CreateComponentDetail(
                        matchedComponent.index,
                        ObjectNames.GetInspectorTitle(component),
                        component.GetType().FullName,
                        component.GetType().Assembly.GetName().Name,
                        scriptGuid,
                        scriptPath,
                        properties,
                        sample.Truncated));

                    componentSummaries[matchedComponent.index].propertyCount = sample.PropertyCount;
                    componentSummaries[matchedComponent.index].returnedPropertyCount = sample.ReturnedPropertyCount;
                }
            }
            else
            {
                for (var index = 0; index < componentSummaries.Count; index++)
                {
                    var summary = componentSummaries[index];
                    componentDetails.Add(AssetQueryReportBuilder.CreateComponentDetail(summary.index, summary.name, summary.type, null, null, summary.scriptPath, new JArray(), false));
                }
            }

            var metrics = new GameObjectComponentInfoMetrics
            {
                mode = inspectMode ? "component_inspect" : "component_list",
                target = targetMetrics,
                componentQuery = inspectMode
                    ? new ComponentQueryRecord
                    {
                        componentName = args.componentName,
                        componentIndex = args.componentIndex >= 0 ? args.componentIndex : (int?)null,
                        propertyMode = args.propertyMode
                    }
                    : null,
                componentCount = components.Length,
                matchedCount = inspectMode ? matchedComponents.Count : (int?)null,
                propertyCount = inspectMode ? propertyCount : (int?)null,
                returnedPropertyCount = inspectMode ? returnedPropertyCount : (int?)null,
                truncated = inspectMode ? truncated : (bool?)null,
                components = componentSummaries.ToArray(),
                details = ToolResultMetadata.CreateDetails(true, inspectMode && (truncated || returnedPropertyCount > 0), "/components"),
                followUp = ToolResultMetadata.None()
            };

            var plannedReportPath = UnityQueriesReportWriter.GetReportRelativePath(context, context.CommandId, "get_gameobject_component_info");
            ToolResultMetadata.AttachReportPath(metrics.details, plannedReportPath);
            metrics.followUp = BuildFollowUp(metrics, plannedReportPath);

            var report = AssetQueryReportBuilder.CreateGameObjectComponentReport(metrics.mode, targetReport, componentDetails);
            var result = new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = inspectMode
                    ? $"Matched {matchedComponents.Count} component(s) on GameObject '{gameObject.name}'."
                    : $"GameObject '{gameObject.name}' has {components.Length} component(s).",
                MetricsObjectJson = AssetQueryJson.Serialize(metrics),
                ReportPath = UnityQueriesReportWriter.WriteReport(context, context.CommandId, "get_gameobject_component_info", report)
            };
            return result;
        }

        private static ToolFollowUpMetadata BuildFollowUp(GameObjectComponentInfoMetrics metrics, string reportPath)
        {
            if (metrics == null)
            {
                return ToolResultMetadata.None();
            }

            if (string.Equals(metrics.mode, "component_list", StringComparison.Ordinal))
            {
                if (metrics.componentCount <= 0 || metrics.components == null || metrics.components.Length == 0)
                {
                    return ToolResultMetadata.None();
                }

                return ToolResultMetadata.Recommended(
                    ToolResultMetadata.Option(
                        "unity.get_gameobject_component_info",
                        "Inspect a specific component on this GameObject.",
                        new JObject
                        {
                            ["locator"] = metrics.target.locator,
                            ["componentIndex"] = metrics.components[0].index
                        }));
            }

            if (metrics.truncated == true || (metrics.returnedPropertyCount ?? 0) > 0)
            {
                return ToolResultMetadata.Recommended(
                    ToolResultMetadata.Option(
                        "unity.read_report",
                        "Read the component report for full serialized property detail within the applied bounds.",
                        new JObject
                        {
                            ["reportPath"] = reportPath,
                            ["jsonPointer"] = "/components"
                        }));
            }

            return ToolResultMetadata.None();
        }

        private static bool MatchesComponent(Component component, string displayName, Type componentType, string componentName, int index, int componentIndex)
        {
            if (componentIndex >= 0 && index != componentIndex)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(componentName))
            {
                return componentIndex < 0 || index == componentIndex;
            }

            if (component == null || componentType == null)
            {
                return false;
            }

            return string.Equals(displayName, componentName, StringComparison.Ordinal) ||
                   string.Equals(componentType.Name, componentName, StringComparison.Ordinal) ||
                   string.Equals(componentType.FullName, componentName, StringComparison.Ordinal);
        }
    }
}
