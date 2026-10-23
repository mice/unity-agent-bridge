using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.ExternalBridgeClientCore;

/// <summary>Project-local terminal history. Reads never dispatch a Unity command.</summary>
public sealed class BridgeResultHistory(QueuePaths paths, string? connectionId = null)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private string Root => Path.Combine(paths.ProjectPath, "Library", "AgentBridge", "command-history");
    private string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    private string FileFor(string id) => Path.Combine(Root, Key(id) + ".json");
    public ToolResultEnvelope? Read(string commandId)
    {
        var path = FileFor(commandId);
        try { File.GetAttributes(path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (UnauthorizedAccessException e) { throw new IOException("COMMAND_HISTORY_UNREADABLE: " + commandId, e); }
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("COMMAND_HISTORY_TOO_LARGE: " + commandId);
        JObject record;
        try { record = ParseObject(File.ReadAllText(path)); }
        catch (JsonException e) { throw new InvalidDataException("COMMAND_HISTORY_CORRUPT: " + commandId, e); }
        catch (UnauthorizedAccessException e) { throw new IOException("COMMAND_HISTORY_UNREADABLE: " + commandId, e); }
        if (record.Value<int>("version") != 1 || record.Value<string>("commandId") != commandId || !string.Equals(Path.GetFullPath(record.Value<string>("projectPath") ?? string.Empty), Path.GetFullPath(paths.ProjectPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("COMMAND_HISTORY_INCOMPATIBLE: " + commandId);
        if (record["payload"] is not JObject || record["expiresAt"] is null)
            throw new InvalidDataException("COMMAND_HISTORY_CORRUPT: " + commandId);
        if (record["expiresAt"]!.ToObject<DateTimeOffset>() <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("COMMAND_HISTORY_EXPIRED: " + commandId);
        var payload = (JObject)record["payload"]!;
        foreach (var report in payload.Descendants().OfType<JProperty>().Where(p => p.Name == "reportPath").ToArray())
        {
            var reportPath = report.Value.Type == JTokenType.String ? report.Value.Value<string>() : null;
            if (!string.IsNullOrWhiteSpace(reportPath) && !File.Exists(Path.GetFullPath(reportPath, paths.ProjectPath)))
                payload["reportRetention"] = new JObject { ["available"] = false, ["reason"] = "REPORT_MISSING" };
        }
        return new(payload.ToString(Formatting.None), record.Value<string>("status") ?? "completed", record.Value<bool>("isUnknownStatus"));
    }
    public ToolResultEnvelope Save(string commandId, ToolResultEnvelope result)
    {
        Directory.CreateDirectory(Root);
        var path = FileFor(commandId);
        // A terminal result is immutable even if an observation arrives later.
        if (File.Exists(path)) return Read(commandId)!;
        var payload = ParseObject(result.RawJson);
        if (paths.TargetProjectId is not null)
        {
            payload["projectPath"] = paths.ProjectPath;
            payload["projectId"] = paths.TargetProjectId;
            payload["editorSessionId"] = paths.TargetEditorSessionId;
            payload["connectionId"] ??= connectionId;
        }
        foreach (var report in payload.Descendants().OfType<JProperty>().Where(p => p.Name == "reportPath").ToArray())
        {
            if (report.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace(report.Value.Value<string>())) continue;
            try
            {
                var source = Path.GetFullPath(report.Value.Value<string>()!, paths.ProjectPath);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!source.StartsWith(Path.GetFullPath(paths.ProjectPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                    throw new IOException("REPORT_OUTSIDE_PROJECT");
                if (!File.Exists(source)) throw new IOException("REPORT_MISSING");
                if (new FileInfo(source).Length > 16 * 1024 * 1024) throw new IOException("REPORT_TOO_LARGE");
                // Use one complete digest for a structured pair, keeping retained reports readable
                // by Unity's Mono file APIs in project paths where two digests exceed MAX_PATH.
                var reportIdentity = JsonConvert.SerializeObject(new[] { commandId, source });
                var retained = Path.Combine(Root, "r-" + Key(reportIdentity) + ".json");
                AtomicWrite(retained, File.ReadAllText(source));
                report.Value = Path.GetRelativePath(paths.ProjectPath, retained).Replace('\\', '/');
                payload["reportRetention"] = new JObject { ["available"] = true, ["owned"] = true };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { payload["reportRetention"] = new JObject { ["available"] = false, ["reason"] = e.Message }; }
        }
        var record = new JObject { ["version"] = 1, ["commandId"] = commandId, ["projectPath"] = paths.ProjectPath,
            ["queueRoot"] = paths.QueueRoot, ["expiresAt"] = DateTimeOffset.UtcNow + Retention, ["status"] = result.Status, ["isUnknownStatus"] = result.IsUnknownStatus, ["payload"] = payload };
        var contents = record.ToString(Formatting.None);
        if (Encoding.UTF8.GetByteCount(contents) > 4 * 1024 * 1024) throw new InvalidDataException("COMMAND_HISTORY_TOO_LARGE: " + commandId);
        AtomicWrite(path, contents);
        Cleanup();
        return new(payload.ToString(Formatting.None), result.Status, result.IsUnknownStatus);
    }
    /// <summary>Reads only bridge-owned retained reports in the original project, without Unity readiness.</summary>
    public string ReadRetainedReport(string reportPath)
    {
        var absolute = Path.GetFullPath(reportPath, Path.GetFullPath(paths.ProjectPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var allowed = new[] { Root, Path.Combine(paths.ProjectPath, "Library", "AgentBridge", "mcp-task-reports"),
            Path.Combine(paths.ProjectPath, "Library", "AgentBridge", "editor-test-reports") }
            .Select(Path.GetFullPath).Any(root => absolute.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison));
        if (!allowed) throw new InvalidOperationException("TASK_PROJECT_CONFLICT: Report is outside this project's retained history.");
        try
        {
            var info = new FileInfo(absolute);
            if (!info.Exists) throw new FileNotFoundException("REPORT_MISSING: " + reportPath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > Retention) throw new InvalidDataException("REPORT_EXPIRED: " + reportPath);
            if (info.Length > 16 * 1024 * 1024) throw new InvalidDataException("REPORT_TOO_LARGE: " + reportPath);
            return File.ReadAllText(absolute);
        }
        catch (UnauthorizedAccessException e) { throw new IOException("REPORT_UNREADABLE: " + reportPath, e); }
    }
    private void Cleanup()
    {
        var files = new DirectoryInfo(Root).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long bytes = 0;
        for (var i = 0; i < files.Length; i++)
        {
            bytes += files[i].Length;
            if (DateTime.UtcNow - files[i].LastWriteTimeUtc > Retention || i >= 2000 || bytes > 256L * 1024 * 1024) files[i].Delete();
        }
    }
    private static JObject ParseObject(string json)
    {
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }
    private static void AtomicWrite(string path, string contents)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(contents);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path)) { }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
