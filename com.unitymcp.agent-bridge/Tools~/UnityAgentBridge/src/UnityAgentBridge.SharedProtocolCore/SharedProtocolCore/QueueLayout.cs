using System;
using System.IO;

namespace UnityMcp.AgentBridge
{
    public static class QueueLayout
    {
        public const string DefaultQueueRoot = "Temp/AgentBridge";
        public const string InboxDirectoryName = "inbox";
        public const string ProcessingDirectoryName = "processing";
        public const string OutboxDirectoryName = "outbox";
        public const string FailedDirectoryName = "failed";
        public const string StatusDirectoryName = "status";
        public const string LogsDirectoryName = "logs";
        public const string StatusFileName = "unity_bridge_status.json";

        public static string ResolveRelativePath(string projectRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("Relative path is required.", nameof(relativePath));
            }

            return Path.GetFullPath(Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
