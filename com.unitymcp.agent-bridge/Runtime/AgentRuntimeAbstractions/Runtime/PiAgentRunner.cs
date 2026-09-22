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
            finished = false;
            try
            {
                process = processFactory.Create(options.ExecutablePath);
                process.WorkingDirectory = request.WorkingDirectory ?? string.Empty;
                process.Arguments = BuildArguments(request.Task, options.Model);
                startedAt = DateTime.UtcNow;
                process.Start();
            }
            catch (Exception exception) { Finish(); context.Fail(new AgentRunError("PI_START_FAILED", exception.Message, exception)); }
        }

        public void Advance(IAgentRunnerContext context)
        {
            if (finished || process == null) return;
            try
            {
                if (context.CancellationRequested) { RequestCancellation(context); return; }
                if ((DateTime.UtcNow - startedAt).TotalMilliseconds > options.TimeoutMilliseconds)
                {
                    process.Kill();
                    Finish();
                    context.Fail(new AgentRunError("PI_TIMEOUT", "Pi execution exceeded its timeout."));
                    return;
                }
                if (!process.HasExited) return;
                var output = process.StandardOutput;
                var error = process.StandardError;
                var exitCode = process.ExitCode;
                Finish();
                if (exitCode == 0) context.Complete(AgentRunResult.Succeeded(output));
                else context.Fail(new AgentRunError("PI_EXIT_NONZERO", "Pi exited with code " + exitCode + ": " + error));
            }
            catch (Exception exception) { Finish(); context.Fail(new AgentRunError("PI_PROCESS_FAILED", exception.Message, exception)); }
        }

        public void RequestCancellation(IAgentRunnerContext context)
        {
            if (process == null || finished) return;
            try
            {
                process.Kill();
                Finish();
                context.Cancel(AgentRunResult.Failed(new AgentRunError("PI_CANCELLED", "Pi execution was cancelled.")));
            }
            catch (Exception exception) { Finish(); context.Fail(new AgentRunError("PI_CANCEL_FAILED", exception.Message, exception)); }
        }

        private void Finish()
        {
            finished = true;
            try { (process as IDisposable)?.Dispose(); }
            catch { /* Preserve the original execution result if cleanup races process exit. */ }
        }

        private static string BuildArguments(string task, string model)
        {
            var arguments = "-p --tools read,grep,find,ls --no-extensions --no-skills --no-prompt-templates --no-session --mode text";
            if (!string.IsNullOrWhiteSpace(model)) arguments += " --model " + QuoteArgument(model);
            return arguments + " -- " + QuoteArgument(task);
        }

        // ProcessStartInfo.Arguments quoting: only slashes before quotes/end need doubling.
        private static string QuoteArgument(string value)
        {
            var result = new System.Text.StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
