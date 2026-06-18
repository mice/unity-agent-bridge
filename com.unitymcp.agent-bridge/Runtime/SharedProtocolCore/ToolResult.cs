using System;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class ToolResult
    {
        public string schemaVersion = JsonUtil.CurrentSchemaVersion;
        public string commandId;
        public string tool;
        public bool success;
        public string status;
        public string startedAt;
        public string finishedAt;
        public long durationMs;
        public string summary;
        public List<ToolError> errors = new List<ToolError>();
        public List<ToolWarning> warnings = new List<ToolWarning>();
        public List<ToolLog> logs = new List<ToolLog>();
        public string metricsObjectJson = "{}";
        public List<string> changedFiles = new List<string>();
        public string reportPath;

        public static ToolResult InvalidArgs(string code, string message)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.InvalidArgs,
                summary = message,
                errors = new List<ToolError>
                {
                    new ToolError
                    {
                        code = code,
                        message = message
                    }
                }
            };
        }

        public static ToolResult Unsupported(string code, string message)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Unsupported,
                summary = message,
                errors = new List<ToolError>
                {
                    new ToolError
                    {
                        code = code,
                        message = message
                    }
                }
            };
        }
    }

    public static class ToolResultStatus
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Resuming = "resuming";
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Timeout = "timeout";
        public const string Blocked = "blocked";
        public const string Unsupported = "unsupported";
        public const string InvalidArgs = "invalid_args";
        public const string Exception = "exception";
        public const string Cancelled = "cancelled";
    }
}
