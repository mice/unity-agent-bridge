using System;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.agent_task_control")]
    internal sealed class UnityAgentTaskControlTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.agent_task_control",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Create, query, retrieve, or cancel a project-local AgentTask.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = false,
            SupportsConcurrentObservation = true
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            try
            {
                cancellation?.ThrowIfCancellationRequested();
                var request = new AgentTaskControlCodec().DeserializeRequest(context?.RawArgsJson ?? "{}");
                if (request.Version != AgentTaskControlProtocol.CurrentVersion) return ToolResult.Unsupported("UNSUPPORTED_VERSION", "Unsupported task control version.");
                request = EditorTaskScheduling.ReconcileCreation(request);
                var response = request.Operation == AgentTaskControlProtocol.Cancel && !string.IsNullOrEmpty(request.CommandId)
                    ? EditorTaskScheduling.CancelCommand(request.CommandId)
                    : (string.Equals(request?.TaskType, AgentTaskTemplateExecutionService.TaskType, StringComparison.Ordinal)
                    || request?.AgentTaskId != null && request.AgentTaskId.StartsWith("template-", StringComparison.Ordinal))
                    ? AgentTaskTemplateExecutionService.Control(request)
                    : (string.Equals(request?.TaskType, AgentTaskVerifiedAnalysisExecutionService.TaskType, StringComparison.Ordinal)
                    || string.Equals(request?.TaskType, AgentTaskVerifiedAnalysisExecutionService.AssetTaskType, StringComparison.Ordinal))
                    ? AgentTaskVerifiedAnalysisExecutionService.Control(request)
                    : request?.AgentTaskId != null && request.AgentTaskId.StartsWith("analysis-", StringComparison.Ordinal)
                    ? AgentTaskVerifiedAnalysisExecutionService.Control(request)
                    : string.Equals(request?.TaskType, "agent_run", StringComparison.Ordinal)
                    || (request?.AgentTaskId != null && request.AgentTaskId.StartsWith("agent-", StringComparison.Ordinal))
                    ? new AgentTaskControlResponse { Success = false, ErrorCode = "AGENT_TASK_UNSUPPORTED", ErrorMessage = "Generic agent execution is unavailable; use an enabled fixed-template provider." }
                    : string.Equals(request?.TaskType, "compile", StringComparison.Ordinal)
                    || (request?.Operation != AgentTaskControlProtocol.Create && request?.AgentTaskId != null && request.AgentTaskId.StartsWith("compile-", StringComparison.Ordinal))
                    ? AgentTaskCompileExecutionService.Control(request)
                    : string.Equals(request?.TaskType, "run_playmode_tests", StringComparison.Ordinal)
                    || (request?.Operation != AgentTaskControlProtocol.Create && request?.AgentTaskId != null && request.AgentTaskId.StartsWith("playmode-", StringComparison.Ordinal))
                    ? AgentTaskPlayModeExecutionService.Control(request)
                    : AgentTaskEditModeExecutionService.Control(request);
                var raw = new AgentTaskControlCodec().SerializeResponse(EditorTaskScheduling.Observe(response));
                return new ToolResult
                {
                    commandId = context?.Command?.commandId,
                    tool = Descriptor.Name,
                    success = response.Success,
                    // The control command itself is synchronous. A Running snapshot is data,
                    // not a request for the bridge poller to hold this command open.
                    status = response.Success ? ToolResultStatus.Success : ToolResultStatus.Failed,
                    summary = response.Success ? "Agent task control completed." : (response.ErrorMessage ?? "Agent task control failed."),
                    metricsObjectJson = raw
                };
            }
            catch (Exception exception)
            {
                return ToolResult.InvalidArgs("AGENT_TASK_CONTROL_INVALID", exception.Message);
            }
        }
    }
}
