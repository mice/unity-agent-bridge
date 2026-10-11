using System.Text;
using System.Text.Json;

namespace UnityAgentBridge.Mcp;

public sealed class McpStageLogger
{
    private const string LogVersion = "1.0";
    private const string SchemaVersion = "1.0";
    private static readonly object WriteGate = new();
    private readonly string _logPath;

    public McpStageLogger(string logPath)
    {
        _logPath = logPath;
    }

    public void Write(string stage, string commandId, string tool, string status, string message)
    {
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

        lock (WriteGate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(_logPath, JsonSerializer.Serialize(payload) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Stage logging is best effort. Another host may briefly own the
                // file; an unavailable diagnostic sink must not fail the tool or
                // replace its durable terminal result. The next write retries.
            }
        }
    }
}
