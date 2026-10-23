using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentBridge;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class ProjectRegistry : IProjectRegistry
{
    public ProjectRegistry(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_REGISTRY_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityAgentBridge", "projects");
    }
    public string Root { get; }
    public IReadOnlyList<ProjectRegistration> List() => ReadSnapshot().Registrations;
    public IReadOnlyList<ProjectRegistrationIssue> ListDiagnostics() => ReadSnapshot().Diagnostics;
    public ProjectRegistrySnapshot ReadSnapshot()
    {
        var records = new List<ProjectRegistration>();
        var issues = new List<ProjectRegistrationIssue>();
        if (!Directory.Exists(Root)) return new(records, issues);
        foreach (var path in Directory.EnumerateFiles(Root, "project-*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            void Issue(string reason) => issues.Add(new(id, path, reason));
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) { Issue("REGISTRATION_TOO_LARGE"); continue; }
                using var text = new StringReader(File.ReadAllText(path));
                using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
                var json = JObject.Load(reader);
                var version = json["schemaVersion"];
                if (version?.Type != JTokenType.Integer) { Issue("REGISTRATION_CORRUPT"); continue; }
                if (version.ToString() != "1") { Issue("REGISTRATION_UNSUPPORTED"); continue; }
                var item = json.ToObject<ProjectRegistration>();
                if (item is null || item.projectId != id || string.IsNullOrWhiteSpace(item.projectPath) ||
                    !Path.IsPathFullyQualified(item.projectPath) || string.IsNullOrWhiteSpace(item.queueRoot) || !Path.IsPathFullyQualified(item.queueRoot))
                { Issue("REGISTRATION_IDENTITY_INVALID"); continue; }
                records.Add(item);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Issue("REGISTRATION_UNREADABLE"); }
            catch (Exception e) when (e is JsonException or ArgumentException) { Issue("REGISTRATION_CORRUPT"); }
        }
        return new(records.OrderBy(x => x.projectPath, StringComparer.Ordinal).ToArray(), issues);
    }
    internal EditorSessionInfo? ReadObservedSession(ProjectRegistration registration)
    {
        var path = ObservationPath(registration.projectId);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 1024 * 1024) throw new JsonSerializationException("Observation is too large.");
        using var text = new StringReader(File.ReadAllText(path));
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        var json = JObject.Load(reader);
        if (json["schemaVersion"]?.Type != JTokenType.Integer || json["schemaVersion"]!.ToString() != "1") throw new JsonSerializationException("Unsupported observation schema.");
        var session = json["editorSession"]?.ToObject<EditorSessionInfo>();
        if (session?.schemaVersion != 1 || session.projectId != registration.projectId ||
            string.IsNullOrWhiteSpace(session.editorSessionId) || !session.bridgeEnabled || session.processId <= 0 ||
            !DateTimeOffset.TryParse(session.processStartTime, out _)) throw new JsonSerializationException("Invalid observed instance identity.");
        return session;
    }
    internal void SaveObservedSession(ProjectRegistration registration, EditorSessionInfo session)
    {
        // Observation is evidence, never registration. Explicit candidate paths stay unregistered.
        if (Find(registration.projectId) is null || session.processId <= 0 || !DateTimeOffset.TryParse(session.processStartTime, out _)) return;
        var target = ObservationPath(registration.projectId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonConvert.SerializeObject(new { schemaVersion = 1, editorSession = session }));
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private string ObservationPath(string projectId)
    {
        if (Path.GetFileName(projectId) != projectId || !projectId.StartsWith("project-", StringComparison.Ordinal) || projectId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid project identity.");
        return Path.Combine(Root, "observed-sessions", projectId + ".json");
    }
    public ProjectRegistration? Find(string projectId) => List().SingleOrDefault(x => x.projectId == projectId);
    public void RegisterSuccessfulConnection(ProjectRegistration registration, EditorSessionInfo session)
    {
        if (registration.schemaVersion != 1 || session.schemaVersion != 1 || registration.projectId != session.projectId || !session.bridgeEnabled || string.IsNullOrWhiteSpace(session.editorSessionId)) throw new ArgumentException("A matching enabled session is required.");
        var canonical = ProjectIdentityService.Canonicalize(registration.projectPath);
        if (ProjectIdentityService.GetProjectId(canonical) != registration.projectId) throw new ArgumentException("Project identity mismatch.");
        var existing = Find(registration.projectId);
        var copy = new ProjectRegistration { projectId = registration.projectId, projectPath = canonical, queueRoot = Path.GetFullPath(registration.queueRoot), registeredAt = existing?.registeredAt ?? DateTime.UtcNow.ToString("O"), lastConnectedAt = DateTime.UtcNow.ToString("O") };
        Directory.CreateDirectory(Root);
        var target = Path.Combine(Root, copy.projectId + ".json");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonConvert.SerializeObject(copy)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        SaveObservedSession(copy, session);
    }
}

public sealed record ProjectRegistrySnapshot(IReadOnlyList<ProjectRegistration> Registrations, IReadOnlyList<ProjectRegistrationIssue> Diagnostics);

public sealed record ProjectRegistrationIssue(
    [property: JsonProperty("projectId")] string ProjectId,
    [property: JsonProperty("recordPath")] string RecordPath,
    [property: JsonProperty("reason")] string Reason)
{
    [JsonProperty("availability")] public string Availability => "unavailable";
    [JsonProperty("ready")] public bool Ready => false;
}
