using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    public interface IUnityToolFacade
    {
        ToolResult Execute(AgentCommand command, IAgentCancellation cancellation);

        IReadOnlyList<ToolDescriptor> ListTools();

        bool TryGetTool(string toolName, out IAgentTool tool);
    }
}
