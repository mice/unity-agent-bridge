using System;
using System.Collections.Generic;

namespace UnityMcp.AgentTaskCore
{
    public interface IAgentTaskFactory
    {
        IAgentTask Create(string taskType, string payload);
    }

    public sealed class AgentTaskControlDispatcher
    {
        private readonly IAgentTaskManager manager;
        private readonly IAgentTaskFactory factory;
        private readonly string projectIdentity;

        public AgentTaskControlDispatcher(IAgentTaskManager manager, IAgentTaskFactory factory, string projectIdentity)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.projectIdentity = string.IsNullOrWhiteSpace(projectIdentity) ? throw new ArgumentException("Project identity is required.", nameof(projectIdentity)) : projectIdentity;
        }

        public AgentTaskControlResponse Dispatch(AgentTaskControlRequest request)
        {
            if (request == null) return Error("INVALID_REQUEST", "Request is required.");
            if (request.Version != AgentTaskControlProtocol.CurrentVersion) return Error("UNSUPPORTED_VERSION", "Unsupported task control version.");
            if (!string.Equals(projectIdentity, request.ProjectIdentity, StringComparison.Ordinal)) return Error("PROJECT_CONFLICT", "Project identity does not match.");
            try
            {
                switch (request.Operation)
                {
                    case AgentTaskControlProtocol.Create: return Create(request);
                    case AgentTaskControlProtocol.Query: return Query(request);
                    case AgentTaskControlProtocol.Result: return Query(request);
                    case AgentTaskControlProtocol.Cancel: return Cancel(request);
                    default: return Error("INVALID_OPERATION", "Unsupported task control operation.");
                }
            }
            catch (KeyNotFoundException exception) { return Error("NOT_FOUND", exception.Message); }
            catch (Exception exception) { return Error("TASK_CONTROL_FAILURE", exception.Message); }
        }

        private AgentTaskControlResponse Create(AgentTaskControlRequest request)
        {
            var task = factory.Create(request.TaskType, request.Payload);
            if (task == null) return Error("UNKNOWN_TASK_TYPE", "Task factory returned no task.");
            var id = manager.Register(task);
            manager.Start(id);
            return Success(manager.Get(id));
        }

        private AgentTaskControlResponse Query(AgentTaskControlRequest request)
        {
            AgentTaskId id;
            if (!TryGetId(request, out id)) return Error("INVALID_TASK_ID", "Agent task ID is required.");
            var snapshot = manager.Get(id);
            return snapshot == null ? Error("NOT_FOUND", "Agent task was not found.") : Success(snapshot);
        }

        private AgentTaskControlResponse Cancel(AgentTaskControlRequest request)
        {
            AgentTaskId id;
            if (!TryGetId(request, out id)) return Error("INVALID_TASK_ID", "Agent task ID is required.");
            var disposition = manager.Cancel(id);
            return new AgentTaskControlResponse { Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition, Snapshot = manager.Get(id) };
        }

        private static bool TryGetId(AgentTaskControlRequest request, out AgentTaskId id)
        {
            try { id = new AgentTaskId(request.AgentTaskId); return true; }
            catch (ArgumentException) { id = default(AgentTaskId); return false; }
        }

        private static AgentTaskControlResponse Success(AgentTaskSnapshot snapshot) => new AgentTaskControlResponse { Success = true, Snapshot = snapshot };
        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };
    }
}
