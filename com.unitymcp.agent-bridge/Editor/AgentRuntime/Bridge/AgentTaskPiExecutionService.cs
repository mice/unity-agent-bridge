using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    public static class AgentTaskPiExecutionService
    {
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("agent-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<string, AgentTaskId> Tasks = new Dictionary<string, AgentTaskId>(StringComparer.Ordinal);
        static AgentTaskPiExecutionService() => UnityEditor.EditorApplication.update += AdvanceTasks;
        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            if (request.Operation == AgentTaskControlProtocol.Create) return Create(request);
            if (!TryGetId(request.AgentTaskId, out var id)) return Error("INVALID_TASK_ID", "Agent task ID is required.");
            if (request.Operation == AgentTaskControlProtocol.Cancel) { var disposition = Manager.Cancel(id); return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = Manager.Get(id), AgentTaskId = id.Value }; }
            var snapshot = Manager.Get(id);
            return snapshot == null ? Error("NOT_FOUND", "Agent task was not found.") : Project(snapshot, id.Value);
        }
        private static AgentTaskControlResponse Create(AgentTaskControlRequest request)
        {
            AgentRunArguments arguments;
            try { arguments = JsonUtility.FromJson<AgentRunArguments>(request.Payload ?? "{}"); } catch (Exception exception) { return Error("INVALID_ARGS", exception.Message); }
            if (arguments == null || string.IsNullOrWhiteSpace(arguments.task)) return Error("INVALID_ARGS", "Agent task is required.");
            var directory = string.IsNullOrWhiteSpace(arguments.working_directory) ? System.IO.Directory.GetParent(Application.dataPath)?.FullName : arguments.working_directory;
            try
            {
                var settings = PiAgentRunnerProjectSettings.Load();
                var service = new AgentService(Manager, new PiAgentRunnerFactory(settings));
                var handle = service.Run(new AgentRunRequest(arguments.task, directory));
                Tasks[handle.TaskId.Value] = handle.TaskId;
                return Project(handle.Snapshot, handle.TaskId.Value);
            }
            catch (Exception exception) { return Error("AGENT_RUN_CREATE_FAILED", exception.Message); }
        }
        private static void AdvanceTasks() { foreach (var id in new List<AgentTaskId>(Tasks.Values)) { var snapshot = Manager.Get(id); if (snapshot != null && snapshot.State == AgentTaskState.Running) Manager.Advance(id); } }
        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id)
        {
            var result = snapshot?.Result?.Payload as AgentRunResult;
            return new AgentTaskControlResponse
            {
                Success = snapshot != null && snapshot.State != AgentTaskState.Failed,
                ErrorCode = snapshot?.Error?.Code,
                ErrorMessage = snapshot?.Error?.Message,
                Snapshot = snapshot,
                AgentTaskId = id,
                ResultPayloadJson = result == null ? null : JsonUtility.ToJson(new AgentRunPayload
                {
                    success = result.Success,
                    output = result.Output,
                    error = result.Error?.Message
                })
            };
        }
        private static bool TryGetId(string value, out AgentTaskId id) { try { id = new AgentTaskId(value); return true; } catch (ArgumentException) { id = default(AgentTaskId); return false; } }
        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };
        [Serializable] private sealed class AgentRunArguments { public string task; public string working_directory; }
        [Serializable] private sealed class AgentRunPayload { public bool success; public string output; public string error; }
    }

}
