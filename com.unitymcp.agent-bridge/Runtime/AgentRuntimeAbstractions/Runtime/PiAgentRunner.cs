using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnityMcp.AgentRuntime
{
    public sealed class PiAgentRunnerOptions
    {
        public string ExecutablePath { get; set; } = "pi";
        public int TimeoutMilliseconds { get; set; } = 120000;
        public string Model { get; set; } = PiAgentRunnerSettings.DefaultModel;
        public int MaxTurns { get; set; } = 8;
        public int MaxOutputBytes { get; set; } = 65536;
        public IReadOnlyList<string> Tools { get; set; } = PiAgentRunnerSettings.DefaultTools;
    }

    /// <summary>Project-local, read-only Pi policy shared by the Editor settings and runner seam.</summary>
    [Serializable]
    public sealed class PiAgentRunnerSettings
    {
        public const int DefaultTimeoutMilliseconds = 120000;
        public const int DefaultMaxTurns = 8;
        public const int DefaultMaxOutputBytes = 65536;
        public const string DefaultExecutablePath = "pi";
        public const string DefaultModel = "deepseek/deepseek-flash";

        public static readonly IReadOnlyList<string> DefaultTools = new[] { "read", "grep", "find", "ls" };
        public static readonly IReadOnlyList<string> KnownReadOnlyTools = DefaultTools;

        public string ExecutablePath = DefaultExecutablePath;
        public string Model = DefaultModel;
        public int TimeoutMilliseconds = DefaultTimeoutMilliseconds;
        public int MaxTurns = DefaultMaxTurns;
        public int MaxOutputBytes = DefaultMaxOutputBytes;
        public List<string> Tools = new List<string>(DefaultTools);

        public PiAgentRunnerSettings Clone()
        {
            return new PiAgentRunnerSettings
            {
                ExecutablePath = ExecutablePath,
                Model = Model,
                TimeoutMilliseconds = TimeoutMilliseconds,
                MaxTurns = MaxTurns,
                MaxOutputBytes = MaxOutputBytes,
                Tools = new List<string>(Tools ?? new List<string>())
            };
        }

        public PiAgentRunnerOptions ToOptions(int? timeoutOverrideMilliseconds = null)
        {
            if (!TryValidate(this, out var error)) throw new InvalidOperationException(error);
            var normalized = Clone();
            normalized.ExecutablePath = normalized.ExecutablePath.Trim();
            normalized.Model = normalized.Model.Trim();
            if (timeoutOverrideMilliseconds.HasValue && timeoutOverrideMilliseconds.Value <= 0)
                throw new InvalidOperationException("Pi timeout override must be positive.");
            return new PiAgentRunnerOptions
            {
                ExecutablePath = normalized.ExecutablePath,
                Model = normalized.Model,
                TimeoutMilliseconds = timeoutOverrideMilliseconds.HasValue
                    ? Math.Min(normalized.TimeoutMilliseconds, timeoutOverrideMilliseconds.Value)
                    : normalized.TimeoutMilliseconds,
                MaxTurns = normalized.MaxTurns,
                MaxOutputBytes = normalized.MaxOutputBytes,
                Tools = new List<string>(normalized.Tools).AsReadOnly()
            };
        }

        public static PiAgentRunnerSettings CreateDefaults()
        {
            return new PiAgentRunnerSettings();
        }

        public static PiAgentRunnerSettings Normalize(PiAgentRunnerSettings settings)
        {
            var source = settings ?? CreateDefaults();
            var normalized = source.Clone();
            normalized.ExecutablePath = string.IsNullOrWhiteSpace(normalized.ExecutablePath)
                ? DefaultExecutablePath
                : normalized.ExecutablePath.Trim();
            normalized.Model = string.IsNullOrWhiteSpace(normalized.Model)
                ? DefaultModel
                : normalized.Model.Trim();
            normalized.TimeoutMilliseconds = NormalizeRange(normalized.TimeoutMilliseconds, 1000, 900000, DefaultTimeoutMilliseconds);
            normalized.MaxTurns = NormalizeRange(normalized.MaxTurns, 1, 64, DefaultMaxTurns);
            normalized.MaxOutputBytes = NormalizeRange(normalized.MaxOutputBytes, 1024, 1048576, DefaultMaxOutputBytes);
            normalized.Tools = NormalizeTools(normalized.Tools);
            return normalized;
        }

        public static bool TryValidate(PiAgentRunnerSettings settings, out string error)
        {
            if (settings == null)
            {
                error = "Pi runner settings are required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
            {
                error = "Pi executable path is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.Model))
            {
                error = "Pi model is required.";
                return false;
            }

            if (settings.TimeoutMilliseconds < 1000 || settings.TimeoutMilliseconds > 900000)
            {
                error = "Pi timeout must be between 1000 and 900000 milliseconds.";
                return false;
            }

            if (settings.MaxTurns < 1 || settings.MaxTurns > 64)
            {
                error = "Pi max turns must be between 1 and 64.";
                return false;
            }

            if (settings.MaxOutputBytes < 1024 || settings.MaxOutputBytes > 1048576)
            {
                error = "Pi max output must be between 1024 and 1048576 bytes.";
                return false;
            }

            if (settings.Tools == null || settings.Tools.Count == 0)
            {
                error = "At least one read-only Pi tool is required.";
                return false;
            }

            if (settings.Tools.Any(tool => !KnownReadOnlyTools.Contains(tool, StringComparer.Ordinal)))
            {
                error = "Pi tools must be a non-empty subset of read, grep, find, and ls.";
                return false;
            }

            error = null;
            return true;
        }

        private static int NormalizeRange(int value, int min, int max, int fallback)
        {
            return value < min || value > max ? fallback : value;
        }

        private static List<string> NormalizeTools(IEnumerable<string> tools)
        {
            var result = new List<string>();
            foreach (var tool in tools ?? Enumerable.Empty<string>())
            {
                var value = tool == null ? string.Empty : tool.Trim();
                if (!string.IsNullOrWhiteSpace(value) && KnownReadOnlyTools.Contains(value, StringComparer.Ordinal) && !result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result.Count == 0 ? new List<string>(DefaultTools) : result;
        }
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
        }

        public bool CanCancel => true;

        public void Start(AgentRunRequest request, IAgentRunnerContext context)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (context == null) throw new ArgumentNullException(nameof(context));
            finished = false;
            try
            {
                string configurationError = "Pi timeout must be positive.";
                if (options.TimeoutMilliseconds <= 0 || !PiAgentRunnerSettings.TryValidate(new PiAgentRunnerSettings
                {
                    ExecutablePath = options.ExecutablePath,
                    Model = options.Model,
                    TimeoutMilliseconds = Math.Max(1000, options.TimeoutMilliseconds),
                    MaxTurns = options.MaxTurns,
                    MaxOutputBytes = options.MaxOutputBytes,
                    Tools = options.Tools == null ? null : new List<string>(options.Tools)
                }, out configurationError))
                {
                    Finish();
                    context.Fail(new AgentRunError("PI_CONFIGURATION_INVALID", configurationError));
                    return;
                }

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
                if (exitCode == 0) context.Complete(AgentRunResult.Succeeded(BoundOutput(output)));
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

        private string BuildArguments(string task, string model)
        {
            var tools = options.Tools == null || options.Tools.Count == 0
                ? string.Join(",", PiAgentRunnerSettings.DefaultTools)
                : string.Join(",", options.Tools);
            var arguments = "-p --tools " + tools + " --no-extensions --no-skills --no-prompt-templates --no-session --mode text";
            if (!string.IsNullOrWhiteSpace(model)) arguments += " --model " + QuoteArgument(model);
            return arguments + " -- " + QuoteArgument(task);
        }

        private string BoundOutput(string output)
        {
            var text = output ?? string.Empty;
            var max = options.MaxOutputBytes <= 0 ? PiAgentRunnerSettings.DefaultMaxOutputBytes : options.MaxOutputBytes;
            if (Encoding.UTF8.GetByteCount(text) <= max) return text;
            var builder = new StringBuilder();
            var bytes = 0;
            foreach (var character in text)
            {
                var count = Encoding.UTF8.GetByteCount(new[] { character });
                if (bytes + count > max) break;
                builder.Append(character);
                bytes += count;
            }
            return builder.ToString();
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
