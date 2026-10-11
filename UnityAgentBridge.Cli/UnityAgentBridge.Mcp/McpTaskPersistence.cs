using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnityAgentBridge.Mcp;

internal sealed class McpTaskPersistence
{
    internal const long MaxRecordBytes = 4 * 1024 * 1024;
    internal string Root { get; }
    internal string ProjectPath { get; }
    private readonly string mutexName;

    internal McpTaskPersistence(string projectPath)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        Root = Path.Combine(ProjectPath, "Library", "AgentBridge");
        Directory.CreateDirectory(Root);
        mutexName = "UnityAgentBridge.McpTasks." + Hash(OperatingSystem.IsWindows() ? Root.ToUpperInvariant() : Root);
    }

    // All transactions are synchronous and short; no await may occur while held.
    internal IDisposable Enter(CancellationToken token = default)
    {
        var mutex = new Mutex(false, mutexName);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { if (mutex.WaitOne(100)) return new Lease(mutex); }
                catch (AbandonedMutexException) { return new Lease(mutex); }
            }
        }
        catch { mutex.Dispose(); throw; }
    }

    internal string DirectoryFor(string category)
    {
        var directory = Path.Combine(Root, category);
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal string PathFor(string category, string identity) => Path.Combine(DirectoryFor(category), Hash(identity) + ".json");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > MaxRecordBytes) throw new InvalidDataException("MCP_TASK_RECORD_TOO_LARGE: " + path);
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("MCP_TASK_RECORD_CORRUPT: " + path); }
        catch (JsonException exception) { throw new InvalidDataException("MCP_TASK_RECORD_CORRUPT: " + path, exception); }
    }

    internal static void Write<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.LongLength > MaxRecordBytes) throw new InvalidDataException("MCP_TASK_RECORD_TOO_LARGE: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal void ImportOnce(string category, Action<string, string> import)
    {
        using var lease = Enter();
        var directory = DirectoryFor(category);
        var marker = Path.Combine(directory, ".legacy-imported");
        if (File.Exists(marker)) return;
        var legacy = Path.Combine(ProjectPath, "Temp", "AgentBridge", category);
        if (Directory.Exists(legacy))
            foreach (var source in Directory.EnumerateFiles(legacy, "*.json"))
            {
                var destination = Path.Combine(directory, Path.GetFileName(source));
                if (!File.Exists(destination)) import(source, destination);
            }
        File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}

internal sealed record McpTaskPolicy
{
    internal TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);
    internal TimeSpan RecoveryWindow { get; init; } = TimeSpan.FromMinutes(5);
    internal TimeSpan DefaultExecutionWindow { get; init; } = TimeSpan.FromMinutes(15);
    internal TimeSpan ObservationBudget { get; init; } = TimeSpan.FromSeconds(5);
    internal int MaxTerminalRecords { get; init; } = 1000;
    internal int MaxActiveRecords { get; init; } = 128;
    internal long MaxStorageBytes { get; init; } = 256L * 1024 * 1024;
    internal long MaxReportBytes { get; init; } = 16L * 1024 * 1024;
}
