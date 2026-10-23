using System.Diagnostics;
using System.Text.Json;

namespace UnityAgentBridge.Mcp;

/// <summary>Process-start-bound evidence that a runtime serves more than one project.</summary>
public sealed class McpHostLease : IDisposable
{
    private readonly string path;
    private McpHostLease(string path) { this.path = path; }
    public static string Root => Path.Combine(Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityAgentBridge", "projects"), "hosts");
    public static McpHostLease Create(string connectionId)
    {
        Directory.CreateDirectory(Root);
        using var process = Process.GetCurrentProcess();
        var path = Path.Combine(Root, process.Id + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { schemaVersion = 1, processId = process.Id,
                processStartTime = process.StartTime.ToUniversalTime().ToString("o"), routingMode = "multi-project", connectionId }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(path);
    }
    public void Dispose() { if (File.Exists(path)) File.Delete(path); }
}
