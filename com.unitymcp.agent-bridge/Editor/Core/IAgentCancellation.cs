namespace UnityMcp.AgentBridge
{
    public interface IAgentCancellation
    {
        bool IsCancellationRequested { get; }

        void ThrowIfCancellationRequested();
    }
}
