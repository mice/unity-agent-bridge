using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public static class ProjectIdentityService
{
    public static string Canonicalize(string projectPath) => ProjectPathIdentity.Canonicalize(projectPath);
    public static string GetProjectId(string projectPath) => ProjectPathIdentity.GetProjectId(projectPath);
}
