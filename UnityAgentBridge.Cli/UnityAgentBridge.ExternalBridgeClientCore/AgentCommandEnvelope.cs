using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public static class AgentCommandEnvelope
{
    public const string SchemaVersion = JsonUtil.CurrentSchemaVersion;

    public static string Build(string commandId, string tool, int timeoutMs, string argsJson, int targetSchemaVersion = 0, string? projectId = null, string? editorSessionId = null)
    {
        try
        {
            return JsonUtil.BuildCommandEnvelope(commandId, tool, timeoutMs, argsJson, targetSchemaVersion: targetSchemaVersion, projectId: projectId, editorSessionId: editorSessionId);
        }
        catch (ArgumentException exception)
        {
            throw new BridgeCommandValidationException(exception.Message);
        }
    }
}
