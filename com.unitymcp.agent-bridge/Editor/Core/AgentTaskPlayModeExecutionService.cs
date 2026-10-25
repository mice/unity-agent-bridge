using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using UnityMcp.AgentTaskEditor;

namespace UnityMcp.AgentBridge
{
    /// <summary>Owns project-local PlayMode AgentTasks created through the control protocol.</summary>
    [InitializeOnLoad]
    public static class AgentTaskPlayModeExecutionService
    {
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("playmode-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<AgentTaskId, IAgentTask> Tasks = new Dictionary<AgentTaskId, IAgentTask>();
        private static readonly Dictionary<AgentTaskId, AgentCommand> Commands = new Dictionary<AgentTaskId, AgentCommand>();
        private static readonly Dictionary<AgentTaskId, DateTimeOffset> Deadlines = new Dictionary<AgentTaskId, DateTimeOffset>();

        static AgentTaskPlayModeExecutionService()
        {
            RestorePersistedTasks();
            EditorApplication.update += AdvanceTasks;
        }

        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            if (request.Operation == AgentTaskControlProtocol.Create) return Create(request);
            AgentTaskId id;
            try { id = new AgentTaskId(request.AgentTaskId); } catch (ArgumentException) { return Error("INVALID_TASK_ID", "Agent task ID is required."); }
            var retained = AgentTaskDurableResults.Read(id.Value, request.Operation == AgentTaskControlProtocol.Cancel);
            if (retained != null) return retained;
            if (request.Operation == AgentTaskControlProtocol.Cancel)
            {
                var disposition = EditorTaskScheduling.CancelTask(Manager, id);
                return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = Manager.Get(id), AgentTaskId = id.Value };
            }
            var snapshot = Manager.Get(id);
            return snapshot == null ? Error("NOT_FOUND", "PlayMode task was not found.") : Project(snapshot, id.Value);
        }

