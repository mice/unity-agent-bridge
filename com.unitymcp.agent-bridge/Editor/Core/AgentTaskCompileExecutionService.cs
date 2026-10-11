using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>Adapts the durable compile operation manager to the AgentTask lifecycle.</summary>
    public static class AgentTaskCompileExecutionService
    {
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("compile-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<string, AgentTaskId> Tasks = new Dictionary<string, AgentTaskId>(StringComparer.Ordinal);

        static AgentTaskCompileExecutionService() => EditorApplication.update += AdvanceTasks;

        private static void AdvanceTasks()
        {
            foreach (var id in Tasks.Values.ToList())
            {
                var snapshot = Manager.Get(id);
                if (snapshot != null && snapshot.State == AgentTaskState.Running) Manager.Advance(id);
            }
        }

        public static AgentTaskControlResponse Create(AgentTaskControlRequest request)
        {
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            if (!string.Equals(request.TaskType, "compile", StringComparison.Ordinal)) return Error("UNKNOWN_TASK_TYPE", "Unsupported compile task type.");
            var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : 120000;
            var commandId = "agent-task-compile-" + Guid.NewGuid().ToString("N");
            var command = new AgentCommand { commandId = commandId, tool = "unity.compile", timeoutMs = timeoutMs, rawArgsJson = request.Payload ?? "{}" };
            var settings = AgentBridgeSettingsLoader.Load().Settings;
            var id = Manager.Register(new CompileTask(command, settings));
            Tasks[commandId] = id;
            Manager.Start(id);
            return Project(Manager.Get(id), id.Value);
        }

        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            if (request?.Operation == AgentTaskControlProtocol.Create) return Create(request);
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            AgentTaskId id;
            try { id = new AgentTaskId(request.AgentTaskId); }
            catch (ArgumentException) { return Error("INVALID_TASK_ID", "Agent task ID is required."); }
            if (request.Operation == AgentTaskControlProtocol.Cancel)
            {
                var disposition = Manager.Cancel(id);
                return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = Manager.Get(id), AgentTaskId = id.Value };
            }
            var snapshot = Manager.Get(id);
            return snapshot == null ? Error("NOT_FOUND", "Compile task was not found.") : Project(snapshot, id.Value);
        }

        private static AgentTaskControlResponse Project(AgentTaskSnapshot snapshot, string id) => new AgentTaskControlResponse
        {
            Success = true,
            Snapshot = snapshot,
            AgentTaskId = id,
            ResultPayloadJson = snapshot?.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload)
        };

        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };

        private sealed class CompileTask : IAgentTask
        {
            private readonly AgentCommand command;
            private readonly AgentBridgeSettings settings;
            public CompileTask(AgentCommand command, AgentBridgeSettings settings) { this.command = command; this.settings = settings; }
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) => Observe(context);
            public void Advance(IAgentTaskContext context) => Observe(context);
            public void RequestCancellation(IAgentTaskContext context) => throw new NotSupportedException("Unity compilation cancellation is not supported.");

            private void Observe(IAgentTaskContext context)
            {
                var result = UnityCompileOperationManager.StartOrResume(command, settings);
                if (result == null || result.status == ToolResultStatus.Running || result.status == ToolResultStatus.Pending || result.status == ToolResultStatus.Resuming) return;
                if (result.status == ToolResultStatus.Timeout)
                {
                    context.Complete(AgentTaskResult.TimedOut(result.summary));
                    return;
                }
                if (result.success) context.Complete(AgentTaskResult.Succeeded(result, result.summary));
                else context.Complete(AgentTaskResult.DomainFailure(result.summary ?? "Compilation failed.", result));
            }
        }
    }
}
