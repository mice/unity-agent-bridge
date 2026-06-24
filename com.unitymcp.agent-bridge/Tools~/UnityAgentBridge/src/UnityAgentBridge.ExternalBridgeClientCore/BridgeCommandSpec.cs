namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed record BridgeCommandSpec(string Tool, int TimeoutMs, string ArgsJson);
