using System;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentRuntime
{
    public sealed class AgentExecutionTask : IAgentTask, IAgentExecutionObservable
    {
        private readonly AgentRunRequest request;
        private readonly IAgentRunner runner;

        public AgentExecutionTask(AgentRunRequest request, IAgentRunner runner)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public AgentExecutionObservation Execution => (runner as IAgentExecutionObservable)?.Execution ?? AgentExecutionObservation.Unknown;
        public bool CanCancel => runner.CanCancel;
        public void Start(IAgentTaskContext context) => runner.Start(request, new RunnerContext(request, context));
        public void Advance(IAgentTaskContext context) => runner.Advance(new RunnerContext(request, context));
        public void RequestCancellation(IAgentTaskContext context) => runner.RequestCancellation(new RunnerContext(request, context));

        private sealed class RunnerContext : IAgentRunnerContext
        {
            private readonly AgentRunRequest request;
            private readonly IAgentTaskContext taskContext;

            public RunnerContext(AgentRunRequest request, IAgentTaskContext taskContext)
            {
                this.request = request;
                this.taskContext = taskContext ?? throw new ArgumentNullException(nameof(taskContext));
            }

            public AgentRunRequest Request => request;
            public bool CancellationRequested => taskContext.CancellationRequested;

            public void Complete(AgentRunResult result)
            {
                if (result == null) throw new ArgumentNullException(nameof(result));
                if (!result.Success)
                {
                    Fail(result.Error ?? new AgentRunError("AGENT_RUN_FAILURE", "Agent runner reported failure."));
                    return;
                }

                taskContext.Complete(AgentTaskResult.Succeeded(result));
            }

            public void Fail(AgentRunError error)
            {
                if (error == null) throw new ArgumentNullException(nameof(error));
                taskContext.Fail(new AgentTaskError(error.Code, error.Message));
            }

            public void Cancel(AgentRunResult result = null)
            {
                taskContext.Cancel(AgentTaskResult.Cancelled(result == null ? "Agent runner cancelled execution." : result.Output));
            }
        }
    }
}
