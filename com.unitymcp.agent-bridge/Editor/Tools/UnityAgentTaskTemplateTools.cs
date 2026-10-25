using System;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.agent_task_template.list")]
    internal sealed class UnityAgentTaskTemplateListTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.agent_task_template.list", SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "List built-in and project task templates, including unavailable entries and diagnostics.",
            AllowedModes = ToolExecutionModes.EditAndPlay, SideEffect = ToolSideEffect.None, SupportsConcurrentObservation = true
        };
        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            try
            {
                if (AgentTaskTemplateCatalog.ParseObject(context.RawArgsJson ?? "{}").Count != 0) throw new ArgumentException("No arguments are accepted.");
                return new ToolResult { success = true, status = ToolResultStatus.Success, tool = Descriptor.Name,
                    summary = "Task template catalog.", metricsObjectJson = AgentTaskTemplateExecutionService.Catalog().ToJson() };
            }
            catch (Exception exception) { return ToolResult.InvalidArgs("TEMPLATE_LIST_INVALID", exception.Message); }
        }
    }

    [AgentTool("unity.agent_task_template.run")]
    internal sealed class UnityAgentTaskTemplateRunTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.agent_task_template.run", SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run a validated task template by ID with business inputs.",
            AllowedModes = ToolExecutionModes.EditAndPlay, SideEffect = ToolSideEffect.ReadsProject, SupportsConcurrentObservation = true
        };
        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            var response = AgentTaskTemplateExecutionService.Control(new UnityMcp.AgentTaskCore.AgentTaskControlRequest
            { Operation = "create", TaskType = AgentTaskTemplateExecutionService.TaskType, Payload = context.RawArgsJson });
            return new ToolResult { success = response.Success, status = response.Success ? ToolResultStatus.Success : ToolResultStatus.Failed,
                tool = Descriptor.Name, summary = response.ErrorMessage ?? "Template task admitted.",
                metricsObjectJson = new UnityMcp.AgentTaskCore.AgentTaskControlCodec().SerializeResponse(response) };
        }
    }
}
