using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public interface IAsyncProcessRunner
    {
        Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken);
    }

    public sealed class ProcessExecutionRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; set; } = new string[0];
        public string WorkingDirectory { get; set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
        public System.TimeSpan Timeout { get; set; }
        public ProcessCancellationMode CancellationMode { get; set; } = ProcessCancellationMode.Unspecified;
        public System.TimeSpan TerminateGracePeriod { get; set; } = System.TimeSpan.FromSeconds(2);
    }

    public sealed class ProcessExecutionResult
    {
        public ProcessOutcome Outcome { get; set; }
        public int? ExitCode { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
        public System.TimeSpan Duration { get; set; }
    }

    public enum ProcessCancellationMode
    {
        Unspecified = 0,
        DetachOnCancel = 1,
        TerminateOnCancel = 2,
    }

    public enum ProcessOutcome
    {
        Completed,
        TimedOut,
        Detached,
        Terminated,
        Failed,
    }
}
