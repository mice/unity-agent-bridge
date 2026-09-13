using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using UnityMcp.AgentTaskEditor;

namespace UnityMcp.AgentBridge
{
    /// <summary>Owns task-backed EditMode runs while preserving the legacy queue contract.</summary>
    public static class AgentTaskEditModeExecutionService
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            if (request == null) return new AgentTaskControlResponse { Success = false, ErrorCode = "INVALID_REQUEST", ErrorMessage = "Request is required." };
            if (request.Operation == AgentTaskControlProtocol.Create)
            {
                if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(request.Payload ?? "{}", out var filterArgs, out var failure))
                    return new AgentTaskControlResponse { Success = false, ErrorCode = "INVALID_ARGS", ErrorMessage = failure.summary };
                var validation = Validate(filterArgs);
                if (validation != null) return new AgentTaskControlResponse { Success = false, ErrorCode = "INVALID_ARGS", ErrorMessage = validation.summary };
                var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120000;
                var testTask = new RunEditModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), UnityTestOperationManager.CreateRunnerFilter(TestMode.EditMode, filterArgs));
                var task = new TimedEditModeTask(testTask, timeoutMs);
                var id = Manager.Register(task);
                var command = new AgentCommand { commandId = "agent-task-" + id.Value, tool = "unity.run_editmode_tests", timeoutMs = timeoutMs, rawArgsJson = request.Payload ?? "{}" };
                var settings = AgentBridgeSettingsLoader.Load().Settings;
                Runs[command.commandId] = new ActiveRun { TaskId = id, TestTask = testTask, Command = command, Settings = settings, Deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs), CompleteQueue = false };
                Persist(Runs[command.commandId]);
                EnsureUpdate();
                Manager.Start(id);
                Persist(Runs[command.commandId]);
                return ProjectControlResponse(Manager.Get(id), id.Value);
            }

            AgentTaskId taskId;
            try { taskId = new AgentTaskId(request.AgentTaskId); }
            catch (ArgumentException) { return new AgentTaskControlResponse { Success = false, ErrorCode = "INVALID_TASK_ID", ErrorMessage = "Agent task ID is required." }; }
            if (request.Operation == AgentTaskControlProtocol.Cancel)
            {
                var disposition = Manager.Cancel(taskId);
                return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = Manager.Get(taskId), AgentTaskId = taskId.Value, ErrorCode = disposition == AgentTaskCancellationDisposition.NotFound ? "NOT_FOUND" : null };
            }
            var snapshot = Manager.Get(taskId);
            return snapshot == null
                ? new AgentTaskControlResponse { Success = false, ErrorCode = "NOT_FOUND", ErrorMessage = "Agent task was not found.", AgentTaskId = taskId.Value }
                : ProjectControlResponse(snapshot, taskId.Value);
        }

        private static AgentTaskControlResponse ProjectControlResponse(AgentTaskSnapshot snapshot, string taskId)
        {
            return new AgentTaskControlResponse
            {
                Success = true,
                Snapshot = snapshot,
                AgentTaskId = taskId,
                ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload)
            };
        }
        private sealed class ActiveRun
        {
            public AgentTaskId TaskId;
            public RunEditModeTestsTask TestTask;
            public AgentCommand Command;
            public AgentBridgeSettings Settings;
            public DateTimeOffset StartedAt;
            public DateTimeOffset Deadline;
            public bool ResultWritten;
            public bool CompleteQueue = true;
        }

        private static readonly AgentTaskManager Manager = new AgentTaskManager();
        private static readonly Dictionary<string, ActiveRun> Runs = new Dictionary<string, ActiveRun>(StringComparer.Ordinal);
        private static bool updateRegistered;

        static AgentTaskEditModeExecutionService()
        {
            RestorePersistedRuns();
            EnsureUpdate();
        }

        public static ToolResult StartOrResume(AgentCommand command, AgentBridgeSettings settings, UnityTestRunArgs args)
        {
            if (command == null) return ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_NULL", "Command is required.");
            if (settings == null) return ToolResult.InvalidArgs("AGENTBRIDGE_SETTINGS_NULL", "Settings are required.");
            args ??= new UnityTestRunArgs();

            var validation = Validate(args);
            if (validation != null) return validation;
            if (UnityTestOperationManager.IsTestRunActive())
            {
                return new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Blocked,
                    summary = "Unity Test Runner is already running. Nested test runs are blocked."
                };
            }
            if (Runs.TryGetValue(command.commandId, out var active))
            {
                return Project(active);
            }

            var effectiveTimeout = Math.Min(command.timeoutMs, settings.maxToolDurationMs);
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_PROJECT_ROOT", "Project root could not be resolved.");
            }

            var filter = UnityTestOperationManager.CreateRunnerFilter(TestMode.EditMode, args);
            var testTask = new RunEditModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), filter);
            var task = new TimedEditModeTask(testTask, effectiveTimeout);
            var taskId = Manager.Register(task);
            active = new ActiveRun
            {
                TaskId = taskId,
                TestTask = testTask,
                Command = command,
                Settings = settings,
                StartedAt = DateTimeOffset.UtcNow,
                Deadline = DateTimeOffset.UtcNow.AddMilliseconds(effectiveTimeout),
            };
            Runs.Add(command.commandId, active);
            Persist(active);
            EnsureUpdate();
            Manager.Start(taskId);
            Persist(active);
            return Project(active);
        }

        private static ToolResult Validate(UnityTestRunArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.filter) && args.testNames != null && args.testNames.Length > 0)
                return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_CONFLICT", "Legacy filter cannot be combined with structured testNames.");
            return ValidateNoWildcards(args.testNames, "testNames")
                ?? ValidateNoWildcards(args.assemblyNames, "assemblyNames")
                ?? ValidateNoWildcards(args.categoryNames, "categoryNames")
                ?? ValidateNoWildcards(args.groupNames, "groupNames");
        }

        private static ToolResult ValidateNoWildcards(string[] values, string field)
        {
            if (values == null) return null;
            for (var i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_EMPTY", field + "[" + i + "] must not be empty.");
                if (values[i].IndexOf('*') >= 0 || values[i].IndexOf('?') >= 0) return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_WILDCARD_UNSUPPORTED", field + "[" + i + "] does not support wildcard characters '*' or '?'.");
            }
            return null;
        }

        private static ToolResult Project(ActiveRun run)
        {
            var snapshot = Manager.Get(run.TaskId);
            if (snapshot == null) return ToolResult.InvalidArgs("AGENT_TASK_NOT_FOUND", "Agent task was not found.");
            var result = AgentTaskToolResultAdapter.FromSnapshot(snapshot, run.Command, run.Command.tool);
            if (!snapshot.IsTerminal) return result;
            WriteTerminalResult(run, result);
            return result;
        }

        private static void EnsureUpdate()
        {
            if (updateRegistered) return;
            EditorApplication.update += OnUpdate;
            updateRegistered = true;
        }

        private static void OnUpdate()
        {
            foreach (var run in Runs.Values.ToList())
            {
                var snapshot = Manager.Get(run.TaskId);
                if (snapshot != null && snapshot.State == AgentTaskState.Running)
                    Manager.Advance(run.TaskId);
                snapshot = Manager.Get(run.TaskId);
                if (snapshot != null && snapshot.IsTerminal && !run.ResultWritten)
                    WriteTerminalResult(run, AgentTaskToolResultAdapter.FromSnapshot(snapshot, run.Command, run.Command.tool));
            }
        }

        private static void WriteTerminalResult(ActiveRun run, ToolResult result)
        {
            if (run.ResultWritten) return;
            run.ResultWritten = true;
            if (run.CompleteQueue && run.Settings != null)
            {
                var queue = new AgentCommandQueue(Directory.GetParent(Application.dataPath).FullName, run.Settings.tempRoot);
                queue.Complete(run.Command.commandId, result);
            }
            Runs.Remove(run.Command.commandId);
            DeleteState(run.Command.commandId, run.Settings.tempRoot);
        }

        private static void RestorePersistedRuns()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot)) return;
                var load = AgentBridgeSettingsLoader.Load();
                var settings = load.Settings;
                var tempRoot = settings == null ? "Temp/AgentBridge" : settings.tempRoot;
                var directory = Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar), "processing");
                if (!Directory.Exists(directory)) return;
                foreach (var path in Directory.GetFiles(directory, "*.task.json"))
                {
                    ActiveRunState state;
                    try { state = JsonConvert.DeserializeObject<ActiveRunState>(File.ReadAllText(path, Utf8NoBom)); }
                    catch { continue; }
                    if (state == null || string.IsNullOrWhiteSpace(state.commandId) || string.IsNullOrWhiteSpace(state.agentTaskId) || string.IsNullOrWhiteSpace(state.runGuid)) continue;
                    AgentCommand command;
                    if (!JsonUtil.TryDeserializeArgs<AgentCommand>(state.commandJson ?? "{}", out command, out _)) continue;
                    command.commandId = state.commandId;
                    command.tool = string.IsNullOrWhiteSpace(command.tool) ? "unity.run_editmode_tests" : command.tool;
                    if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(command.rawArgsJson ?? "{}", out var args, out _)) args = new UnityTestRunArgs();
                    var filter = UnityTestOperationManager.CreateRunnerFilter(TestMode.EditMode, args);
                    var task = new RunEditModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), filter);
                    var timed = new TimedEditModeTask(task, state.effectiveTimeoutMs, state.startedAtUtc);
                    var taskId = new AgentTaskId(state.agentTaskId);
                    Manager.RestoreRunning(taskId, timed, state.createdAtUtc == default(DateTimeOffset) ? DateTimeOffset.UtcNow : state.createdAtUtc);
                    if (!UnityTestOperationManager.IsTestRunActive())
                    {
                        Manager.Fail(taskId, new AgentTaskError("AGENT_TASK_RECOVERY_STALE", "The Unity Test Runner no longer has an active run handle after domain reload."));
                        var stale = new ActiveRun { TaskId = taskId, Command = command, Settings = settings, StartedAt = state.startedAtUtc, Deadline = state.deadlineUtc, ResultWritten = false };
                        Runs[command.commandId] = stale;
                        WriteTerminalResult(stale, AgentTaskToolResultAdapter.FromSnapshot(Manager.Get(taskId), command, command.tool));
                        continue;
                    }
                    task.Reattach(Manager.GetContext(taskId), state.runGuid);
                    Runs[command.commandId] = new ActiveRun { TaskId = taskId, TestTask = task, Command = command, Settings = settings, StartedAt = state.startedAtUtc, Deadline = state.deadlineUtc };
                }
            }
            catch { }
        }

        private static void Persist(ActiveRun run)
        {
            try
            {
                var root = Directory.GetParent(Application.dataPath).FullName;
                var path = Path.Combine(root, run.Settings.tempRoot.Replace('/', Path.DirectorySeparatorChar), "processing", run.Command.commandId + ".task.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var payload = new ActiveRunState
                {
                    commandId = run.Command.commandId, agentTaskId = run.TaskId.Value, commandJson = JsonConvert.SerializeObject(run.Command),
                    runGuid = run.TestTask?.RunGuid,
                    effectiveTimeoutMs = (int)Math.Max(0, (run.Deadline - run.StartedAt).TotalMilliseconds),
                    createdAtUtc = Manager.Get(run.TaskId).CreatedAt, startedAtUtc = run.StartedAt, deadlineUtc = run.Deadline
                };
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(payload), Utf8NoBom);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch { }
        }

        private static void DeleteState(string commandId, string tempRoot)
        {
            try
            {
                var root = Directory.GetParent(Application.dataPath).FullName;
                var path = Path.Combine(root, tempRoot.Replace('/', Path.DirectorySeparatorChar), "processing", commandId + ".task.json");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        [Serializable]
        private sealed class ActiveRunState
        {
            public string commandId;
            public string agentTaskId;
            public string commandJson;
            public string runGuid;
            public int effectiveTimeoutMs;
            public DateTimeOffset createdAtUtc;
            public DateTimeOffset startedAtUtc;
            public DateTimeOffset deadlineUtc;
        }

        private sealed class TimedEditModeTask : IAgentTask
        {
            private readonly IAgentTask inner;
            private readonly int timeoutMs;
            private DateTimeOffset startedAt;
            public TimedEditModeTask(IAgentTask inner, int timeoutMs, DateTimeOffset? restoredStart = null) { this.inner = inner; this.timeoutMs = timeoutMs; startedAt = restoredStart ?? default(DateTimeOffset); }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context)
            {
                if (startedAt == default(DateTimeOffset)) startedAt = DateTimeOffset.UtcNow;
                inner.Start(context);
            }
            public void Advance(IAgentTaskContext context)
            {
                if (timeoutMs > 0 && DateTimeOffset.UtcNow - startedAt > TimeSpan.FromMilliseconds(timeoutMs))
                {
                    context.Fail(new AgentTaskError("AGENT_TASK_TIMEOUT", "EditMode test task timed out."));
                    return;
                }
                inner.Advance(context);
            }
            public void RequestCancellation(IAgentTaskContext context) => inner.RequestCancellation(context);
        }
    }
}
