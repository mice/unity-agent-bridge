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
            MayTriggerDomainReload = false
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            try
            {
                cancellation?.ThrowIfCancellationRequested();
                var request = new AgentTaskControlCodec().DeserializeRequest(context?.RawArgsJson ?? "{}");
                var response = string.Equals(request?.TaskType, "compile", StringComparison.Ordinal)
                    || (request?.Operation != AgentTaskControlProtocol.Create && request?.AgentTaskId != null && request.AgentTaskId.StartsWith("compile-", StringComparison.Ordinal))
                    ? AgentTaskCompileExecutionService.Control(request)
                    : AgentTaskEditModeExecutionService.Control(request);
                var raw = new AgentTaskControlCodec().SerializeResponse(response);
                return new ToolResult
                {
                    commandId = context?.Command?.commandId,
                    tool = Descriptor.Name,
                    success = response.Success,
                    status = response.Success ? (response.Snapshot != null && response.Snapshot.IsTerminal ? ToolResultStatus.Success : ToolResultStatus.Running) : ToolResultStatus.Failed,
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
