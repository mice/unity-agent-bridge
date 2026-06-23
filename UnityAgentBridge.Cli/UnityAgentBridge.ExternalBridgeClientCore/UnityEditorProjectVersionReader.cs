namespace UnityAgentBridge.ExternalBridgeClientCore;

internal static class UnityEditorProjectVersionReader
{
    public static bool TryReadVersion(string projectPath, out string? version, out string? code)
    {
        version = null;
        code = null;

        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
        {
            code = "ProjectVersionMissing";
            return false;
        }

        foreach (var line in File.ReadLines(versionFile))
        {
            const string prefix = "m_EditorVersion:";
            if (!line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            version = line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return true;
            }
        }

        code = "ProjectVersionInvalid";
        return false;
    }
}
