using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace UnityAgentBridge.ExternalBridgeClientCore;

internal sealed class WindowsUnityEditorProcessDiscovery : IUnityEditorProcessDiscovery
{
    private static readonly Regex ProjectPathRegex = new(@"(?:^|\s)-projectPath\s+(?:""(?<quoted>[^""]+)""|(?<plain>\S+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<UnityEditorInstance> Discover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<UnityEditorInstance>();
        }

        var results = new List<UnityEditorInstance>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath, CommandLine, Name FROM Win32_Process WHERE Name = 'Unity.exe'");
        foreach (var process in searcher.Get().Cast<ManagementObject>())
        {
            var warnings = new List<string>();
            var processId = Convert.ToInt32(process["ProcessId"] ?? 0);
            var executablePath = process["ExecutablePath"]?.ToString();
            var commandLine = process["CommandLine"]?.ToString();
            var projectPath = TryParseProjectPath(commandLine, warnings);
            string? projectVersion = null;
            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
            {
                UnityEditorProjectVersionReader.TryReadVersion(projectPath, out projectVersion, out var versionCode);
                if (versionCode is not null)
                {
                    warnings.Add(versionCode);
                }
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                warnings.Add("ExecutablePathUnavailable");
            }

            results.Add(new UnityEditorInstance(processId, executablePath, projectPath, projectVersion, warnings));
        }

        return results;
    }

    internal static string? ParseProjectPathFromCommandLine(string? commandLine)
    {
        return TryParseProjectPath(commandLine, null);
    }

    private static string? TryParseProjectPath(string? commandLine, List<string>? warnings)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            warnings?.Add("CommandLineUnavailable");
            return null;
        }

        var match = ProjectPathRegex.Match(commandLine);
        if (!match.Success)
        {
            warnings?.Add("ProjectPathUnavailable");
            return null;
        }

        var value = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            warnings?.Add("ProjectPathUnavailable");
            return null;
        }

        try
        {
            return UnityEditorPathUtility.NormalizePath(value);
        }
        catch
        {
            warnings?.Add("ProjectPathInvalid");
            return value;
        }
    }

    public static bool TryStartUnity(string executablePath, string projectPath, out int? processId, out string? errorMessage)
    {
        processId = null;
        errorMessage = null;
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"-projectPath \"{projectPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
            });

            processId = process?.Id;
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            errorMessage = exception.Message;
            return false;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}
