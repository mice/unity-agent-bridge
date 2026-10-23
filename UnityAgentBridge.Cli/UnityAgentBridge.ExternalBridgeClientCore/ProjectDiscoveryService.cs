using System.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class ProjectDiscoveryService : IProjectDiscovery
{
    private readonly IProjectRegistry _registry;
    private readonly Func<ProjectRegistration, SessionObservation> _read;
    private readonly Func<EditorSessionInfo, bool?> _alive;
    private readonly Func<DateTimeOffset> _now;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EditorSessionInfo> _lastKnown = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _observationGates = new(StringComparer.Ordinal);
    public ProjectDiscoveryService(IProjectRegistry registry, Func<ProjectRegistration, EditorSessionInfo?>? readSession = null, Func<EditorSessionInfo, bool?>? processAlive = null, Func<DateTimeOffset>? utcNow = null)
    {
        _registry = registry;
        _read = readSession is null ? ReadObservation : registration => new SessionObservation(readSession(registration));
        _alive = processAlive ?? ProcessAlive;
        _now = utcNow ?? (() => DateTimeOffset.UtcNow);
    }
    public TimeSpan HeartbeatBudget { get; init; } = TimeSpan.FromSeconds(10);
    public IReadOnlyList<ProjectSessionInfo> List(bool includeOffline) => List(_registry.List(), includeOffline);
    public IReadOnlyList<ProjectSessionInfo> List(IEnumerable<ProjectRegistration> registrations, bool includeOffline)
        => registrations.Select(Observe).Where(x => includeOffline || (x.editorSession?.bridgeEnabled == true && x.availability != "offline")).ToArray();
    public ProjectSessionInfo Observe(ProjectRegistration registration)
    {
        // The read belongs inside the same project gate as validation and persistence:
        // an earlier read must not race a newer observation into the revision cache.
        lock (_observationGates.GetOrAdd(registration.projectId, _ => new object()))
            return ObserveCore(registration);
    }
    private ProjectSessionInfo ObserveCore(ProjectRegistration registration)
    {
        var result = new ProjectSessionInfo { registration = registration, availability = "recovering", reason = "SESSION_UNAVAILABLE" };
        SessionObservation observation;
        try { observation = ReadWithRetry(registration); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        { return ObserveMissingStatus(result, "SESSION_UNREADABLE"); }
        var session = observation.Session;
        result.editorSession = session;
        if (session == null) return ObserveMissingStatus(result, "SESSION_UNAVAILABLE");
        if (session.schemaVersion != 1) { result.reason = "PROJECT_PROTOCOL_UNSUPPORTED"; return result; }
        if (session.projectId != registration.projectId || string.IsNullOrWhiteSpace(session.editorSessionId)) { result.reason = "SESSION_IDENTITY_CONFLICT"; return result; }
        if (LastKnown(registration, out _) is { } prior)
        {
            if (prior.editorSessionId == session.editorSessionId && session.revision < prior.revision)
            { result.editorSession = prior; result.reason = "SESSION_REVISION_REGRESSED"; return result; }
            if (prior.editorSessionId != session.editorSessionId && _alive(prior) == true)
            { result.reason = "SESSION_IDENTITY_CONFLICT"; return result; }
            if (prior.editorSessionId != session.editorSessionId && DateTimeOffset.TryParse(prior.processStartTime, out var priorStart) && DateTimeOffset.TryParse(session.processStartTime, out var currentStart) && currentStart < priorStart)
            { result.editorSession = prior; result.reason = "SESSION_REVISION_REGRESSED"; return result; }
        }
        var alive = _alive(session);
        if (alive == false) { result.availability = "offline"; result.reason = "EDITOR_EXITED"; return result; }
        if (!session.bridgeEnabled) { result.availability = "unavailable"; result.reason = "BRIDGE_DISABLED"; return result; }
        if (alive != true) { result.reason = "PROCESS_IDENTITY_UNKNOWN"; return result; }
        if (!DateTimeOffset.TryParse(session.heartbeatUtc, out var heartbeat) || _now() - heartbeat > HeartbeatBudget || heartbeat > _now().AddSeconds(1)) { result.reason = "HEARTBEAT_STALE"; return result; }
        _lastKnown[registration.projectId] = Newtonsoft.Json.JsonConvert.DeserializeObject<EditorSessionInfo>(Newtonsoft.Json.JsonConvert.SerializeObject(session))!;
        var persisted = true;
        if (_registry is ProjectRegistry diskRegistry)
        {
            try { diskRegistry.SaveObservedSession(registration, session); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException) { persisted = false; }
        }
        result.availability = "online";
        result.ready = true;
        result.reason = persisted ? "READY" : "READY_OBSERVATION_NOT_PERSISTED";
        if (observation.IsCompiling) { result.availability = "recovering"; result.ready = false; result.reason = "EDITOR_COMPILING"; }
        else if (observation.IsUpdating) { result.availability = "recovering"; result.ready = false; result.reason = "EDITOR_UPDATING"; }
        else if (observation.CurrentStage.Contains("reload", StringComparison.OrdinalIgnoreCase))
        { result.availability = "recovering"; result.ready = false; result.reason = "EDITOR_RELOADING"; }
        else if (observation.CurrentStage.Contains("shutting", StringComparison.OrdinalIgnoreCase) || observation.CurrentStage.Contains("stopp", StringComparison.OrdinalIgnoreCase))
        { result.availability = "unavailable"; result.ready = false; result.reason = "EDITOR_STOPPING"; }
        else if (!string.IsNullOrEmpty(observation.CurrentCommandId) && observation.CurrentStage is "unity.tool.start" or "unity.poller.pickup")
        { result.reason = "BRIDGE_BUSY"; } // Other tools may remain usable; this is not a Test Runner admission promise.
        return result;
    }
    public ProjectContext CreateCandidate(string projectPath, string? queueRoot = null, string? connectionId = null)
    {
        var canonical = ProjectIdentityService.Canonicalize(projectPath);
        var id = ProjectIdentityService.GetProjectId(canonical);
        var registration = _registry.Find(id) ?? new ProjectRegistration { projectId = id, projectPath = canonical, queueRoot = new QueuePaths(canonical, queueRoot ?? QueuePaths.DefaultQueueRoot).QueueRoot };
        var observed = Observe(registration);
        if (!observed.ready) throw new InvalidOperationException(observed.reason);
        return new ProjectContext(registration, observed.editorSession!, new QueuePaths(canonical, registration.queueRoot), connectionId);
    }
    public void PersistVerifiedObservation(ProjectRegistration registration, string editorSessionId)
    {
        lock (_observationGates.GetOrAdd(registration.projectId, _ => new object()))
        {
            if (_registry is ProjectRegistry registry && _lastKnown.TryGetValue(registration.projectId, out var session) && session.editorSessionId == editorSessionId)
                registry.SaveObservedSession(registration, session);
        }
    }
    private ProjectSessionInfo ObserveMissingStatus(ProjectSessionInfo result, string reason)
    {
        result.reason = reason;
        if (LastKnown(result.registration, out var observationError) is { } prior)
        {
            // A graceful Editor exit may remove Temp/status. Preserve the verified
            // instance and ask about that exact process rather than inferring exit
            // from file absence (or discarding the previously observed identity).
            result.editorSession = Newtonsoft.Json.JsonConvert.DeserializeObject<EditorSessionInfo>(Newtonsoft.Json.JsonConvert.SerializeObject(prior))!;
            if (prior.processId > 0 && DateTimeOffset.TryParse(prior.processStartTime, out _) && _alive(prior) == false) { result.availability = "offline"; result.reason = "EDITOR_EXITED"; }
        }
        else if (observationError != null) result.reason = observationError;
        return result;
    }
    private EditorSessionInfo? LastKnown(ProjectRegistration registration, out string? error)
    {
        error = null;
        if (_lastKnown.TryGetValue(registration.projectId, out var prior)) return prior;
        if (_registry is not ProjectRegistry registry) return null;
        try
        {
            prior = registry.ReadObservedSession(registration);
            if (prior != null) _lastKnown[registration.projectId] = prior;
            return prior;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = "SESSION_OBSERVATION_UNREADABLE"; }
        catch (Exception e) when (e is Newtonsoft.Json.JsonException or ArgumentException) { error = "SESSION_OBSERVATION_CORRUPT"; }
        return null;
    }

    private SessionObservation ReadWithRetry(ProjectRegistration registration)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return _read(registration); }
            catch (Exception e) when (IsTransientStatusAccess(e) && elapsed.ElapsedMilliseconds < 200)
            {
                // Retry only the current file. Historical identity never supplies readiness.
                System.Threading.Thread.Sleep((int)Math.Min(10, Math.Max(1, 200 - elapsed.ElapsedMilliseconds)));
            }
        }
    }
    private static bool IsTransientStatusAccess(Exception error)
        => OperatingSystem.IsWindows() && (error is IOException or UnauthorizedAccessException) &&
            (uint)error.HResult is 0x80070005u or 0x80070020u or 0x80070021u;

    private sealed record SessionObservation(EditorSessionInfo? Session, bool IsCompiling = false, bool IsUpdating = false,
        string CurrentStage = "", string CurrentCommandId = "");

    private static SessionObservation ReadObservation(ProjectRegistration registration)
    {
        var path = Path.Combine(registration.queueRoot, QueueLayout.StatusDirectoryName, QueueLayout.StatusFileName);
        byte[] bytes;
        try
        {
            // Do not use File.Exists: access/sharing failures must not become absence.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }
        catch (FileNotFoundException) { return new(null); }
        catch (DirectoryNotFoundException) { return new(null); }
        // Parse identity, root flags and string timestamps from precisely this read.
        using var snapshot = new MemoryStream(bytes, writable: false);
        using var text = new StreamReader(snapshot, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var reader = new Newtonsoft.Json.JsonTextReader(text) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        var json = JObject.Load(reader);
        bool Flag(string name)
        {
            var value = json[name];
            if (value is null || value.Type == JTokenType.Null) return false;
            if (value.Type != JTokenType.Boolean) throw new Newtonsoft.Json.JsonSerializationException("Invalid snapshot flag: " + name);
            return value.Value<bool>();
        }
        string Field(string name)
        {
            var value = json[name];
            if (value is null || value.Type == JTokenType.Null) return string.Empty;
            if (value.Type != JTokenType.String) throw new Newtonsoft.Json.JsonSerializationException("Invalid snapshot field: " + name);
            return value.Value<string>() ?? string.Empty;
        }
        return new(json["editorSession"]?.ToObject<EditorSessionInfo>(), Flag("isCompiling"), Flag("isUpdating"), Field("currentStage"), Field("currentCommandId"));
    }
    private static bool? ProcessAlive(EditorSessionInfo session)
    {
        if (!DateTimeOffset.TryParse(session.processStartTime, out var expected)) return null;
        try { using var process = Process.GetProcessById(session.processId); return !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - expected.UtcDateTime).TotalMilliseconds) < 1; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
