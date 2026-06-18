using System.Reflection;

namespace UnityAgentBridge.Mcp;

public sealed record McpHostDiagnostics(
    string ResolvedCliPath,
    string CliMode,
    IReadOnlyList<string> CliWarnings,
    string ProjectPath,
    string QueueRoot,
    string LogsDirectory,
    string ServerLogPath)
{
    public static McpHostDiagnostics Resolve(string? explicitProjectPath = null)
    {
        var projectPath = ResolveProjectPath(explicitProjectPath);
        var queueRoot = Path.Combine(projectPath, "Temp", "AgentBridge");
        var logsDirectory = Path.Combine(queueRoot, "logs");
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
        var repoRoot = FindRepoRoot(assemblyDirectory);
        var packageBinary = repoRoot is null
            ? string.Empty
            : Path.Combine(repoRoot, "com.unitymcp.agent-bridge", "Tools~", "UnityAgentBridge", "cli", "out", CurrentRid(), CurrentExecutableName());

        var resolvedCliPath = packageBinary;
        var cliMode = "package-binary";
        var cliWarnings = Array.Empty<string>();

        return new McpHostDiagnostics(
            resolvedCliPath,
            cliMode,
            cliWarnings,
            projectPath,
            queueRoot,
            logsDirectory,
            Path.Combine(logsDirectory, "mcp-server.log"));
    }

    private static string ResolveProjectPath(string? explicitProjectPath)
    {
        var envProjectPath = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_PROJECT_PATH");
        var candidate = string.IsNullOrWhiteSpace(explicitProjectPath) ? envProjectPath : explicitProjectPath;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var fullPath = Path.GetFullPath(candidate);
            if (IsUnityProject(fullPath))
            {
                return fullPath;
            }
        }

        foreach (var path in EnumerateCandidateRoots())
        {
            if (IsUnityProject(path))
            {
                return path;
            }

            var unityMcpPath = Path.Combine(path, "UnityMCP");
            if (IsUnityProject(unityMcpPath))
            {
                return unityMcpPath;
            }
        }

        throw new DirectoryNotFoundException("Unable to auto-detect a Unity project. Set UNITY_AGENT_BRIDGE_PROJECT_PATH explicitly.");
    }

    private static IEnumerable<string> EnumerateCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var cursor = Directory.GetCurrentDirectory(); !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (seen.Add(cursor))
            {
                yield return cursor;
            }
        }

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        for (var cursor = assemblyDirectory; !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (seen.Add(cursor))
            {
                yield return cursor;
            }
        }
    }

    private static string? FindRepoRoot(string startPath)
    {
        for (var cursor = startPath; !string.IsNullOrWhiteSpace(cursor); cursor = Directory.GetParent(cursor)?.FullName)
        {
            if (Directory.Exists(Path.Combine(cursor, ".git")) || Directory.Exists(Path.Combine(cursor, "openspec")))
            {
                return cursor;
            }
        }

        return null;
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

    private static string CurrentRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx-arm64";
        }

        return "linux-x64";
    }

    private static string CurrentExecutableName()
    {
        return OperatingSystem.IsWindows() ? "unity-agent-bridge.exe" : "unity-agent-bridge";
    }
}
