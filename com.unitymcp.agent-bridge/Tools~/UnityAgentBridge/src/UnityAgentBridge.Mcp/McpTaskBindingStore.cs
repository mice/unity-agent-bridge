using System.Text.Json;

namespace UnityAgentBridge.Mcp;

public sealed record McpTaskBinding(string ProjectIdentity, string McpTaskId, string AgentTaskId, string? CommandId, int Version = 1);

public sealed class McpTaskBindingStore
{
    public const int CurrentVersion = 1;
    private readonly string _root;

    public McpTaskBindingStore(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("Project path is required.", nameof(projectPath));
        _root = Path.Combine(Path.GetFullPath(projectPath), "Temp", "AgentBridge", "mcp-task-mappings");
        Directory.CreateDirectory(_root);
    }

    public void Save(McpTaskBinding binding)
    {
        if (binding is null) throw new ArgumentNullException(nameof(binding));
        if (binding.Version != CurrentVersion || string.IsNullOrWhiteSpace(binding.ProjectIdentity) || string.IsNullOrWhiteSpace(binding.McpTaskId) || string.IsNullOrWhiteSpace(binding.AgentTaskId))
            throw new InvalidDataException("Invalid MCP task binding.");
        var path = PathFor(binding.ProjectIdentity, binding.McpTaskId);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(binding));
        File.Move(temp, path, true);
    }

    public McpTaskBinding? Load(string projectIdentity, string mcpTaskId)
    {
        var path = PathFor(projectIdentity, mcpTaskId);
        if (!File.Exists(path)) return null;
        try
        {
            var binding = JsonSerializer.Deserialize<McpTaskBinding>(File.ReadAllText(path));
            if (binding is null || binding.Version != CurrentVersion ||
                !string.Equals(binding.ProjectIdentity, projectIdentity, StringComparison.Ordinal) ||
                !string.Equals(binding.McpTaskId, mcpTaskId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(binding.AgentTaskId)) return null;
            return binding;
        }
        catch (JsonException)
        {
            return null;
        }
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

    private string PathFor(string projectIdentity, string mcpTaskId)
    {
        if (string.IsNullOrWhiteSpace(projectIdentity) || string.IsNullOrWhiteSpace(mcpTaskId)) throw new ArgumentException("Task identity is required.");
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectIdentity + "\n" + mcpTaskId))).ToLowerInvariant();
        return Path.Combine(_root, safe + ".json");
    }
}
