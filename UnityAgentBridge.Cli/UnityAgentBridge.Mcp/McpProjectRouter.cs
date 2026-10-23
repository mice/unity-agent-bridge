using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

public sealed record McpRoutingOptions(string RoutingMode, string WorkspaceRoot)
{
    public bool MultiProject => RoutingMode == "multi-project";
    public static McpRoutingOptions Parse(string[]? arguments = null)
    {
        var mode = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_ROUTING_MODE") ?? "single-project";
        var workspace = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_WORKSPACE_PATH") ?? Directory.GetCurrentDirectory();
        var args = arguments ?? Array.Empty<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--routing-mode" or "--workspace-root")
            {
                var name = args[i];
                if (++i >= args.Length) throw new ArgumentException("Missing value for " + name);
                if (name == "--routing-mode") mode = args[i]; else workspace = args[i];
            }
            else throw new ArgumentException("Unknown MCP server option: " + args[i]);
        }
        if (mode is not ("single-project" or "multi-project")) throw new ArgumentException("Unsupported routing mode: " + mode);
        return new(mode, Path.GetFullPath(workspace));
    }
}

public sealed class McpProjectRoutingException(string code, string message, IReadOnlyList<string>? candidates = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Candidates { get; } = candidates ?? Array.Empty<string>();
}

public sealed class McpProjectRouter
{
    private readonly IProjectRegistry registry;
    private readonly Func<string, ProjectContext> createCandidate;
    public McpProjectRouter(McpRoutingOptions options, IProjectRegistry registry, Func<string, ProjectContext> createCandidate)
    { Options = options; this.registry = registry; this.createCandidate = createCandidate; }
    public McpRoutingOptions Options { get; }
    public string ResolvePath(string? projectPath, string? projectId)
    {
        string path;
        if (!string.IsNullOrWhiteSpace(projectPath)) path = Path.GetFullPath(projectPath, Options.WorkspaceRoot);
        else
        {
            path = NearestProject(Options.WorkspaceRoot) ?? string.Empty;
            if (path.Length == 0)
            {
                var candidates = registry.List().Where(r => Within(r.projectPath, Options.WorkspaceRoot)).Select(r => r.projectPath).Distinct().ToArray();
                if (candidates.Length == 0) throw new McpProjectRoutingException("PROJECT_NOT_RESOLVED", "No Unity project resolved from the connection workspace.");
                if (candidates.Length > 1) throw new McpProjectRoutingException("PROJECT_AMBIGUOUS", "More than one registered project belongs to the connection workspace.", candidates);
                path = candidates[0];
            }
        }
        try { path = ProjectIdentityService.Canonicalize(path); }
        catch (Exception e) when (e is IOException or ArgumentException) { throw new McpProjectRoutingException("PROJECT_PATH_INVALID", e.Message); }
        if (!string.IsNullOrWhiteSpace(projectId) && ProjectIdentityService.GetProjectId(path) != projectId)
            throw new McpProjectRoutingException(string.IsNullOrWhiteSpace(projectPath) ? "PROJECT_PATH_REQUIRED" : "PROJECT_ID_MISMATCH", "The project ID does not identify the selected path.");
        return path;
    }
    public ProjectContext Resolve(string? projectPath, string? projectId)
    {
        var path = ResolvePath(projectPath, projectId);
        try { return createCandidate(path); }
        catch (McpProjectRoutingException) { throw; }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException)
        { throw new McpProjectRoutingException("PROJECT_UNAVAILABLE", e.Message, new[] { path }); }
    }
    internal static bool IsHostTool(string name) => name is "mcp_echo" or "unity_editor_list" or "unity_editor_open" or "unity_projects_list";
    internal static JsonObject BusinessArguments(JsonObject arguments)
    {
        var copy = (JsonObject)arguments.DeepClone(); copy.Remove("projectPath"); copy.Remove("projectId"); return copy;
    }
    private static bool Within(string path, string root)
    {
        path = path.Replace('\\', '/').TrimEnd('/'); root = root.Replace('\\', '/').TrimEnd('/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(path, root, comparison) || path.StartsWith(root + "/", comparison);
    }
    private static string? NearestProject(string root)
    {
        for (var cursor = root; cursor is not null; cursor = Directory.GetParent(cursor)?.FullName)
            if (File.Exists(Path.Combine(cursor, "ProjectSettings", "ProjectVersion.txt")) && Directory.Exists(Path.Combine(cursor, "Assets"))) return cursor;
        return null;
    }
}
