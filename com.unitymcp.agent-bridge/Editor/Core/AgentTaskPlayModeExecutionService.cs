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
    public static class AgentTaskPlayModeExecutionService
    {
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("playmode-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<AgentTaskId, IAgentTask> Tasks = new Dictionary<AgentTaskId, IAgentTask>();

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
            if (request.Operation == AgentTaskControlProtocol.Cancel)
            {
                var disposition = Manager.Cancel(id);
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
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || UnityTestOperationManager.IsTestRunActive()) return Error("TASK_BLOCKED", "PlayMode tests cannot start in the current Editor state.");
            var task = new RunPlayModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), UnityTestOperationManager.CreateRunnerFilter(TestMode.PlayMode, args));
            var id = Manager.Register(task);
            Tasks[id] = task;
            Manager.Start(id);
                Persist(id, request.Payload ?? "{}");
            return Project(Manager.Get(id), id.Value);
        }

        private static void AdvanceTasks()
        {
            foreach (var id in Tasks.Keys.ToList())
            {
                var snapshot = Manager.Get(id);
                if (snapshot != null && snapshot.State == AgentTaskState.Running) Manager.Advance(id);
                if (snapshot != null && snapshot.IsTerminal) Tasks.Remove(id);
                if (snapshot != null && snapshot.IsTerminal) DeletePersisted(id);
            }
        }

        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id) => new AgentTaskControlResponse { Success = true, Snapshot = snapshot, AgentTaskId = id, ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload) };
        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };

        private static string StateDirectory
        {
            get
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                return root == null ? null : Path.Combine(root, "Temp", "AgentBridge", "processing");
            }
        }

        private static string StatePath(AgentTaskId id) => Path.Combine(StateDirectory ?? string.Empty, id.Value + ".playmode.task.json");

        private static void Persist(AgentTaskId id, string payload)
        {
            try
            {
                Directory.CreateDirectory(StateDirectory);
                File.WriteAllText(StatePath(id), JsonConvert.SerializeObject(new PersistedTask { agentTaskId = id.Value, payload = payload }));
            }
            catch { }
        }

        private static void DeletePersisted(AgentTaskId id)
        {
            try { if (File.Exists(StatePath(id))) File.Delete(StatePath(id)); } catch { }
        }

        private static void RestorePersistedTasks()
        {
            try
            {
                if (!Directory.Exists(StateDirectory)) return;
                foreach (var path in Directory.GetFiles(StateDirectory, "*.playmode.task.json"))
                {
                    var state = JsonConvert.DeserializeObject<PersistedTask>(File.ReadAllText(path));
                    if (state == null || string.IsNullOrWhiteSpace(state.agentTaskId)) continue;
                    var id = new AgentTaskId(state.agentTaskId);
                    if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(state.payload ?? "{}", out var args, out _)) args = new UnityTestRunArgs();
                    var task = new RunPlayModeTestsTask(ScriptableObject.CreateInstance<TestRunnerApi>(), UnityTestOperationManager.CreateRunnerFilter(TestMode.PlayMode, args));
                    Manager.RestoreRunning(id, task, DateTimeOffset.UtcNow);
                    Manager.Fail(id, new AgentTaskError("AGENT_TASK_RECOVERY_STALE", "The PlayMode Test Runner handle was not available after domain reload."));
                    DeletePersisted(id);
                }
            }
            catch { }
        }

        [Serializable]
        private sealed class PersistedTask
        {
            public string agentTaskId;
            public string payload;
        }
    }
}
