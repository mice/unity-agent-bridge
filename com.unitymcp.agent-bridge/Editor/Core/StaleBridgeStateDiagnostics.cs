using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    internal sealed class StaleBridgeStateDiagnostics
    {
        public string primaryClassification;
        public string evidencePriorityPath;
        public QueueSnapshotCounts queueSnapshotCounts = new QueueSnapshotCounts();
        public List<QueueCommandEvidence> activeProcessingCommands = new List<QueueCommandEvidence>();
        public long heartbeatAgeMs = -1;
        public string pollerStage;
        public string statusCommandId;
        public string configuredProjectPath;
        public string detectedProjectPath;
        public string projectBindingKind;
        public string runtimeIdentity;
        public string executableIdentity;
        public List<string> missingEvidence = new List<string>();
        public List<string> conflictingEvidence = new List<string>();
        public string invalidField;
        public string sourceDiagnosticHint;
    }

    [Serializable]
    internal sealed class QueueSnapshotCounts
    {
        public int inbox;
        public int processing;
        public int outbox;
        public int failed;
        public int orphaned;
    }

    [Serializable]
    internal sealed class QueueCommandEvidence
    {
        public string commandId;
        public string tool;
        public string createdAt;
        public int timeoutMs;
        public string status;
        public string startedAt;
    }

    internal static class StaleBridgeStateDiagnosticsCollector
    {
        private const long FreshHeartbeatThresholdMs = 30_000;
        private const long StaleHeartbeatThresholdMs = 30_000;

        public static StaleBridgeStateDiagnostics CollectForInvalidCommand(
            string projectRoot,
            string tempRoot,
            string invalidCommandPath,
            string invalidField,
            ToolResult parseFailure)
        {
            var diagnostics = BuildBaseDiagnostics(projectRoot, tempRoot, invalidCommandPath, invalidField, parseFailure);
            diagnostics.primaryClassification = Classify(diagnostics);
            return diagnostics;
        }

        public static StaleBridgeStateDiagnostics CaptureSnapshot(string projectRoot, string tempRoot)
        {
            var diagnostics = BuildBaseDiagnostics(projectRoot, tempRoot, string.Empty, string.Empty, null);
            diagnostics.primaryClassification = string.Empty;
            diagnostics.evidencePriorityPath = string.Empty;
            return diagnostics;
        }

        public static string AttachToResultMetrics(ToolResult result, StaleBridgeStateDiagnostics diagnostics)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var root = ParseMetricsObject(result.metricsObjectJson);
            root["staleStateDiagnostics"] = diagnostics == null ? JValue.CreateNull() : JToken.FromObject(diagnostics);
            result.metricsObjectJson = root.ToString(Formatting.None);
            return result.metricsObjectJson;
        }

        internal static string Classify(StaleBridgeStateDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                return "inconclusive";
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.invalidField))
            {
                diagnostics.evidencePriorityPath = "command_payload>queue_snapshot";
                if (HasInvalidCreatedAtEvidence(diagnostics))
                {
                    return "stale_queue";
                }

                if (HasStalePollerEvidence(diagnostics))
                {
                    return "stale_poller_session";
                }

                if (HasRuntimeMismatchEvidence(diagnostics))
                {
                    return "stale_runtime";
                }

                diagnostics.missingEvidence = Distinct(diagnostics.missingEvidence, "queue_snapshot", "heartbeat_age", "runtime_identity");
                return "inconclusive";
            }

            diagnostics.missingEvidence = Distinct(diagnostics.missingEvidence, "classification_requires_invalid_command_context");
            return "inconclusive";
        }

        private static StaleBridgeStateDiagnostics BuildBaseDiagnostics(
            string projectRoot,
            string tempRoot,
            string invalidCommandPath,
            string invalidField,
            ToolResult parseFailure)
        {
            var normalizedProjectRoot = NormalizePath(projectRoot);
            var diagnostics = new StaleBridgeStateDiagnostics
            {
                invalidField = invalidField ?? string.Empty,
                detectedProjectPath = normalizedProjectRoot,
                configuredProjectPath = ResolveConfiguredProjectPath(projectRoot),
                runtimeIdentity = Application.unityVersion ?? string.Empty,
                executableIdentity = Environment.CommandLine ?? string.Empty,
                sourceDiagnosticHint = parseFailure?.summary ?? string.Empty
            };

            diagnostics.projectBindingKind = DetermineProjectBindingKind(diagnostics.configuredProjectPath, diagnostics.detectedProjectPath);
            PopulateQueueEvidence(diagnostics, projectRoot, tempRoot, invalidCommandPath);
            PopulateStatusEvidence(diagnostics, projectRoot, tempRoot);

            if (string.IsNullOrWhiteSpace(diagnostics.configuredProjectPath))
            {
                diagnostics.missingEvidence.Add("configured_project_path");
            }

            if (diagnostics.heartbeatAgeMs < 0)
            {
                diagnostics.missingEvidence.Add("heartbeat_age");
            }

            if (string.IsNullOrWhiteSpace(diagnostics.pollerStage))
            {
                diagnostics.missingEvidence.Add("poller_stage");
            }

            return diagnostics;
        }

        private static void PopulateQueueEvidence(StaleBridgeStateDiagnostics diagnostics, string projectRoot, string tempRoot, string invalidCommandPath)
        {
            var queueRoot = QueueLayout.ResolveRelativePath(projectRoot, tempRoot);
            diagnostics.queueSnapshotCounts = new QueueSnapshotCounts
            {
                inbox = CountJson(Path.Combine(queueRoot, QueueLayout.InboxDirectoryName)),
                processing = CountJson(Path.Combine(queueRoot, QueueLayout.ProcessingDirectoryName), "*.json", excludeStateFiles: true),
                outbox = CountJson(Path.Combine(queueRoot, QueueLayout.OutboxDirectoryName)),
                failed = CountJson(Path.Combine(queueRoot, QueueLayout.FailedDirectoryName)),
                orphaned = CountOrphanedStateFiles(Path.Combine(queueRoot, QueueLayout.ProcessingDirectoryName))
            };

            foreach (var path in Directory.Exists(Path.Combine(queueRoot, QueueLayout.ProcessingDirectoryName))
                ? Directory.GetFiles(Path.Combine(queueRoot, QueueLayout.ProcessingDirectoryName), "*.json")
                : Array.Empty<string>())
            {
                if (path.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var evidence = ReadQueueCommandEvidence(path, Path.Combine(queueRoot, QueueLayout.ProcessingDirectoryName));
                if (evidence != null)
                {
                    diagnostics.activeProcessingCommands.Add(evidence);
                }
            }

            if (!string.IsNullOrWhiteSpace(invalidCommandPath))
            {
                var invalidEvidence = ReadQueueCommandEvidence(invalidCommandPath, Path.GetDirectoryName(invalidCommandPath) ?? string.Empty);
                if (invalidEvidence != null && diagnostics.activeProcessingCommands.All(item => !string.Equals(item.commandId, invalidEvidence.commandId, StringComparison.Ordinal)))
                {
                    diagnostics.activeProcessingCommands.Add(invalidEvidence);
                }
            }
        }

        private static void PopulateStatusEvidence(StaleBridgeStateDiagnostics diagnostics, string projectRoot, string tempRoot)
        {
            var queueRoot = QueueLayout.ResolveRelativePath(projectRoot, tempRoot);
            var statusPath = Path.Combine(queueRoot, QueueLayout.StatusDirectoryName, QueueLayout.StatusFileName);
            if (!File.Exists(statusPath))
            {
                diagnostics.missingEvidence.Add("status_file");
                return;
            }

            try
            {
                var raw = File.ReadAllText(statusPath);
                var parsed = JsonUtility.FromJson<UnityBridgeStatusSnapshot>(raw);
                if (parsed == null)
                {
                    diagnostics.conflictingEvidence.Add("status_file_unreadable");
                    return;
                }

                diagnostics.pollerStage = parsed.currentStage ?? string.Empty;
                diagnostics.statusCommandId = parsed.currentCommandId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(parsed.projectPath))
                {
                    diagnostics.detectedProjectPath = NormalizePath(parsed.projectPath);
                }

                if (DateTime.TryParse(parsed.heartbeatUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var heartbeatUtc))
                {
                    diagnostics.heartbeatAgeMs = Math.Max(0, (long)(DateTime.UtcNow - heartbeatUtc).TotalMilliseconds);
                }
            }
            catch
            {
                diagnostics.conflictingEvidence.Add("status_file_parse_failed");
            }
        }

        private static bool HasInvalidCreatedAtEvidence(StaleBridgeStateDiagnostics diagnostics)
        {
            if (!string.Equals(diagnostics.invalidField, "createdAt", StringComparison.Ordinal))
            {
                return diagnostics.activeProcessingCommands.Any(IsCommandQueueStale);
            }

            return diagnostics.activeProcessingCommands.Any(command =>
                !IsValidUtc(command.createdAt) ||
                IsCommandQueueStale(command));
        }

        private static bool HasStalePollerEvidence(StaleBridgeStateDiagnostics diagnostics)
        {
            if (diagnostics.heartbeatAgeMs >= StaleHeartbeatThresholdMs)
            {
                diagnostics.evidencePriorityPath = "command_payload>queue_snapshot>heartbeat_poller";
                return true;
            }

            if (string.IsNullOrWhiteSpace(diagnostics.pollerStage))
            {
                diagnostics.evidencePriorityPath = "command_payload>queue_snapshot>heartbeat_poller";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.statusCommandId) &&
                diagnostics.activeProcessingCommands.Count > 0 &&
                diagnostics.activeProcessingCommands.All(item => !string.Equals(item.commandId, diagnostics.statusCommandId, StringComparison.Ordinal)))
            {
                diagnostics.evidencePriorityPath = "command_payload>queue_snapshot>heartbeat_poller";
                return true;
            }

            return false;
        }

        private static bool HasRuntimeMismatchEvidence(StaleBridgeStateDiagnostics diagnostics)
        {
            if (diagnostics.heartbeatAgeMs < 0 || diagnostics.heartbeatAgeMs >= FreshHeartbeatThresholdMs)
            {
                diagnostics.missingEvidence = Distinct(diagnostics.missingEvidence, "fresh_heartbeat_required_for_runtime");
                return false;
            }

            if (string.IsNullOrWhiteSpace(diagnostics.pollerStage))
            {
                diagnostics.missingEvidence = Distinct(diagnostics.missingEvidence, "fresh_poller_stage_required_for_runtime");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(diagnostics.configuredProjectPath) &&
                !string.IsNullOrWhiteSpace(diagnostics.detectedProjectPath) &&
                !string.Equals(NormalizePath(diagnostics.configuredProjectPath), NormalizePath(diagnostics.detectedProjectPath), StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.evidencePriorityPath = "command_payload>queue_snapshot>heartbeat_poller>runtime_project_identity";
                return true;
            }

            return false;
        }

        private static bool IsCommandQueueStale(QueueCommandEvidence command)
        {
            if (command == null)
            {
                return false;
            }

            if (!IsValidUtc(command.createdAt))
            {
                return true;
            }

            if (!IsValidUtc(command.startedAt))
            {
                return false;
            }

            if (!DateTime.TryParse(command.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAtUtc))
            {
                return false;
            }

            var timeoutMs = Math.Max(0, command.timeoutMs);
            if (timeoutMs <= 0)
            {
                return false;
            }

            return (DateTime.UtcNow - startedAtUtc).TotalMilliseconds > timeoutMs;
        }

        private static QueueCommandEvidence ReadQueueCommandEvidence(string commandPath, string processingDirectory)
        {
            try
            {
                var raw = File.ReadAllText(commandPath);
                var parsed = JObject.Parse(raw);
                var commandId = parsed.Value<string>("commandId") ?? Path.GetFileNameWithoutExtension(commandPath);
                var statePath = Path.Combine(processingDirectory, commandId + ".state.json");
                QueueCommandState state = null;
                if (File.Exists(statePath))
                {
                    try
                    {
                        state = JsonConvert.DeserializeObject<QueueCommandState>(File.ReadAllText(statePath));
                    }
                    catch
                    {
                    }
                }

                return new QueueCommandEvidence
                {
                    commandId = commandId,
                    tool = parsed.Value<string>("tool") ?? string.Empty,
                    createdAt = parsed["createdAt"]?.Type == JTokenType.String ? parsed.Value<string>("createdAt") ?? string.Empty : "<invalid-type>",
                    timeoutMs = parsed.Value<int?>("timeoutMs") ?? state?.timeoutMs ?? 0,
                    status = state?.status ?? string.Empty,
                    startedAt = state?.startedAt ?? string.Empty
                };
            }
            catch
            {
                return null;
            }
        }

        private static JObject ParseMetricsObject(string metricsObjectJson)
        {
            if (string.IsNullOrWhiteSpace(metricsObjectJson))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(metricsObjectJson);
            }
            catch
            {
                return new JObject();
            }
        }

        private static int CountJson(string directoryPath, string pattern = "*.json", bool excludeStateFiles = false)
        {
            if (!Directory.Exists(directoryPath))
            {
                return 0;
            }

            var files = Directory.GetFiles(directoryPath, pattern);
            return excludeStateFiles
                ? files.Count(path => !path.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase))
                : files.Length;
        }

        private static int CountOrphanedStateFiles(string processingDirectory)
        {
            if (!Directory.Exists(processingDirectory))
            {
                return 0;
            }

            var count = 0;
            foreach (var statePath in Directory.GetFiles(processingDirectory, "*.state.json"))
            {
                var fileName = Path.GetFileName(statePath);
                if (fileName == null)
                {
                    continue;
                }

                var commandId = fileName.Substring(0, fileName.Length - ".state.json".Length);
                if (!File.Exists(Path.Combine(processingDirectory, commandId + ".json")))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveConfiguredProjectPath(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            var projectLocalConfigPath = Path.Combine(projectRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json");
            if (File.Exists(projectLocalConfigPath))
            {
                var projectLocalPath = TryReadConfiguredProjectPath(projectLocalConfigPath);
                if (!string.IsNullOrWhiteSpace(projectLocalPath))
                {
                    return projectLocalPath;
                }
            }

            var repositoryRoot = FindRepositoryRoot(projectRoot);
            if (string.IsNullOrWhiteSpace(repositoryRoot))
            {
                return string.Empty;
            }

            var configPath = Path.Combine(repositoryRoot, "Tools", "AgentBridge", "Start-Codex-With-UnityMcp.json");
            return File.Exists(configPath) ? TryReadConfiguredProjectPath(configPath) : string.Empty;
        }

        private static string TryReadConfiguredProjectPath(string configPath)
        {
            try
            {
                var parsed = JObject.Parse(File.ReadAllText(configPath));
                return NormalizePath(parsed.Value<string>("unityProjectPath"));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DetermineProjectBindingKind(string configuredProjectPath, string detectedProjectPath)
        {
            if (string.IsNullOrWhiteSpace(configuredProjectPath))
            {
                return "baseline";
            }

            return string.Equals(NormalizePath(configuredProjectPath), NormalizePath(detectedProjectPath), StringComparison.OrdinalIgnoreCase)
                ? "bound"
                : "explicit";
        }

        private static string FindRepositoryRoot(string projectRoot)
        {
            for (var cursor = Path.GetFullPath(projectRoot); !string.IsNullOrWhiteSpace(cursor); cursor = Path.GetDirectoryName(cursor))
            {
                if (Directory.Exists(Path.Combine(cursor, ".git")) || File.Exists(Path.Combine(cursor, "openspec", "project.md")))
                {
                    return cursor;
                }
            }

            return string.Empty;
        }

        private static bool IsValidUtc(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path).Replace('\\', '/');
        }

        private static List<string> Distinct(List<string> values, params string[] additions)
        {
            values ??= new List<string>();
            foreach (var addition in additions)
            {
                if (!values.Contains(addition, StringComparer.Ordinal))
                {
                    values.Add(addition);
                }
            }

            return values;
        }
    }
}
