using System.Reflection;

namespace UnityAgentBridge.Mcp;

public static class RuntimeIdentity
{
    public const string ProtocolVersion = "1.0";

    public static string RuntimeVersion => ResolveVersion();

    public static string PackageVersion
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_PACKAGE_VERSION")?.Trim();
            return string.IsNullOrWhiteSpace(value) ? RuntimeVersion : value;
        }
    }

    public static string RuntimeMode
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_RUNTIME_MODE")?.Trim();
            return string.IsNullOrWhiteSpace(value) ? "machine-runtime" : value;
        }
    }

    private static string ResolveVersion()
    {
        var informational = typeof(RuntimeIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var separator = informational.IndexOf('+');
            return (separator >= 0 ? informational[..separator] : informational).Trim();
        }

        return "0.0.0-dev";
    }
}
