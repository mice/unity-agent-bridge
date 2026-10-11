using System;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentRuntime
{
    public sealed class AgentExecutionHandle
    {
        internal AgentExecutionHandle(AgentTaskId taskId, AgentTaskSnapshot snapshot)
        {
            TaskId = taskId;
            Snapshot = snapshot;
        }

        public AgentTaskId TaskId { get; }
        public AgentTaskSnapshot Snapshot { get; }
    }

    public interface IAgentRunnerFactory
    {
        IAgentRunner Create(AgentRunRequest request);
    }

    public sealed class AgentService
    {
        private readonly IAgentTaskManager taskManager;
        private readonly IAgentRunnerFactory runnerFactory;

        public AgentService(IAgentTaskManager taskManager, IAgentRunnerFactory runnerFactory)
        {
            this.taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            this.runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        }

        public AgentExecutionHandle Run(AgentRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var runner = runnerFactory.Create(request);
            if (runner == null) throw new InvalidOperationException("Runner factory returned no runner.");
            var taskId = taskManager.Register(new AgentExecutionTask(request, runner));
            taskManager.Start(taskId);
            return new AgentExecutionHandle(taskId, taskManager.Get(taskId));
        }
    }
}
