using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.ExternalBridgeClientCore;

internal static class UnityEditorLaunchSettings
{
    public const int DefaultMaxRunningEditors = 3;
    public const int DefaultBridgeReadyTimeoutMs = 120000;
    public const int DefaultBridgePollIntervalMs = 1000;
    public const string MaxRunningEditorsEnvironmentVariable = "UNITY_AGENT_BRIDGE_MAX_RUNNING_EDITORS";
    public const string LauncherConfigEnvironmentVariable = "MCP_LAUNCHER_CONFIG";

    public static (int Limit, List<string> Warnings) ResolveMaxRunningEditors(int? overrideValue)
    {
        var warnings = new List<string>();
        if (overrideValue.HasValue)
        {
            if (overrideValue.Value <= 0)
            {
                throw new BridgeCommandValidationException("maxRunningUnityEditors must be greater than 0.");
            }

            return (overrideValue.Value, warnings);
        }

        if (TryParsePositiveInt(Environment.GetEnvironmentVariable(MaxRunningEditorsEnvironmentVariable), out var envValue))
        {
            return (envValue, warnings);
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MaxRunningEditorsEnvironmentVariable)))
        {
            warnings.Add("Ignored invalid UNITY_AGENT_BRIDGE_MAX_RUNNING_EDITORS value.");
        }

        var launcherConfigPath = Environment.GetEnvironmentVariable(LauncherConfigEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(launcherConfigPath) || !File.Exists(launcherConfigPath))
        {
            return (DefaultMaxRunningEditors, warnings);
        }

        try
        {
            var raw = JObject.Parse(File.ReadAllText(launcherConfigPath));
            if (TryParsePositiveInt(raw["maxRunningUnityEditors"]?.ToString(), out var configValue))
            {
                return (configValue, warnings);
            }

            if (raw["maxRunningUnityEditors"] is not null)
            {
                warnings.Add($"Ignored invalid maxRunningUnityEditors in launcher config '{launcherConfigPath}'.");
            }
        }
        catch
        {
            warnings.Add($"Ignored unreadable launcher config '{launcherConfigPath}'.");
        }

        return (DefaultMaxRunningEditors, warnings);
    }

    private static bool TryParsePositiveInt(string? raw, out int value)
    {
        value = 0;
        return int.TryParse(raw, out value) && value > 0;
    }
}
