namespace UnityMcp.AgentBridge
{
    public interface IAgentTool
    {
        ToolDescriptor Descriptor { get; }

        ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation);
    }

    public sealed class AgentToolContext
    {
        public AgentCommand Command { get; set; }

        public string RawArgsJson { get; set; }

        public AgentBridgeSettings Settings { get; set; }
    }
}
