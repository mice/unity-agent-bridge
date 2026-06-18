namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed record ToolResultEnvelope(string RawJson, string Status, bool IsUnknownStatus);
