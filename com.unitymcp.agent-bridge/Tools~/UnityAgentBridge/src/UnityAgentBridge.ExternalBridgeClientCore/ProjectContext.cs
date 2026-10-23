using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class ProjectContext
{
    public ProjectContext(ProjectRegistration registration, EditorSessionInfo session, QueuePaths paths, string? connectionId = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(session);
        if (registration.schemaVersion != 1 || session.schemaVersion != 1) throw new ArgumentException("PROJECT_PROTOCOL_UNSUPPORTED");
        if (registration.projectId != session.projectId) throw new ArgumentException("Project/session identity mismatch.");
        ProjectId = registration.projectId;
        ProjectPath = registration.projectPath;
        EditorSessionId = session.editorSessionId;
        CapabilityRevision = session.capabilityRevision;
        ConnectionId = connectionId;
        QueuePaths = new QueuePaths(paths.ProjectPath, paths.QueueRoot, ProjectId, EditorSessionId);
    }
    public string ProjectId { get; }
    public string ProjectPath { get; }
    public string EditorSessionId { get; }
    public string CapabilityRevision { get; }
    public string? ConnectionId { get; }
    public QueuePaths QueuePaths { get; }
}

public interface IProjectRegistry
{
    IReadOnlyList<ProjectRegistration> List();
    ProjectRegistration? Find(string projectId);
    void RegisterSuccessfulConnection(ProjectRegistration registration, EditorSessionInfo session);
}

public interface IProjectDiscovery
{
    ProjectSessionInfo Observe(ProjectRegistration registration);
    IReadOnlyList<ProjectSessionInfo> List(bool includeOffline);
}
