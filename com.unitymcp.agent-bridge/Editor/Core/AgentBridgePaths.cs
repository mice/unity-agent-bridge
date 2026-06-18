using System;
using System.IO;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentBridgePaths
    {
        public AgentBridgePaths(string projectRoot, AgentBridgeSettings settings)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ProjectRoot = Path.GetFullPath(projectRoot);
            TempRoot = string.IsNullOrWhiteSpace(settings.tempRoot) ? "Temp/AgentBridge" : settings.tempRoot;
            QueueRoot = QueueLayout.ResolveRelativePath(ProjectRoot, TempRoot);
            LogRoot = ResolveRelative(settings.logRoot);
            MetricsPath = ResolveRelative(settings.metricsPath);
            PluginCatalogPath = ResolveRelative(settings.pluginCatalogPath);
            BridgeLogPath = Path.Combine(LogRoot, "bridge.log");
            StatusRoot = Path.Combine(QueueRoot, QueueLayout.StatusDirectoryName);
            StatusFilePath = Path.Combine(StatusRoot, QueueLayout.StatusFileName);
        }

        public string ProjectRoot { get; }

        public string TempRoot { get; }

        public string QueueRoot { get; }

        public string LogRoot { get; }

        public string MetricsPath { get; }

        public string PluginCatalogPath { get; }

        public string BridgeLogPath { get; }

        public string StatusRoot { get; }

        public string StatusFilePath { get; }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(QueueRoot);
            Directory.CreateDirectory(Path.Combine(QueueRoot, QueueLayout.InboxDirectoryName));
            Directory.CreateDirectory(Path.Combine(QueueRoot, QueueLayout.ProcessingDirectoryName));
            Directory.CreateDirectory(Path.Combine(QueueRoot, QueueLayout.OutboxDirectoryName));
            Directory.CreateDirectory(Path.Combine(QueueRoot, QueueLayout.FailedDirectoryName));
            Directory.CreateDirectory(StatusRoot);
            Directory.CreateDirectory(LogRoot);

            var metricsDirectory = Path.GetDirectoryName(MetricsPath);
            if (!string.IsNullOrWhiteSpace(metricsDirectory))
            {
                Directory.CreateDirectory(metricsDirectory);
            }

            var pluginCatalogDirectory = Path.GetDirectoryName(PluginCatalogPath);
            if (!string.IsNullOrWhiteSpace(pluginCatalogDirectory))
            {
                Directory.CreateDirectory(pluginCatalogDirectory);
            }
        }

        private string ResolveRelative(string value)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, value.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
