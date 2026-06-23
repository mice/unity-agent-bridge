namespace UnityAgentBridge.ExternalBridgeClientCore;

internal static class UnityEditorPathUtility
{
    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsUnityProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(path, "Assets")) &&
               File.Exists(Path.Combine(path, "ProjectSettings", "ProjectVersion.txt"));
    }

    public static bool IsUnityEditorExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "Unity.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Unity", StringComparison.OrdinalIgnoreCase);
    }
}
