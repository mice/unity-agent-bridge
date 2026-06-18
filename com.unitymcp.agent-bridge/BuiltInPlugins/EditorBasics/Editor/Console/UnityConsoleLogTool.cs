using System;
using System.Collections.Generic;
using System.Linq;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    public sealed class UnityConsoleLogTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.get_console",
            Title = "Unity Console",
            Description = "Read retained Unity console entries by type.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = "{\"type\":\"object\",\"properties\":{\"types\":{\"minItems\":1,\"maxItems\":3,\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"error\",\"warning\",\"info\"]}},\"count\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1000},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":9007199254740991}},\"required\":[\"types\"],\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!EditorBasicsJson.TryDeserializeArgs<UnityConsoleLogArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args = args ?? new UnityConsoleLogArgs();
            if (!TryNormalizeTypes(args.types, out var requestedTypes, out var normalizedTypes, out failure))
            {
                return failure;
            }

            if (args.count < 0 || args.count > EditorBasicsConsoleLogStore.MaxEntriesPerType)
            {
                return EditorBasicsResult.InvalidArgs("AGENTBRIDGE_CONSOLE_COUNT_INVALID", $"count must be in the range 0..{EditorBasicsConsoleLogStore.MaxEntriesPerType}.");
            }

            var results = normalizedTypes.Select(type => CreateBucket(type, args.count)).ToArray();
            var totalEntryCount = results.Sum(bucket => bucket.returnedCount);
            var metrics = new UnityConsoleLogMetrics
            {
                requestedTypes = requestedTypes,
                requestedCountPerType = args.count,
                results = results
            };

            return new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = $"Collected {totalEntryCount} console entr{(totalEntryCount == 1 ? "y" : "ies")} across {results.Length} bucket{(results.Length == 1 ? string.Empty : "s")}.",
                MetricsObjectJson = EditorBasicsJson.Serialize(metrics),
                ReportPath = EditorBasicsReportWriter.WriteReport(context.ProjectRoot, context.TempRoot, context.CommandId, "console", metrics)
            };
        }

        private static UnityConsoleLogBucket CreateBucket(ConsoleLogQueryType queryType, int requestedCount)
        {
            var entries = EditorBasicsConsoleLogStore.GetSnapshot(queryType, requestedCount)
                .Select(entry => new UnityConsoleLogEntry
                {
                    condition = entry.Condition,
                    stackTrace = entry.StackTrace,
                    type = entry.Type.ToString(),
                    timestamp = new DateTime(entry.TimestampUtcTicks, DateTimeKind.Utc).ToString("O")
                })
                .ToArray();

            return new UnityConsoleLogBucket
            {
                type = ToContractType(queryType),
                returnedCount = entries.Length,
                entries = entries
            };
        }

        private static bool TryNormalizeTypes(string[] rawTypes, out string[] requestedTypes, out ConsoleLogQueryType[] normalizedTypes, out UnityMcpToolResult failure)
        {
            requestedTypes = Array.Empty<string>();
            normalizedTypes = Array.Empty<ConsoleLogQueryType>();
            failure = null;

            if (rawTypes == null || rawTypes.Length == 0)
            {
                failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_CONSOLE_TYPES_REQUIRED", "types must contain one or more of: error, warning, info.");
                return false;
            }

            if (rawTypes.Length > 3)
            {
                failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_CONSOLE_TYPES_TOO_MANY", "types must not contain more than three values.");
                return false;
            }

            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            var requested = new List<string>(rawTypes.Length);
            var normalized = new List<ConsoleLogQueryType>(rawTypes.Length);
            foreach (var rawType in rawTypes)
            {
                if (!TryParseQueryType(rawType, out var queryType))
                {
                    failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_CONSOLE_TYPE_INVALID", "types must contain only: error, warning, info.");
                    return false;
                }

                if (!seenTypes.Add(rawType))
                {
                    failure = EditorBasicsResult.InvalidArgs("AGENTBRIDGE_CONSOLE_TYPE_DUPLICATE", "types must not contain duplicate values.");
                    return false;
                }

                requested.Add(rawType);
                normalized.Add(queryType);
            }

            requestedTypes = requested.ToArray();
            normalizedTypes = normalized.ToArray();
            return true;
        }

        private static bool TryParseQueryType(string rawType, out ConsoleLogQueryType queryType)
        {
            switch (rawType)
            {
                case "error":
                    queryType = ConsoleLogQueryType.Error;
                    return true;
                case "warning":
                    queryType = ConsoleLogQueryType.Warning;
                    return true;
                case "info":
                    queryType = ConsoleLogQueryType.Info;
                    return true;
                default:
                    queryType = default;
                    return false;
            }
        }

        private static string ToContractType(ConsoleLogQueryType queryType)
        {
            switch (queryType)
            {
                case ConsoleLogQueryType.Error:
                    return "error";
                case ConsoleLogQueryType.Warning:
                    return "warning";
                default:
                    return "info";
            }
        }
    }
}
