using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>Projects protocol-independent task snapshots into the legacy ToolResult envelope.</summary>
    public static class AgentTaskToolResultAdapter
    {
        internal static void ApplyBusinessPayload(ToolResult result, string payloadJson)
        {
            result.metricsObjectJson = payloadJson;
            using (var text = new System.IO.StringReader(payloadJson))
            using (var reader = new Newtonsoft.Json.JsonTextReader(text) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                if (!(JToken.ReadFrom(reader) is JObject payload)) return;
                result.reportPath = payload.Value<string>("reportPath");
                if (payload["errors"] is JArray errors && errors.Count > 0)
                    result.errors = errors.ToObject<List<ToolError>>();
                if (payload["warnings"] is JArray warnings && warnings.Count > 0)
                    result.warnings = warnings.ToObject<List<ToolWarning>>();
            }
        }

        public static ToolResult FromSnapshot(AgentTaskSnapshot snapshot, AgentCommand command, string tool)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.IsTerminal && snapshot.Result?.Payload is ToolResult toolPayload)
            {
                var preserved = Newtonsoft.Json.JsonConvert.DeserializeObject<ToolResult>(JsonUtil.SerializeObject(toolPayload));
                if (command != null) preserved.commandId = command.commandId;
                return preserved;
            }
            var result = new ToolResult
            {
                commandId = command?.commandId,
                tool = tool ?? command?.tool,
                startedAt = snapshot.CreatedAt.UtcDateTime.ToString("O"),
                finishedAt = snapshot.TerminalAt?.UtcDateTime.ToString("O"),
                summary = "Agent task " + snapshot.State + "."
            };

            switch (snapshot.State)
            {
                case AgentTaskState.Created:
                case AgentTaskState.Running:
                    result.success = false;
                    result.status = ToolResultStatus.Running;
                    result.summary = "Agent task is in progress.";
                    break;
                case AgentTaskState.Completed:
                    var domainFailure = snapshot.Result != null && snapshot.Result.Kind == AgentTaskResultKind.DomainFailure;
                    var timedOut = snapshot.Result != null && snapshot.Result.Kind == AgentTaskResultKind.TimedOut;
                    result.success = snapshot.Result == null || snapshot.Result.Kind == AgentTaskResultKind.Succeeded;
                    result.status = timedOut ? ToolResultStatus.Timeout : domainFailure ? ToolResultStatus.Failed : ToolResultStatus.Success;
                    result.summary = string.IsNullOrWhiteSpace(snapshot.Result?.Reason) ? "Agent task completed." : snapshot.Result.Reason;
                    if (snapshot.Result?.Payload != null)
                    {
                        ApplyBusinessPayload(result, JsonUtil.SerializeObject(snapshot.Result.Payload));
                    }
                    if (domainFailure && result.errors.Count == 0)
                    {
                        result.errors.Add(new ToolError { code = "AGENT_TASK_DOMAIN_FAILURE", message = result.summary });
                    }
                    break;
                case AgentTaskState.Failed:
                    result.success = false;
                    result.status = snapshot.Result != null && snapshot.Result.Kind == AgentTaskResultKind.TimedOut ||
                                    string.Equals(snapshot.Error?.Code, "AGENT_TASK_TIMEOUT", StringComparison.Ordinal)
                        ? ToolResultStatus.Timeout
                        : ToolResultStatus.Exception;
                    result.summary = snapshot.Error?.Message ?? snapshot.Result?.Reason ?? "Agent task failed.";
                    result.errors.Add(new ToolError
                    {
                        code = snapshot.Error?.Code ?? "AGENT_TASK_FAILURE",
                        message = result.summary
                    });
                    break;
                case AgentTaskState.Cancelled:
                    result.success = false;
                    result.status = ToolResultStatus.Cancelled;
                    result.summary = string.IsNullOrWhiteSpace(snapshot.Result?.Reason) ? "Agent task cancelled." : snapshot.Result.Reason;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (snapshot.TerminalAt.HasValue)
            {
                result.durationMs = Math.Max(0, (long)(snapshot.TerminalAt.Value - snapshot.CreatedAt).TotalMilliseconds);
            }
            return result;
        }
    }
}
