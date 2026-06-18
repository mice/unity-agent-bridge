using System;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.compile")]
    public sealed class UnityCompileTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.compile",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Request a Unity script compilation and report the result.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.MutatesProject,
            MayTriggerDomainReload = true,
            ArgsSchemaPath = "Documentation~/schemas/unity.compile.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<UnityCompileArgs>(context.RawArgsJson, out _, out var failure))
            {
                return failure;
            }

            return UnityCompileOperationManager.StartOrResume(context.Command, context.Settings);
        }
    }

    [Serializable]
    public sealed class UnityCompileArgs
    {
    }
}
