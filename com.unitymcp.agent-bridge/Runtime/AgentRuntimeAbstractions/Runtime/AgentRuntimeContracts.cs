using System;

namespace UnityMcp.AgentRuntime
{
    public sealed class AgentRunRequest
    {
        public AgentRunRequest(string task, string workingDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(task)) throw new ArgumentException("Agent task text is required.", nameof(task));
            Task = task;
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
        }

        public string Task { get; }
        public string WorkingDirectory { get; }
    }

    public sealed class AgentRunError
    {
        public AgentRunError(string code, string message, Exception exception = null)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "AGENT_RUN_FAILURE" : code;
            Message = message ?? string.Empty;
            ExceptionType = exception == null ? null : exception.GetType().FullName;
        }

        public string Code { get; }
        public string Message { get; }
        public string ExceptionType { get; }
    }

    public sealed class AgentRunResult
    {
        private AgentRunResult(bool success, string output, AgentRunError error)
        {
            Success = success;
            Output = output ?? string.Empty;
            Error = error;
        }

        public bool Success { get; }
        public string Output { get; }
        public AgentRunError Error { get; }

        public static AgentRunResult Succeeded(string output = null) => new AgentRunResult(true, output, null);

        public static AgentRunResult Failed(AgentRunError error, string output = null)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new AgentRunResult(false, output, error);
        }
    }

    public interface IAgentRunner
    {
        bool CanCancel { get; }
        void Start(AgentRunRequest request, IAgentRunnerContext context);
        void Advance(IAgentRunnerContext context);
        void RequestCancellation(IAgentRunnerContext context);
    }

    public interface IAgentRunnerContext
    {
        AgentRunRequest Request { get; }
        bool CancellationRequested { get; }
        void Complete(AgentRunResult result);
        void Fail(AgentRunError error);
        void Cancel(AgentRunResult result = null);
    }
}
