using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentCommandQueue
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Func<DateTime> _utcNowProvider;

        public AgentCommandQueue(string projectRoot, string relativeRoot = "Temp/AgentBridge", Func<DateTime> utcNowProvider = null)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            if (string.IsNullOrWhiteSpace(relativeRoot))
            {
                throw new ArgumentException("Relative queue root is required.", nameof(relativeRoot));
            }

            ProjectRoot = Path.GetFullPath(projectRoot);
            RelativeRoot = relativeRoot.Replace('\\', '/');
            QueueRoot = Path.GetFullPath(Path.Combine(ProjectRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar)));
            InboxDirectory = Path.Combine(QueueRoot, QueueLayout.InboxDirectoryName);
            ProcessingDirectory = Path.Combine(QueueRoot, QueueLayout.ProcessingDirectoryName);
            OutboxDirectory = Path.Combine(QueueRoot, QueueLayout.OutboxDirectoryName);
            FailedDirectory = Path.Combine(QueueRoot, QueueLayout.FailedDirectoryName);
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);

            EnsureDirectories();
        }

        public string ProjectRoot { get; }

        public string QueueRoot { get; }

        public string RelativeRoot { get; }

        public string InboxDirectory { get; }

        public string ProcessingDirectory { get; }

        public string OutboxDirectory { get; }

        public string FailedDirectory { get; }

        public QueueEnqueueResult Enqueue(string rawCommandJson)
        {
            var parseResult = JsonUtil.ExtractCommand(rawCommandJson);
            if (!parseResult.Success)
            {
                return QueueEnqueueResult.Invalid(parseResult.Failure);
            }

            var command = parseResult.Command;
            if (HasCommandId(command.commandId))
            {
                return QueueEnqueueResult.Duplicate(command.commandId);
            }

            var targetPath = GetInboxCommandPath(command.commandId);
            WriteAllTextAtomic(targetPath, rawCommandJson);
            return QueueEnqueueResult.Accepted(command.commandId, targetPath);
        }

        public bool TryDequeue(out QueuedCommand queuedCommand)
        {
            queuedCommand = null;

            var validCandidates = new List<ParsedInboxCommand>();
            foreach (var path in Directory.GetFiles(InboxDirectory, "*.json"))
            {
                if (ShouldSkipCandidate(path))
                {
                    continue;
                }

                if (!TryReadAllText(path, out var rawCommandJson))
                {
                    continue;
                }

                var parseResult = JsonUtil.ExtractCommand(rawCommandJson);
                if (!parseResult.Success)
                {
                    HandleInvalidInboxCommand(path, rawCommandJson, parseResult.Failure);
                    continue;
                }

                validCandidates.Add(new ParsedInboxCommand(path, rawCommandJson, parseResult.Command));
            }

            foreach (var candidate in validCandidates.OrderBy(item => item.Command.createdAt, StringComparer.Ordinal).ThenBy(item => item.Command.commandId, StringComparer.Ordinal))
            {
                var processingCommandPath = GetProcessingCommandPath(candidate.Command.commandId);
                if (!TryMove(candidate.Path, processingCommandPath))
                {
                    continue;
                }

                var state = new QueueCommandState
                {
                    commandId = candidate.Command.commandId,
                    tool = candidate.Command.tool,
                    status = ToolResultStatus.Running,
                    startedAt = FormatUtc(_utcNowProvider()),
                    timeoutMs = candidate.Command.timeoutMs
                };
                WriteStateAtomic(GetProcessingStatePath(candidate.Command.commandId), state);

                queuedCommand = new QueuedCommand(candidate.Command, candidate.RawCommandJson, processingCommandPath, GetProcessingStatePath(candidate.Command.commandId), state);
                return true;
            }

            return false;
        }

        public IReadOnlyList<QueueRecoveryRecord> Recover()
        {
            var records = new List<QueueRecoveryRecord>();
            var commandIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in Directory.GetFiles(ProcessingDirectory, "*.json"))
            {
                if (path.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                commandIds.Add(Path.GetFileNameWithoutExtension(path));
            }

            foreach (var path in Directory.GetFiles(ProcessingDirectory, "*.state.json"))
            {
                var fileName = Path.GetFileName(path);
                if (fileName == null || !fileName.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                commandIds.Add(fileName.Substring(0, fileName.Length - ".state.json".Length));
            }

            foreach (var commandId in commandIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                var processingCommandPath = GetProcessingCommandPath(commandId);
                var processingStatePath = GetProcessingStatePath(commandId);
                var outboxResultPath = GetOutboxResultPath(commandId);

                if (File.Exists(outboxResultPath))
                {
                    DeleteIfExists(processingCommandPath);
                    DeleteIfExists(processingStatePath);
                    records.Add(QueueRecoveryRecord.CleanedCompleted(commandId));
                    continue;
                }

                var hasState = TryReadStateFile(processingStatePath, out var state);
                var hasCommand = TryReadAllText(processingCommandPath, out var rawCommandJson);

                if (hasState && !hasCommand)
                {
                    var result = ToolResult.InvalidArgs("AGENTBRIDGE_STATE_WITHOUT_COMMAND", "processing state exists without command file.");
                    result.commandId = commandId;
                    result.tool = state?.tool;
                    result.status = ToolResultStatus.Exception;
                    result.summary = "processing state exists without command file.";
                    WriteResultAtomic(outboxResultPath, result);
                    DeleteIfExists(processingStatePath);
                    records.Add(QueueRecoveryRecord.Exception(commandId, result));
                    continue;
                }

                if (!hasState || !hasCommand)
                {
                    continue;
                }

                var parseResult = JsonUtil.ExtractCommand(rawCommandJson);
                if (!parseResult.Success)
                {
                    var result = parseResult.Failure;
                    result.commandId = commandId;
                    WriteResultAtomic(outboxResultPath, result);
                    MoveToFailed(processingCommandPath, commandId);
                    DeleteIfExists(processingStatePath);
                    records.Add(QueueRecoveryRecord.Exception(commandId, result));
                    continue;
                }

                var command = parseResult.Command;
                if (IsTimedOut(state, _utcNowProvider()))
                {
                    var timeoutResult = ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_TIMEOUT", "processing command exceeded recovery timeout.");
                    timeoutResult.commandId = commandId;
                    timeoutResult.tool = command.tool;
                    timeoutResult.status = ToolResultStatus.Timeout;
                    timeoutResult.summary = "processing command exceeded recovery timeout.";
                    WriteResultAtomic(outboxResultPath, timeoutResult);
                    MoveToFailed(processingCommandPath, commandId);
                    DeleteIfExists(processingStatePath);
                    records.Add(QueueRecoveryRecord.Timeout(commandId, timeoutResult));
                    continue;
                }

                state.status = ToolResultStatus.Resuming;
                WriteStateAtomic(processingStatePath, state);
                records.Add(QueueRecoveryRecord.Resuming(new QueuedCommand(command, rawCommandJson, processingCommandPath, processingStatePath, state)));
            }

            return records;
        }

        public void Complete(string commandId, ToolResult result)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("commandId is required.", nameof(commandId));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.commandId = commandId;
            WriteResultAtomic(GetOutboxResultPath(commandId), result);
            DeleteIfExists(GetProcessingCommandPath(commandId));
            DeleteIfExists(GetProcessingStatePath(commandId));
        }

        public bool TryReadState(string commandId, out QueueCommandState state)
        {
            return TryReadStateFile(GetProcessingStatePath(commandId), out state);
        }

        public void UpdateState(string commandId, Action<QueueCommandState> update)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("commandId is required.", nameof(commandId));
            }

            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (!TryReadState(commandId, out var state) || state == null)
            {
                return;
            }

            update(state);
            WriteStateAtomic(GetProcessingStatePath(commandId), state);
        }

        public void Fail(string commandId, ToolResult result)
        {
            Complete(commandId, result);
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(QueueRoot);
            Directory.CreateDirectory(InboxDirectory);
            Directory.CreateDirectory(ProcessingDirectory);
            Directory.CreateDirectory(OutboxDirectory);
            Directory.CreateDirectory(FailedDirectory);
        }

        private bool HasCommandId(string commandId)
        {
            return File.Exists(GetInboxCommandPath(commandId)) ||
                   File.Exists(GetProcessingCommandPath(commandId)) ||
                   File.Exists(GetProcessingStatePath(commandId)) ||
                   File.Exists(GetOutboxResultPath(commandId)) ||
                   File.Exists(GetFailedCommandPath(commandId));
        }

        private bool ShouldSkipCandidate(string path)
        {
            var fileName = Path.GetFileName(path);
            if (fileName == null)
            {
                return true;
            }

            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var info = new FileInfo(path);
            return info.Length == 0;
        }

        private void HandleInvalidInboxCommand(string path, string rawCommandJson, ToolResult failure)
        {
            var commandId = Path.GetFileNameWithoutExtension(path);
            failure.commandId = commandId;
            failure.tool = failure.tool ?? "unknown";
            var invalidField = ExtractInvalidFieldName(failure);
            if (!string.IsNullOrWhiteSpace(invalidField))
            {
                var diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(ProjectRoot, RelativeRoot, path, invalidField, failure);
                StaleBridgeStateDiagnosticsCollector.AttachToResultMetrics(failure, diagnostics);
            }
            WriteResultAtomic(GetOutboxResultPath(commandId), failure);
            MoveToFailed(path, commandId);
        }

        private static string ExtractInvalidFieldName(ToolResult failure)
        {
            var message = failure?.errors?.FirstOrDefault()?.message ?? failure?.summary ?? string.Empty;
            const string prefix = "Field '";
            const string suffix = "' must";
            var start = message.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += prefix.Length;
            var end = message.IndexOf(suffix, start, StringComparison.Ordinal);
            return end > start ? message.Substring(start, end - start) : string.Empty;
        }

        private bool TryReadAllText(string path, out string content)
        {
            content = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, Utf8NoBom, true))
                {
                    content = reader.ReadToEnd();
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private bool TryReadStateFile(string path, out QueueCommandState state)
        {
            state = null;
            if (!TryReadAllText(path, out var content))
            {
                return false;
            }

            try
            {
                state = JsonConvert.DeserializeObject<QueueCommandState>(content);
                return state != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryMove(string sourcePath, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath))
                {
                    return false;
                }

                File.Move(sourcePath, targetPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void MoveToFailed(string sourcePath, string commandId)
        {
            var targetPath = GetFailedCommandPath(commandId);
            DeleteIfExists(targetPath);
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetPath);
            }
        }

        private bool IsTimedOut(QueueCommandState state, DateTime nowUtc)
        {
            if (state == null || state.timeoutMs <= 0 || string.IsNullOrWhiteSpace(state.startedAt))
            {
                return false;
            }

            if (!DateTime.TryParse(state.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAt))
            {
                return false;
            }

            return (nowUtc - startedAt).TotalMilliseconds > state.timeoutMs * 2L;
        }

        private void WriteStateAtomic(string targetPath, QueueCommandState state)
        {
            WriteAllTextAtomic(targetPath, JsonConvert.SerializeObject(state, Formatting.None));
        }

        private void WriteResultAtomic(string targetPath, ToolResult result)
        {
            WriteAllTextAtomic(targetPath, JsonUtil.SerializeResult(result));
        }

        private void WriteAllTextAtomic(string targetPath, string content)
        {
            var tempPath = targetPath + ".tmp";
            DeleteIfExists(tempPath);

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(content);
            }

            DeleteIfExists(targetPath);
            File.Move(tempPath, targetPath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetInboxCommandPath(string commandId)
        {
            return Path.Combine(InboxDirectory, commandId + ".json");
        }

        private string GetProcessingCommandPath(string commandId)
        {
            return Path.Combine(ProcessingDirectory, commandId + ".json");
        }

        private string GetProcessingStatePath(string commandId)
        {
            return Path.Combine(ProcessingDirectory, commandId + ".state.json");
        }

        private string GetOutboxResultPath(string commandId)
        {
            return Path.Combine(OutboxDirectory, commandId + ".result.json");
        }

        private string GetFailedCommandPath(string commandId)
        {
            return Path.Combine(FailedDirectory, commandId + ".json");
        }

        private static string FormatUtc(DateTime utcNow)
        {
            return utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        private sealed class ParsedInboxCommand
        {
            public ParsedInboxCommand(string path, string rawCommandJson, AgentCommand command)
            {
                Path = path;
                RawCommandJson = rawCommandJson;
                Command = command;
            }

            public string Path { get; }

            public string RawCommandJson { get; }

            public AgentCommand Command { get; }
        }
    }

    public sealed class QueuedCommand
    {
        public QueuedCommand(AgentCommand command, string rawCommandJson, string processingCommandPath, string statePath, QueueCommandState state)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            RawCommandJson = rawCommandJson ?? throw new ArgumentNullException(nameof(rawCommandJson));
            ProcessingCommandPath = processingCommandPath ?? throw new ArgumentNullException(nameof(processingCommandPath));
            StatePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public AgentCommand Command { get; }

        public string RawCommandJson { get; }

        public string ProcessingCommandPath { get; }

        public string StatePath { get; }

        public QueueCommandState State { get; }
    }

    public sealed class QueueEnqueueResult
    {
        private QueueEnqueueResult()
        {
        }

        public bool Success { get; private set; }

        public string CommandId { get; private set; }

        public string QueuePath { get; private set; }

        public string Reason { get; private set; }

        public ToolResult Failure { get; private set; }

        public static QueueEnqueueResult Accepted(string commandId, string queuePath)
        {
            return new QueueEnqueueResult
            {
                Success = true,
                CommandId = commandId,
                QueuePath = queuePath,
                Reason = "accepted"
            };
        }

        public static QueueEnqueueResult Duplicate(string commandId)
        {
            return new QueueEnqueueResult
            {
                Success = false,
                CommandId = commandId,
                Reason = "duplicate_command_id"
            };
        }

        public static QueueEnqueueResult Invalid(ToolResult failure)
        {
            return new QueueEnqueueResult
            {
                Success = false,
                Failure = failure,
                Reason = "invalid_command"
            };
        }
    }

    public enum QueueRecoveryAction
    {
        Resuming,
        TimedOut,
        ExceptionWritten,
        CompletedCleanup,
        FailedRecovery
    }

    public sealed class QueueRecoveryRecord
    {
        private QueueRecoveryRecord()
        {
        }

        public string CommandId { get; private set; }

        public QueueRecoveryAction Action { get; private set; }

        public QueuedCommand Command { get; private set; }

        public ToolResult Result { get; private set; }

        public static QueueRecoveryRecord Resuming(QueuedCommand command)
        {
            return new QueueRecoveryRecord
            {
                CommandId = command.Command.commandId,
                Action = QueueRecoveryAction.Resuming,
                Command = command
            };
        }

        public static QueueRecoveryRecord Timeout(string commandId, ToolResult result)
        {
            return new QueueRecoveryRecord
            {
                CommandId = commandId,
                Action = QueueRecoveryAction.TimedOut,
                Result = result
            };
        }

        public static QueueRecoveryRecord Exception(string commandId, ToolResult result)
        {
            return new QueueRecoveryRecord
            {
                CommandId = commandId,
                Action = QueueRecoveryAction.ExceptionWritten,
                Result = result
            };
        }

        public static QueueRecoveryRecord Failed(string commandId, ToolResult result)
        {
            return new QueueRecoveryRecord
            {
                CommandId = commandId,
                Action = QueueRecoveryAction.FailedRecovery,
                Result = result
            };
        }

        public static QueueRecoveryRecord CleanedCompleted(string commandId)
        {
            return new QueueRecoveryRecord
            {
                CommandId = commandId,
                Action = QueueRecoveryAction.CompletedCleanup
            };
        }
    }

}
