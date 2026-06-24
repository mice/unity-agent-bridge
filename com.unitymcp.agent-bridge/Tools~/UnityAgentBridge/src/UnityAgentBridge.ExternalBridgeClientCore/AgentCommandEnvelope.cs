using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public static class AgentCommandEnvelope
{
    public const string SchemaVersion = JsonUtil.CurrentSchemaVersion;

    public static string Build(string commandId, string tool, int timeoutMs, string argsJson)
    {
        try
        {
            return JsonUtil.BuildCommandEnvelope(commandId, tool, timeoutMs, argsJson);
        }
        catch (ArgumentException exception)
        {
            throw new BridgeCommandValidationException(exception.Message);
        }
    }
}
