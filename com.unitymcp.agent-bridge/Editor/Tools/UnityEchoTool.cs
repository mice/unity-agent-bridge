namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.echo")]
    public sealed class UnityEchoTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.echo",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Return a static success response for bridge smoke tests.",
            AllowedModes = ToolExecutionModes.EditAndPlay,
            SideEffect = ToolSideEffect.None,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = null
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            return new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = "echo"
            };
        }
    }
}
