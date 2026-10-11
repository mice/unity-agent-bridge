using System.Text.Json;

namespace UnityAgentBridge.Mcp;

public sealed record McpTaskBinding(string ProjectIdentity, string McpTaskId, string AgentTaskId, string? CommandId, int Version = 1);

public sealed class McpTaskBindingStore
{
    public const int CurrentVersion = 1;
    private readonly string _root;
    private readonly McpTaskPersistence _persistence;

    public McpTaskBindingStore(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("Project path is required.", nameof(projectPath));
        _persistence = new McpTaskPersistence(projectPath);
        _root = _persistence.DirectoryFor("mcp-task-mappings");
        _persistence.ImportOnce("mcp-task-mappings", (source, destination) => File.Copy(source, destination));
    }

    public void Save(McpTaskBinding binding)
    {
        if (binding is null) throw new ArgumentNullException(nameof(binding));
        if (binding.Version != CurrentVersion || string.IsNullOrWhiteSpace(binding.ProjectIdentity) || string.IsNullOrWhiteSpace(binding.McpTaskId) || string.IsNullOrWhiteSpace(binding.AgentTaskId))
            throw new InvalidDataException("Invalid MCP task binding.");
        using var lease = _persistence.Enter();
        McpTaskPersistence.Write(PathFor(binding.ProjectIdentity, binding.McpTaskId), binding);
    }

    public McpTaskBinding? Load(string projectIdentity, string mcpTaskId)
    {
        var path = PathFor(projectIdentity, mcpTaskId);
        using var lease = _persistence.Enter();
        var binding = McpTaskPersistence.Read<McpTaskBinding>(path);
        if (binding is null) return null;
        if (binding.Version != CurrentVersion) throw new InvalidDataException("MCP_TASK_BINDING_VERSION: " + path);
        if (
                !string.Equals(binding.ProjectIdentity, projectIdentity, StringComparison.Ordinal) ||
                !string.Equals(binding.McpTaskId, mcpTaskId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(binding.AgentTaskId)) return null;
        return binding;
    }

    public McpTaskBinding? FindByMcpTaskId(string mcpTaskId)
    {
        if (string.IsNullOrWhiteSpace(mcpTaskId)) throw new ArgumentException("Task identity is required.", nameof(mcpTaskId));
        foreach (var path in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var binding = JsonSerializer.Deserialize<McpTaskBinding>(File.ReadAllText(path));
                if (binding is not null && binding.Version == CurrentVersion && string.Equals(binding.McpTaskId, mcpTaskId, StringComparison.Ordinal))
                    return binding;
            }
            catch (JsonException)
            {
                // Ignore an incomplete or corrupt mapping while looking for an identity conflict.
            }
        }

        return null;
    }

    internal void Delete(string projectIdentity, string mcpTaskId)
    {
        using var lease = _persistence.Enter();
        File.Delete(PathFor(projectIdentity, mcpTaskId));
    }

    private string PathFor(string projectIdentity, string mcpTaskId)
    {
        if (string.IsNullOrWhiteSpace(projectIdentity) || string.IsNullOrWhiteSpace(mcpTaskId)) throw new ArgumentException("Task identity is required.");
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectIdentity + "\n" + mcpTaskId))).ToLowerInvariant();
        return Path.Combine(_root, safe + ".json");
    }
}
