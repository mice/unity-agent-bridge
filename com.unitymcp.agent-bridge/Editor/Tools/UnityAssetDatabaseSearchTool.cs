using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.assetdatabase_search")]
    public sealed class UnityAssetDatabaseSearchTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.assetdatabase_search",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Search Unity project assets with stable paging and reportPath detail payloads.",
            AllowedModes = ToolExecutionModes.EditAndPlay,
            SideEffect = ToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.assetdatabase_search.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!AssetQueryJson.TryDeserializeArgs<UnityAssetDatabaseSearchArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new UnityAssetDatabaseSearchArgs();
            if (string.IsNullOrWhiteSpace(args.query))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_QUERY_REQUIRED", "query is required.");
            }

            if (args.offset < 0)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_ASSET_OFFSET_INVALID", "offset must be greater than or equal to 0.");
            }

            if (args.limit <= 0 || args.limit > AssetQueryContract.MaxAssetSearchLimit)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_ASSET_LIMIT_INVALID", $"limit must be in the range 1..{AssetQueryContract.MaxAssetSearchLimit}.");
            }

            if (!AssetQueryPathValidator.TryNormalizeFolders(args.folders, out var normalizedFolders, out failure))
            {
                return failure;
            }

            var guids = AssetDatabase.FindAssets(args.query, normalizedFolders.Length > 0 ? normalizedFolders : null)
                .OrderBy(guid => AssetDatabase.GUIDToAssetPath(guid), StringComparer.Ordinal)
                .ToArray();

            var page = guids.Skip(args.offset).Take(args.limit).ToArray();
            var summaries = page.Select((guid, pageIndex) => AssetQueryResultBuilder.BuildSummary(guid, args.offset + pageIndex)).ToArray();
            var truncated = args.offset + page.Length < guids.Length;
            var metrics = new AssetDatabaseSearchMetrics
            {
                query = args.query,
                folders = normalizedFolders,
                totalCount = guids.Length,
                returnedCount = summaries.Length,
                offset = args.offset,
                limit = args.limit,
                truncated = truncated,
                nextOffset = truncated ? args.offset + summaries.Length : (int?)null,
                results = summaries
            };

            var assetDetails = new JArray(page.Select((guid, pageIndex) => AssetQueryResultBuilder.BuildDetail(guid, args.offset + pageIndex, args.includeDetails)));
            var report = AssetQueryReportBuilder.CreateAssetSearchReport(args, metrics, assetDetails);

            var result = new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = $"Found {metrics.totalCount} assets for query '{args.query}'; returned {metrics.returnedCount} summaries.",
                metricsObjectJson = AssetQueryJson.Serialize(metrics)
            };
            result.reportPath = AgentBridgeReportWriter.WriteReport(context.Settings, context.Command.commandId, "assetdatabase_search", report);
            return result;
        }
    }
}