        private static AgentTaskControlResponse Create(AgentTaskControlRequest request)
        {
            if (!string.Equals(request.TaskType, "run_playmode_tests", StringComparison.Ordinal)) return Error("UNKNOWN_TASK_TYPE", "Unsupported PlayMode task type.");
            if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(request.Payload ?? "{}", out var args, out var failure)) return Error("INVALID_ARGS", failure.summary);
            var validation = UnityTestOperationManager.ValidateRunArgsForAgentTask(args);
            if (validation != null) return Error("INVALID_ARGS", validation.summary);
            if (EditorApplication.isPlaying && !UnityTestOperationManager.IsTestRunActive() && !EditorApplication.isCompiling && EditorApplication.isPlayingOrWillChangePlaymode) return Error("TASK_BLOCKED", "PlayMode tests require a compatible stable Editor mode.");
            var command = EditorTaskScheduling.TaskCommand("agent-task-playmode-" + Guid.NewGuid().ToString("N"), "unity.run_playmode_tests", request.TimeoutMs > 0 ? request.TimeoutMs : 120000, request.Payload, request.SubmittedAtUtc);
            var task = new RunPlayModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), UnityTestOperationManager.CreateRunnerFilter(TestMode.PlayMode, args), guid => EditorTaskScheduling.NotifyTestStarted(command.commandId, guid));
            var id = Manager.Register(task);
            Tasks[id] = task;
            Commands[id] = command;
            Deadlines[id] = DateTimeOffset.UtcNow.AddMilliseconds(request.TimeoutMs > 0 ? request.TimeoutMs : 120000);
            Persist(id, request.Payload ?? "{}");
            Schedule(id, task, command);
            return Project(Manager.Get(id), id.Value);
        }

        private static void Schedule(AgentTaskId id, RunPlayModeTestsTask task, AgentCommand command)
        {
            var deniedBeforeStart = false;
            EditorTaskScheduling.ScheduleTask(Manager, id, command, "run_playmode_tests", remaining =>
            {
                if (EditorApplication.isPlaying)
                {
                    deniedBeforeStart = true;
                    Manager.RejectBeforeStart(id, new AgentTaskError("TASK_BLOCKED", "PlayMode tests require EditMode before startup."));
                    return;
                }
                command.timeoutMs = remaining;
                Deadlines[id] = DateTimeOffset.UtcNow.AddMilliseconds(remaining);
                Persist(id, command.rawArgsJson);
                EditorTaskScheduling.StartTask(Manager, id);
                Persist(id, command.rawArgsJson);
            }, () => deniedBeforeStart ? "Mode validation rejected execution before driver Start." : task.ExecutionEnded ? "Original PlayMode runner finished or failed before start." : null,
                () => Project(Manager.Get(id), id.Value));
        }

        private static void AdvanceTasks()
        {
            foreach (var id in Tasks.Keys.ToList())
            {
                var snapshot = Manager.Get(id);
                if (snapshot != null && snapshot.State == AgentTaskState.Running)
                {
                    if (Deadlines.TryGetValue(id, out var deadline) && DateTimeOffset.UtcNow >= deadline)
                        Manager.Fail(id, new AgentTaskError("AGENT_TASK_TIMEOUT", "PlayMode task exceeded its execution deadline."));
                    else Manager.Advance(id);
                }
                snapshot = Manager.Get(id);
                if (snapshot != null && snapshot.IsTerminal)
                {
                    AgentTaskDurableResults.Save(Project(snapshot, id.Value));
                    Tasks.Remove(id);
                    Deadlines.Remove(id);
                    Commands.Remove(id);
                    DeletePersisted(id);
                }
            }
            Manager.Cleanup();
        }

        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id) => EditorTaskScheduling.Observe(AgentTaskDurableResults.Save(new AgentTaskControlResponse { Success = true, Snapshot = snapshot, AgentTaskId = id, ErrorCode = snapshot?.Error?.Code, ErrorMessage = snapshot?.Error?.Message, ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload) }));
        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };

        private static string StateDirectory
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                return root == null ? null : Path.Combine(root, "Library", "AgentBridge", "agent-task-playmode");
            }
        }

        private static string StatePath(AgentTaskId id) => Path.Combine(StateDirectory ?? string.Empty, id.Value + ".playmode.task.json");

        private static void Persist(AgentTaskId id, string payload)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                AgentTaskDurableResults.WriteAtomic(StatePath(id), JsonConvert.SerializeObject(new PersistedTask { version = 1, agentTaskId = id.Value, payload = payload, createdAt = Manager.Get(id).CreatedAt, command = Commands[id], deadline = Deadlines[id], editorSession = AgentTaskDurableResults.EditorSession, runGuid = (Tasks[id] as RunPlayModeTestsTask)?.RunGuid }));
            }
            catch (Exception exception) { throw new IOException("AGENT_TASK_PLAYMODE_PERSIST_FAILED", exception); }
        }

        private static void DeletePersisted(AgentTaskId id)
        {
            try { if (File.Exists(StatePath(id))) File.Delete(StatePath(id)); } catch { }
        }

        private static void RestorePersistedTasks()
        {
            try
            {
                AgentTaskDurableResults.ImportLegacy("agent-task-playmode", "*.playmode.task.json", Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "AgentBridge", "processing"));
                foreach (var path in Directory.GetFiles(StateDirectory, "*.playmode.task.json"))
                {
                    var state = JsonConvert.DeserializeObject<PersistedTask>(File.ReadAllText(path));
                    if (state == null || string.IsNullOrWhiteSpace(state.agentTaskId)) continue;
                    var id = new AgentTaskId(state.agentTaskId);
                    if (AgentTaskDurableResults.Read(id.Value) != null) { File.Delete(path); continue; }
                    if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(state.payload ?? "{}", out var args, out _)) args = new UnityTestRunArgs();
                    var command = state.command ?? EditorTaskScheduling.TaskCommand("agent-task-" + id.Value, "unity.run_playmode_tests", 120000, state.payload);
                    var task = new RunPlayModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), UnityTestOperationManager.CreateRunnerFilter(TestMode.PlayMode, args), guid => EditorTaskScheduling.NotifyTestStarted(command.commandId, guid));
                    var scheduled = EditorTaskScheduling.FindTask(id.Value);
                    Tasks[id] = task; Commands[id] = command; Deadlines[id] = state.deadline;
                    if (scheduled != null && !scheduled.StartedAt.HasValue)
                    {
                        Manager.RestoreCreated(id, task, state.createdAt);
                        Schedule(id, task, command);
                    }
                    else
                    {
                        Manager.RestoreRunning(id, task, state.createdAt == default(DateTimeOffset) ? DateTimeOffset.UtcNow : state.createdAt);
                        Schedule(id, task, command);
                        if (state.editorSession == AgentTaskDurableResults.EditorSession && AgentTaskEditModeExecutionService.CanReattachRun(state.runGuid))
                            task.Reattach(Manager.GetContext(id), state.runGuid);
                        else Manager.Fail(id, new AgentTaskError("AGENT_TASK_RECOVERY_STALE", "The original PlayMode Test Runner handle is unavailable; execution was not restarted."));
                    }
                }
            }
            catch (Exception exception) { Debug.LogWarning("AGENT_TASK_PLAYMODE_RESTORE_FAILED: " + exception.Message); }
        }

        [Serializable]
        private sealed class PersistedTask
        {
            public int version;
            public DateTimeOffset createdAt;
            public string agentTaskId;
            public string payload;
            public AgentCommand command;
            public DateTimeOffset deadline;
            public string editorSession;
            public string runGuid;
        }
    }
}
