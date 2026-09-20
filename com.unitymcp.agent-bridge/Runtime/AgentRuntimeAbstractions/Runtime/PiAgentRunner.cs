using System;

namespace UnityMcp.AgentRuntime
{
    public sealed class PiAgentRunnerOptions
    {
        public string ExecutablePath { get; set; } = "pi";
        public int TimeoutMilliseconds { get; set; } = 120000;
        public string Model { get; set; }
    }

    public interface IPiProcess
    {
        string WorkingDirectory { get; set; }
        string Arguments { get; set; }
        bool HasExited { get; }
        int ExitCode { get; }
        string StandardOutput { get; }
        string StandardError { get; }
        void Start();
        void Kill();
    }

    public interface IPiProcessFactory
    {
        IPiProcess Create(string executablePath);
    }

    public sealed class PiAgentRunner : IAgentRunner
    {
        private readonly PiAgentRunnerOptions options;
        private readonly IPiProcessFactory processFactory;
        private IPiProcess process;
        private DateTime startedAt;
        private bool finished;

        public PiAgentRunner(PiAgentRunnerOptions options = null, IPiProcessFactory processFactory = null)
        {
            this.options = options ?? new PiAgentRunnerOptions();
            this.processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory), "A platform Pi process factory is required.");
            if (this.options.TimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Pi timeout must be positive.");
        }

        public bool CanCancel => true;

        public void Start(AgentRunRequest request, IAgentRunnerContext context)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (context == null) throw new ArgumentNullException(nameof(context));
            process = processFactory.Create(options.ExecutablePath);
            process.WorkingDirectory = request.WorkingDirectory ?? string.Empty;
            process.Arguments = BuildArguments(request.Task, options.Model);
            startedAt = DateTime.UtcNow;
            finished = false;
            try { process.Start(); }
            catch (Exception exception) { finished = true; context.Fail(new AgentRunError("PI_START_FAILED", exception.Message, exception)); }
        }

        public void Advance(IAgentRunnerContext context)
        {
            if (finished || process == null) return;
            if (context.CancellationRequested)
            {
                process.Kill();
                finished = true;
                context.Cancel(AgentRunResult.Failed(new AgentRunError("PI_CANCELLED", "Pi execution was cancelled.")));
                return;
            }
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > options.TimeoutMilliseconds)
            {
                process.Kill();
                finished = true;
                context.Fail(new AgentRunError("PI_TIMEOUT", "Pi execution exceeded its timeout."));
                return;
            }
            if (!process.HasExited) return;
            finished = true;
            var output = process.StandardOutput;
            var error = process.StandardError;
            if (process.ExitCode == 0)
                context.Complete(AgentRunResult.Succeeded(output));
            else
                context.Fail(new AgentRunError("PI_EXIT_NONZERO", "Pi exited with code " + process.ExitCode + ": " + error));
        }

        public void RequestCancellation(IAgentRunnerContext context)
        {
            if (process == null || finished) return;
            process.Kill();
            finished = true;
            context.Cancel(AgentRunResult.Failed(new AgentRunError("PI_CANCELLED", "Pi execution was cancelled.")));
        }

        private static string BuildArguments(string task, string model)
        {
            var escaped = task.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
            var arguments = "-p \"" + escaped + "\" --no-tools --no-session --mode text";
            if (!string.IsNullOrWhiteSpace(model)) arguments += " --model \"" + model.Replace("\"", "\\\"") + "\"";
            return arguments;
        }
    }
}
