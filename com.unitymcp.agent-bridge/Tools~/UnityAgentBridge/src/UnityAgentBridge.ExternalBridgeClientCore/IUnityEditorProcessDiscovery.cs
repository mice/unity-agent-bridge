namespace UnityAgentBridge.ExternalBridgeClientCore;

internal interface IUnityEditorProcessDiscovery
{
    IReadOnlyList<UnityEditorInstance> Discover();
}
