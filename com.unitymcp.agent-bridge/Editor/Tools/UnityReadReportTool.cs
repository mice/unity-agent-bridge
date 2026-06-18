using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.read_report")]
    public sealed class UnityReadReportTool : IAgentTool
    {
        private const string ContractVersion = "report_read.v1";
        private const int DefaultLimit = 100;
        private const int MaxLimit = 500;
        private const int DefaultMaxBytes = 65536;
        private const int MaxMaxBytes = 262144;

        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.read_report",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Read bounded JSON slices from Agent Bridge report files without re-querying Unity APIs.",
            AllowedModes = ToolExecutionModes.EditAndPlay,
            SideEffect = ToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.read_report.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<UnityReadReportArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new UnityReadReportArgs();
            if (string.IsNullOrWhiteSpace(args.reportPath))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_PATH_REQUIRED", "reportPath is required.");
            }

            if (args.offset < 0)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_OFFSET_INVALID", "offset must be greater than or equal to 0.");
            }

            if (args.limit <= 0 || args.limit > MaxLimit)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_LIMIT_INVALID", $"limit must be in the range 1..{MaxLimit}.");
            }

            if (args.maxBytes <= 0 || args.maxBytes > MaxMaxBytes)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_MAX_BYTES_INVALID", $"maxBytes must be in the range 1..{MaxMaxBytes}.");
            }

            if (!TryResolveReportPath(context.Settings, args.reportPath, out var absolutePath, out var normalizedReportPath, out failure))
            {
                return failure;
            }

            if (!File.Exists(absolutePath))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_NOT_FOUND", "reportPath does not resolve to an existing Agent Bridge report.");
            }

            JObject report;
            try
            {
                report = JObject.Parse(File.ReadAllText(absolutePath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_PARSE_FAILED", exception.Message);
            }

            if (!IsValidAgentBridgeReport(report))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_SHAPE_INVALID", "reportPath does not resolve to an Agent Bridge report payload.");
            }

            var jsonPointer = string.IsNullOrWhiteSpace(args.jsonPointer) ? string.Empty : args.jsonPointer;
            if (!TrySelectByJsonPointer(report, jsonPointer, out var selected, out var pointerFailure))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_POINTER_INVALID", pointerFailure);
            }

            cancellation?.ThrowIfCancellationRequested();

            var metrics = new JObject
            {
                ["contractVersion"] = ContractVersion,
                ["reportPath"] = normalizedReportPath,
                ["jsonPointer"] = jsonPointer,
                ["offset"] = args.offset,
                ["limit"] = args.limit,
                ["maxBytes"] = args.maxBytes
            };

            JToken responseValue;
            if (selected is JArray array)
            {
                var totalCount = array.Count;
                var slice = new JArray(array.Skip(args.offset).Take(args.limit).Select(token => token.DeepClone()));
                var nextOffset = args.offset + slice.Count < totalCount ? args.offset + slice.Count : (int?)null;
                responseValue = slice;

                metrics["selectedIsArray"] = true;
                metrics["returnedCount"] = slice.Count;
                metrics["totalCount"] = totalCount;
                metrics["nextOffset"] = nextOffset.HasValue ? JToken.FromObject(nextOffset.Value) : JValue.CreateNull();
                metrics["truncated"] = nextOffset.HasValue;
                metrics["items"] = slice;
                metrics["value"] = JValue.CreateNull();
            }
            else
            {
                responseValue = selected.DeepClone();
                metrics["selectedIsArray"] = false;
                metrics["returnedCount"] = JValue.CreateNull();
                metrics["totalCount"] = JValue.CreateNull();
                metrics["nextOffset"] = JValue.CreateNull();
                metrics["truncated"] = false;
                metrics["items"] = JValue.CreateNull();
                metrics["value"] = responseValue;
            }

            var serializedValue = responseValue.ToString(Formatting.None);
            var byteCount = Encoding.UTF8.GetByteCount(serializedValue);
            metrics["byteCount"] = byteCount;
            if (byteCount > args.maxBytes)
            {
                return new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Failed,
                    summary = "The selected report slice exceeds the applied maxBytes limit.",
                    reportPath = normalizedReportPath,
                    errors = new List<ToolError>
                    {
                        new ToolError
                        {
                            code = "AGENTBRIDGE_REPORT_SLICE_TOO_LARGE",
                            message = $"The selected report slice exceeds the applied maxBytes limit of {args.maxBytes} bytes."
                        }
                    },
                    metricsObjectJson = metrics.ToString(Formatting.None)
                };
            }

            return new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = selected is JArray
                    ? $"Read {metrics["returnedCount"]} item(s) from the selected report array."
                    : "Read the selected report value.",
                reportPath = normalizedReportPath,
                metricsObjectJson = metrics.ToString(Formatting.None)
            };
        }

        private static bool TryResolveReportPath(AgentBridgeSettings settings, string requestedReportPath, out string absolutePath, out string normalizedReportPath, out ToolResult failure)
        {
            absolutePath = null;
            normalizedReportPath = null;
            failure = null;

            if (Path.IsPathRooted(requestedReportPath))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_PATH_ABSOLUTE", "Absolute report paths are not allowed.");
                return false;
            }

            if (!requestedReportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_PATH_EXTENSION_INVALID", "reportPath must reference a .json file.");
                return false;
            }

            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            var reportRoot = AgentBridgeReportWriter.GetReportRootAbsolutePath(settings);
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(reportRoot))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_ROOT_UNAVAILABLE", "The Agent Bridge report root could not be resolved.");
                return false;
            }

            var combinedPath = Path.GetFullPath(Path.Combine(projectRoot, requestedReportPath.Replace('/', Path.DirectorySeparatorChar)));
            var reportRootWithSeparator = reportRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? reportRoot
                : reportRoot + Path.DirectorySeparatorChar;
            if (!combinedPath.StartsWith(reportRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REPORT_PATH_OUTSIDE_ROOT", "reportPath must resolve within the Agent Bridge report root.");
                return false;
            }

            absolutePath = combinedPath;
            normalizedReportPath = requestedReportPath.Replace('\\', '/');
            return true;
        }

        private static bool IsValidAgentBridgeReport(JObject report)
        {
            return string.Equals(report.Value<string>("schemaVersion"), JsonUtil.CurrentSchemaVersion, StringComparison.Ordinal) &&
                   (!string.IsNullOrWhiteSpace(report.Value<string>("payloadVersion")) || report["generatedAt"] != null);
        }

        private static bool TrySelectByJsonPointer(JToken root, string pointer, out JToken selected, out string error)
        {
            selected = null;
            error = null;

            if (root == null)
            {
                error = "Report JSON is empty.";
                return false;
            }

            if (string.IsNullOrEmpty(pointer))
            {
                selected = root;
                return true;
            }

            if (!pointer.StartsWith("/", StringComparison.Ordinal))
            {
                error = "jsonPointer must be empty or begin with '/'.";
                return false;
            }

            JToken current = root;
            foreach (var rawSegment in pointer.Split('/').Skip(1))
            {
                var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
                if (current is JObject obj)
                {
                    if (!obj.TryGetValue(segment, out current))
                    {
                        error = $"jsonPointer segment '{segment}' could not be resolved.";
                        return false;
                    }

                    continue;
                }

                if (current is JArray array)
                {
                    if (!int.TryParse(segment, out var index) || index < 0 || index >= array.Count)
                    {
                        error = $"jsonPointer array index '{segment}' is invalid.";
                        return false;
                    }

                    current = array[index];
                    continue;
                }

                error = $"jsonPointer segment '{segment}' does not apply to the selected token.";
                return false;
            }

            selected = current;
            return true;
        }
    }

    [Serializable]
    public sealed class UnityReadReportArgs
    {
        public string reportPath;
        public string jsonPointer = string.Empty;
        public int offset = 0;
        public int limit = 100;
        public int maxBytes = 65536;
    }
}
