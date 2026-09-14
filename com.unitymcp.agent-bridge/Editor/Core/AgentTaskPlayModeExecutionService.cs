using System;
using System.Collections.Generic;
using System.Linq;
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

        static AgentTaskPlayModeExecutionService() => EditorApplication.update += AdvanceTasks;

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
            return Project(Manager.Get(id), id.Value);
        }

        private static void AdvanceTasks()
        {
            foreach (var id in Tasks.Keys.ToList())
            {
                var snapshot = Manager.Get(id);
                if (snapshot != null && snapshot.State == AgentTaskState.Running) Manager.Advance(id);
                if (snapshot != null && snapshot.IsTerminal) Tasks.Remove(id);
            }
        }

        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id) => new AgentTaskControlResponse { Success = true, Snapshot = snapshot, AgentTaskId = id, ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload) };
        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };
    }
}
