using System.Text;
using System.Text.Json;

namespace UnityAgentBridge.Mcp;

public sealed class McpStageLogger
{
    private const string LogVersion = "1.0";
    private const string SchemaVersion = "1.0";
    private readonly string _logPath;

    public McpStageLogger(string logPath)
    {
        _logPath = logPath;
    }

    public void Write(string stage, string commandId, string tool, string status, string message)
    {
        var directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new
        {
            logVersion = LogVersion,
            schemaVersion = SchemaVersion,
            timestamp = DateTime.UtcNow.ToString("O"),
            stage,
            commandId,
            tool,
            status,
            message
        };

        File.AppendAllText(_logPath, JsonSerializer.Serialize(payload) + Environment.NewLine, Encoding.UTF8);
    }
}
