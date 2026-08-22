namespace UnityAgentBridge.Mcp;

internal static class McpServerInstructions
{
    internal const string Value = """
        You are connected to a Unity Editor through Unity MCP.

        When a request depends on the actual state of the Unity project, Editor,
        scene, prefab, assets, tests, build, or runtime, use available Unity MCP
        inspection capabilities to observe the real state instead of making
        unsupported assumptions.

        For diagnostic tasks, prefer:

        Inspect -> Reason -> Act -> Verify

        Guidelines:

        - When relevant Unity state can be directly observed, prefer observation
          over unsupported inference.

        - Do not ask the user for information that can be obtained directly
          through available Unity MCP inspection capabilities.

        - Read-only inspection may be used proactively when it helps reduce
          uncertainty relevant to the request.

        - After changing Unity state, verify the resulting state when verification
          is relevant and an appropriate inspection capability is available.

        - Follow applicable client or host policies and the user's explicit
          constraints. Requesting approval required by such a policy is not an
          unnecessary request for information. ServerInstructions does not define
          or enforce approval.
        """;
}
