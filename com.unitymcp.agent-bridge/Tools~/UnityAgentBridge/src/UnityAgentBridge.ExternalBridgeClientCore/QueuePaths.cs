using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class QueuePaths
{
    public const string DefaultQueueRoot = QueueLayout.DefaultQueueRoot;

    public QueuePaths(string projectPath, string queueRoot)
    {
        ProjectPath = projectPath;
        QueueRoot = QueueLayout.ResolveRelativePath(projectPath, queueRoot);
        InboxDirectory = Path.Combine(QueueRoot, QueueLayout.InboxDirectoryName);
        ProcessingDirectory = Path.Combine(QueueRoot, QueueLayout.ProcessingDirectoryName);
        OutboxDirectory = Path.Combine(QueueRoot, QueueLayout.OutboxDirectoryName);
        StatusDirectory = Path.Combine(QueueRoot, QueueLayout.StatusDirectoryName);
        LogsDirectory = Path.Combine(QueueRoot, QueueLayout.LogsDirectoryName);
    }

    public string ProjectPath { get; }

    public string QueueRoot { get; }

    public string InboxDirectory { get; }

    public string ProcessingDirectory { get; }

    public string OutboxDirectory { get; }

    public string StatusDirectory { get; }

    public string LogsDirectory { get; }

    public static string ResolveProjectPath(string? explicitProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            var candidate = Path.GetFullPath(explicitProjectPath);
            if (!IsUnityProject(candidate))
            {
                throw new DirectoryNotFoundException($"Unity project not found at '{candidate}'.");
            }

            return candidate;
        }

        var current = Directory.GetCurrentDirectory();
        for (var cursor = current; !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (IsUnityProject(cursor))
            {
                return cursor;
            }

            var unityMcpCandidate = Path.Combine(cursor, "UnityMCP");
            if (IsUnityProject(unityMcpCandidate))
            {
                return unityMcpCandidate;
            }

            foreach (var child in Directory.GetDirectories(cursor))
            {
                if (IsUnityProject(child))
                {
                    return child;
                }
            }
        }

        throw new DirectoryNotFoundException("Unable to auto-detect a Unity project. Use --project-path.");
    }

    private static bool IsUnityProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt")) &&
               Directory.Exists(Path.Combine(path, "Assets"));
    }
}
