using System;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>Projects protocol-independent task snapshots into the legacy ToolResult envelope.</summary>
    public static class AgentTaskToolResultAdapter
    {
        public static ToolResult FromSnapshot(AgentTaskSnapshot snapshot, AgentCommand command, string tool)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
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
                    result.success = !domainFailure;
                    result.status = domainFailure ? ToolResultStatus.Failed : ToolResultStatus.Success;
                    result.summary = string.IsNullOrWhiteSpace(snapshot.Result?.Reason) ? "Agent task completed." : snapshot.Result.Reason;
                    if (snapshot.Result?.Payload != null)
                    {
                        result.metricsObjectJson = JsonUtil.SerializeObject(snapshot.Result.Payload);
                    }
                    if (domainFailure)
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
