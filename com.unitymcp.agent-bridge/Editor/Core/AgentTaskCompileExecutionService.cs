using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>Preserves the AgentTask/command identity while the compile manager owns its epoch.</summary>
    [InitializeOnLoad]
    public static class AgentTaskCompileExecutionService
    {
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("compile-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<string, AgentTaskId> Tasks = new Dictionary<string, AgentTaskId>(StringComparer.Ordinal);
        private static double nextCleanup;

        static AgentTaskCompileExecutionService()
        {
            Restore();
            EditorApplication.update += AdvanceTasks;
        }

        private static void AdvanceTasks()
        {
            foreach (var pair in Tasks.ToList())
            {
                var snapshot = Manager.Get(pair.Value);
                if (snapshot != null && snapshot.State == AgentTaskState.Running) Manager.Advance(pair.Value);
                snapshot = Manager.Get(pair.Value);
                if (snapshot != null && snapshot.IsTerminal)
                {
                    AgentTaskDurableResults.Save(Project(snapshot, pair.Value.Value));
                    File.Delete(StatePath(pair.Key));
                    Tasks.Remove(pair.Key);
                }
            }
            if (EditorApplication.timeSinceStartup >= nextCleanup)
            {
                nextCleanup = EditorApplication.timeSinceStartup + 30;
                Manager.Cleanup();
            }
        }

        public static AgentTaskControlResponse Create(AgentTaskControlRequest request)
        {
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            if (!string.Equals(request.TaskType, "compile", StringComparison.Ordinal)) return Error("UNKNOWN_TASK_TYPE", "Unsupported compile task type.");
            if (!JsonUtil.TryDeserializeArgs<UnityCompileArgs>(request.Payload ?? "{}", out _, out var failure)) return Error("INVALID_ARGS", failure.summary);
            var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120000;
            var commandId = "agent-task-compile-" + Guid.NewGuid().ToString("N");
            var command = EditorTaskScheduling.TaskCommand(commandId, "unity.compile", timeoutMs, request.Payload, request.SubmittedAtUtc);
            var settings = AgentBridgeSettingsLoader.Load().Settings;
            var id = Manager.Register(new CompileTask(command, settings, false));
            // Persist identity before RequestScriptCompilation can reload the domain.
            AgentTaskDurableResults.WriteAtomic(StatePath(commandId), JsonConvert.SerializeObject(new PersistedCompile
            { version = 1, taskId = id.Value, command = command, createdAt = Manager.Get(id).CreatedAt }));
            Tasks[commandId] = id;
            Schedule(id, command);
            return Project(Manager.Get(id), id.Value);
        }

        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            if (request?.Operation == AgentTaskControlProtocol.Create) return Create(request);
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            AgentTaskId id;
            try { id = new AgentTaskId(request.AgentTaskId); }
            catch (ArgumentException) { return Error("INVALID_TASK_ID", "Agent task ID is required."); }
            var retained = AgentTaskDurableResults.Read(id.Value, request.Operation == AgentTaskControlProtocol.Cancel);
            if (retained != null) return retained;
            if (request.Operation == AgentTaskControlProtocol.Cancel)
            {
                var disposition = EditorTaskScheduling.CancelTask(Manager, id);
                return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = Manager.Get(id), AgentTaskId = id.Value };
            }
            var snapshot = Manager.Get(id);
            return snapshot == null ? Error("NOT_FOUND", "Compile task was not found.") : Project(snapshot, id.Value);
        }

        private static void Schedule(AgentTaskId id, AgentCommand command)
        {
            var deniedBeforeStart = false;
            EditorTaskScheduling.ScheduleTask(Manager, id, command, "compile", remaining =>
            {
                if (EditorApplication.isPlaying)
                {
                    deniedBeforeStart = true;
                    Manager.RejectBeforeStart(id, new AgentTaskError("TASK_BLOCKED", "Compilation requires a compatible EditMode state."));
                    return;
                }
                command.timeoutMs = remaining;
                AgentTaskDurableResults.WriteAtomic(StatePath(command.commandId), JsonConvert.SerializeObject(new PersistedCompile
                { version = 1, taskId = id.Value, command = command, createdAt = Manager.Get(id).CreatedAt }));
                EditorTaskScheduling.StartTask(Manager, id);
            }, () => deniedBeforeStart ? "Compile mode validation rejected before driver Start." : null, () => Project(Manager.Get(id), id.Value));
        }

        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id) => EditorTaskScheduling.Observe(AgentTaskDurableResults.Save(new AgentTaskControlResponse
        {
            Success = true, Snapshot = snapshot, AgentTaskId = id,
            ErrorCode = snapshot?.Error?.Code, ErrorMessage = snapshot?.Error?.Message,
            ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload)
        }));

        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };
        private static string StatePath(string command) => Path.Combine(AgentTaskDurableResults.Root, "agent-task-compile", command + ".json");

        private static void Restore()
        {
            var directory = Path.Combine(AgentTaskDurableResults.Root, "agent-task-compile");
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var state = JsonConvert.DeserializeObject<PersistedCompile>(File.ReadAllText(path));
                    if (state == null || state.version != 1 || state.command == null || string.IsNullOrWhiteSpace(state.taskId)) throw new InvalidDataException("Invalid compile task record.");
                    var id = new AgentTaskId(state.taskId);
                    if (AgentTaskDurableResults.Read(id.Value) != null) { File.Delete(path); continue; }
                    var queued = EditorTaskScheduling.FindTask(id.Value)?.Phase == AgentExecutionPhase.Queued;
                    var task = new CompileTask(state.command, AgentBridgeSettingsLoader.Load().Settings, !queued);
                    if (queued) Manager.RestoreCreated(id, task, state.createdAt);
                    else Manager.RestoreRunning(id, task, state.createdAt);
                    Tasks[state.command.commandId] = id;
                    Schedule(id, state.command);
                }
                catch (Exception exception) { UnityEngine.Debug.LogWarning("AGENT_TASK_COMPILE_RESTORE_FAILED: " + exception.Message); }
            }
        }

        [Serializable]
        private sealed class PersistedCompile
        {
            public int version;
            public string taskId;
            public AgentCommand command;
            public DateTimeOffset createdAt;
        }

        private sealed class CompileTask : IAgentTask
        {
            private readonly AgentCommand command;
            private readonly AgentBridgeSettings settings;
            private bool started;
            public CompileTask(AgentCommand command, AgentBridgeSettings settings, bool restored)
            { this.command = command; this.settings = settings; started = restored; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) => Observe(context);
            public void Advance(IAgentTaskContext context) => Observe(context);
            public void RequestCancellation(IAgentTaskContext context) => throw new NotSupportedException("Unity compilation cancellation is not supported.");

            private void Observe(IAgentTaskContext context)
            {
                var result = UnityCompileOperationManager.ReadCompletedResult(command.commandId);
                if (result == null && !started)
                {
                    started = true;
                    result = UnityCompileOperationManager.StartOrResume(command, settings);
                }
                else if (result == null && !UnityCompileOperationManager.HasOperation(command.commandId))
                {
                    context.Fail(new AgentTaskError("AGENT_TASK_RECOVERY_STALE", "Compile identity was retained but its epoch/result is unavailable; compilation was not restarted."));
                    return;
                }
                if (result == null || result.status == ToolResultStatus.Running || result.status == ToolResultStatus.Pending || result.status == ToolResultStatus.Resuming) return;
                if (result.status == ToolResultStatus.Timeout) context.Complete(AgentTaskResult.TimedOut(result.summary, result));
                else if (result.success) context.Complete(AgentTaskResult.Succeeded(result, result.summary));
                else context.Complete(AgentTaskResult.DomainFailure(result.summary ?? "Compilation failed.", result));
            }
        }
    }
}
