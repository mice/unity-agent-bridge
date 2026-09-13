using System;

namespace UnityMcp.AgentTaskCore
{
    public interface IExternalTaskIdGenerator
    {
        string Create();
    }

    public sealed class GuidExternalTaskIdGenerator : IExternalTaskIdGenerator
    {
        public string Create() => Guid.NewGuid().ToString("N");
    }

    public sealed class AgentTaskProjectionAdapter
    {
        private readonly AgentTaskControlDispatcher dispatcher;
        private readonly IAgentTaskBindingStore bindings;
        private readonly IExternalTaskIdGenerator idGenerator;
        private readonly string projectIdentity;

        public AgentTaskProjectionAdapter(AgentTaskControlDispatcher dispatcher, IAgentTaskBindingStore bindings,
            string projectIdentity, IExternalTaskIdGenerator idGenerator = null)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            this.projectIdentity = string.IsNullOrWhiteSpace(projectIdentity) ? throw new ArgumentException("Project identity is required.", nameof(projectIdentity)) : projectIdentity;
            this.idGenerator = idGenerator ?? new GuidExternalTaskIdGenerator();
        }

        public AgentTaskControlResponse Create(string taskType, string payload, string externalTaskId = null, string commandId = null)
        {
            externalTaskId = string.IsNullOrWhiteSpace(externalTaskId) ? idGenerator.Create() : externalTaskId;
            var response = dispatcher.Dispatch(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Create,
                ProjectIdentity = projectIdentity,
                TaskType = taskType,
                Payload = payload
            });
            if (!response.Success || response.Snapshot == null) return response;
            bindings.Save(new AgentTaskBinding
            {
                ProjectIdentity = projectIdentity,
                ExternalTaskId = externalTaskId,
                AgentTaskId = response.Snapshot.Id.Value,
                CommandId = commandId
            });
            return response;
        }

        public AgentTaskControlResponse Get(string externalTaskId)
        {
            var binding = bindings.Load(projectIdentity, externalTaskId);
            if (binding == null) return Error("NOT_FOUND", "External task ID was not found in this project.");
            var response = dispatcher.Dispatch(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Query,
                ProjectIdentity = projectIdentity,
                AgentTaskId = binding.AgentTaskId
            });
            return response.ErrorCode == "NOT_FOUND" ? Error("STALE_BINDING", "External task binding points to a missing agent task.") : response;
        }

        public AgentTaskControlResponse Cancel(string externalTaskId)
        {
            var binding = bindings.Load(projectIdentity, externalTaskId);
            if (binding == null) return Error("NOT_FOUND", "External task ID was not found in this project.");
            var response = dispatcher.Dispatch(new AgentTaskControlRequest
            {
                Operation = AgentTaskControlProtocol.Cancel,
                ProjectIdentity = projectIdentity,
                AgentTaskId = binding.AgentTaskId
            });
            return response.ErrorCode == "NOT_FOUND" ? Error("STALE_BINDING", "External task binding points to a missing agent task.") : response;
        }

        private static AgentTaskControlResponse Error(string code, string message) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message };
    }
}
