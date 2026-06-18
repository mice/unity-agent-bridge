using System;
using UnityMcp.Plugin;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    public sealed class UnityGetHierarchyTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.get_hierarchy",
            Title = "Unity Get Hierarchy",
            Description = "Enumerate bounded scene, prefab, selection, instance, or subtree hierarchy summaries.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = UnityQueriesSchemas.GetHierarchy
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!SceneQueryJson.TryDeserializeArgs<UnityGetHierarchyArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new UnityGetHierarchyArgs();
            if (args.maxDepth < 0)
            {
                return FinalizeFailure(
                    context,
                    args,
                    UnityQueriesResult.InvalidArgs("AGENTBRIDGE_HIERARCHY_MAX_DEPTH_INVALID", "maxDepth must be greater than or equal to 0."),
                    null,
                    null);
            }

            if (args.limit <= 0 || args.limit > SceneQueryContract.MaxHierarchyLimit)
            {
                return FinalizeFailure(
                    context,
                    args,
                    UnityQueriesResult.InvalidArgs("AGENTBRIDGE_HIERARCHY_LIMIT_INVALID", $"limit must be in the range 1..{SceneQueryContract.MaxHierarchyLimit}."),
                    null,
                    null);
            }

            if (!HierarchyTargetResolver.TryResolve(args.locator, out var resolution, out failure))
            {
                return FinalizeFailure(context, args, failure, null, null);
            }

            var nodes = new List<HierarchyNodeRecord>();
            var metrics = new HierarchyMetrics
            {
                target = SceneQueryReportBuilder.CreateTargetRecord(resolution),
                limit = args.limit,
                maxDepth = args.maxDepth,
                details = ToolResultMetadata.CreateDetails(true, false, "/result/nodes"),
                followUp = ToolResultMetadata.None()
            };

            if (string.Equals(resolution.targetKind, "scene_root", StringComparison.Ordinal))
            {
                var roots = resolution.scene.GetRootGameObjects();
                metrics.rootCount = roots.Length;
                TraverseRoots(roots, resolution, args, nodes, metrics, cancellation);
            }
            else
            {
                metrics.rootCount = resolution.gameObject != null ? 1 : 0;
                if (resolution.gameObject != null)
                {
                    TraverseGameObject(resolution.gameObject, resolution, null, 0, args, nodes, metrics, cancellation);
                }
            }

            metrics.returnedNodeCount = nodes.Count;
            metrics.nodes = nodes.ToArray();

            var plannedReportPath = UnityQueriesReportWriter.GetReportRelativePath(context, context.CommandId, "get_hierarchy");
            ToolResultMetadata.AttachReportPath(metrics.details, plannedReportPath);
            metrics.followUp = BuildFollowUp(metrics, plannedReportPath);

            var report = SceneQueryReportBuilder.CreateHierarchyReport(args, metrics);
            var result = new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = $"Returned {metrics.returnedNodeCount} hierarchy node(s).",
                MetricsObjectJson = SceneQueryJson.Serialize(metrics),
                ReportPath = UnityQueriesReportWriter.WriteReport(context, context.CommandId, "get_hierarchy", report)
            };

            return result;
        }

        private static UnityMcpToolResult FinalizeFailure(UnityMcpToolContext context, UnityGetHierarchyArgs args, UnityMcpToolResult result, HierarchyTargetRecord target, object metrics)
        {
            result.ReportPath = UnityQueriesReportWriter.WriteReport(
                context,
                context.CommandId,
                "get_hierarchy",
                SceneQueryReportBuilder.CreateHierarchyFailureReport(args, result, target, metrics));
            return result;
        }

        private static ToolFollowUpMetadata BuildFollowUp(HierarchyMetrics metrics, string reportPath)
        {
            if (metrics == null || metrics.returnedNodeCount == 0)
            {
                return ToolResultMetadata.None();
            }

            if (metrics.truncated)
            {
                return ToolResultMetadata.Recommended(
                    ToolResultMetadata.Option(
                        "unity.read_report",
                        "Read the bounded hierarchy report for the full result within the applied limits.",
                        new JObject
                        {
                            ["reportPath"] = reportPath,
                            ["jsonPointer"] = "/result/nodes"
                        }),
                    ToolResultMetadata.Option(
                        "unity.get_hierarchy",
                        "Query a smaller subtree to reduce response size and inspect a narrower target.",
                        new JObject
                        {
                            ["locator"] = metrics.nodes[0].locator,
                            ["maxDepth"] = SceneQueryContract.DefaultHierarchyMaxDepth,
                            ["limit"] = SceneQueryContract.DefaultHierarchyLimit
                        }));
            }

            if (metrics.returnedNodeCount == 1)
            {
                return ToolResultMetadata.Recommended(
                    ToolResultMetadata.Option(
                        "unity.get_gameobject_component_info",
                        "Inspect the selected GameObject components in more detail.",
                        new JObject
                        {
                            ["locator"] = metrics.nodes[0].locator
                        }));
            }

            return ToolResultMetadata.Recommended(
                ToolResultMetadata.Option(
                    "unity.get_hierarchy",
                    "Inspect a subtree rooted at one of the returned nodes.",
                    new JObject
                    {
                        ["locator"] = metrics.nodes[0].locator,
                        ["maxDepth"] = SceneQueryContract.DefaultHierarchyMaxDepth,
                        ["limit"] = SceneQueryContract.DefaultHierarchyLimit
                    }));
        }

        private static void TraverseRoots(GameObject[] roots, HierarchyTargetResolution resolution, UnityGetHierarchyArgs args, List<HierarchyNodeRecord> nodes, HierarchyMetrics metrics, IUnityMcpCancellation cancellation)
        {
            foreach (var root in roots)
            {
                if (nodes.Count >= args.limit)
                {
                    metrics.truncated = true;
                    return;
                }

                TraverseGameObject(root, resolution, null, 0, args, nodes, metrics, cancellation);
            }
        }

        private static void TraverseGameObject(GameObject gameObject, HierarchyTargetResolution resolution, int? parentIndex, int depth, UnityGetHierarchyArgs args, List<HierarchyNodeRecord> nodes, HierarchyMetrics metrics, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            metrics.visitedCount++;
            metrics.nodeCount = (metrics.nodeCount ?? 0) + 1;

            if (depth > args.maxDepth)
            {
                metrics.truncated = true;
                return;
            }

            if (nodes.Count >= args.limit)
            {
                metrics.truncated = true;
                return;
            }

            var currentIndex = nodes.Count;
            nodes.Add(SceneQueryReportBuilder.CreateNodeRecord(gameObject, resolution, currentIndex, parentIndex, depth, args.includeComponents));

            for (var index = 0; index < gameObject.transform.childCount; index++)
            {
                if (nodes.Count >= args.limit)
                {
                    metrics.truncated = true;
                    return;
                }

                TraverseGameObject(gameObject.transform.GetChild(index).gameObject, resolution, currentIndex, depth + 1, args, nodes, metrics, cancellation);
            }
        }
    }
}
