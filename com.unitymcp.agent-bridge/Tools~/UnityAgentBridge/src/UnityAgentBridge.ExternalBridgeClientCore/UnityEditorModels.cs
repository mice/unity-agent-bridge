namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed record UnityEditorInstance(
    int ProcessId,
    string? ExecutablePath,
    string? ProjectPath,
    string? ProjectVersion,
    IReadOnlyList<string> Warnings);

public sealed record UnityEditorListRequest(
    bool IncludeBridgeHealth,
    string? ProjectPath);

public sealed record UnityEditorOpenRequest(
    string ProjectPath,
    string? UnityExecutablePath,
    bool AllowVersionFallback,
    bool WaitForBridge,
    int BridgeReadyTimeoutMs,
    int BridgePollIntervalMs,
    int? MaxRunningUnityEditors);

public sealed record UnityEditorOpenResponse(
    bool Success,
    string Status,
    string Code,
    string Summary,
    string ProjectPath,
    string? ProjectVersion,
    string? UnityExecutablePath,
    int? ProcessId,
    bool BridgeReady,
    string RecommendedAction,
    IReadOnlyList<string> Warnings,
    string? StatusPath = null,
    long? HeartbeatAgeMs = null);
