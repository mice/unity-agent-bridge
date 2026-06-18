using System;
using System.IO;
using System.Text;

namespace UnityMcp.AgentBridge
{
    public sealed class FileAgentBridgeLogger
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        public const string CurrentLogVersion = "1.0";
        private readonly string _logPath;

        public FileAgentBridgeLogger(string logPath)
        {
            _logPath = logPath ?? throw new ArgumentNullException(nameof(logPath));
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void Info(string eventName, string message)
        {
            Write("info", eventName, message, string.Empty, string.Empty, JsonUtil.CurrentSchemaVersion);
        }

        public void Warning(string eventName, string message)
        {
            Write("warning", eventName, message, string.Empty, string.Empty, JsonUtil.CurrentSchemaVersion);
        }

        public void Error(string eventName, string message)
        {
            Write("error", eventName, message, string.Empty, string.Empty, JsonUtil.CurrentSchemaVersion);
        }

        public void Exception(string eventName, Exception exception)
        {
            Write("error", eventName, exception == null ? "unknown exception" : exception.ToString(), string.Empty, string.Empty, JsonUtil.CurrentSchemaVersion);
        }

        public void Stage(string stage, string commandId, string tool, string status, string message)
        {
            Write("info", stage, message, commandId, tool, JsonUtil.CurrentSchemaVersion, status);
        }

        private void Write(string level, string eventName, string message, string commandId, string tool, string schemaVersion, string status = "")
        {
            var line = "{\"timestamp\":\"" + DateTime.UtcNow.ToString("O") +
                       "\",\"logVersion\":\"" + Escape(CurrentLogVersion) +
                       "\",\"schemaVersion\":\"" + Escape(schemaVersion ?? JsonUtil.CurrentSchemaVersion) +
                       "\",\"level\":\"" + Escape(level) +
                       "\",\"stage\":\"" + Escape(eventName) +
                       "\",\"commandId\":\"" + Escape(commandId ?? string.Empty) +
                       "\",\"tool\":\"" + Escape(tool ?? string.Empty) +
                       "\",\"status\":\"" + Escape(status ?? string.Empty) +
                       "\",\"message\":\"" + Escape(message ?? string.Empty) +
                       "\"}";

            using (var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.WriteLine(line);
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
