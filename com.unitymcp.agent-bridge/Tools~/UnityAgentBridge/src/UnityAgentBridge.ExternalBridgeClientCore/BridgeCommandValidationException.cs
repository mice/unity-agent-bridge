namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class BridgeCommandValidationException : Exception
{
    public BridgeCommandValidationException(string message)
        : base(message)
    {
    }
}
