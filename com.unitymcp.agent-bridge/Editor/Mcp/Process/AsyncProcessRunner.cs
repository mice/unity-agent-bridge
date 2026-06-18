using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class AsyncProcessRunner : IAsyncProcessRunner
    {
        public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.CancellationMode == ProcessCancellationMode.Unspecified)
            {
                throw new ArgumentException(
                    "ProcessExecutionRequest.CancellationMode must be set to a non-default value.",
                    nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                throw new ArgumentException("ProcessExecutionRequest.FilePath is required.", nameof(request));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = request.FilePath,
                Arguments = BuildArguments(request.Arguments),
                WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var pair in request.Environment ?? new Dictionary<string, string>())
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            using (var timeoutCts = request.Timeout > TimeSpan.Zero ? new CancellationTokenSource(request.Timeout) : null)
            using (var linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var stdout = new ProcessOutputBuffer();
                var stderr = new ProcessOutputBuffer();
                var startedAt = DateTime.UtcNow;
                var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stdoutClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stderrClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        stdoutClosedTcs.TrySetResult(true);
                    }
                    else
                    {
                        stdout.AppendStdout(args.Data);
                    }
                };

                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                    {
                        stderrClosedTcs.TrySetResult(true);
                    }
                    else
                    {
                        stderr.AppendStderr(args.Data);
                    }
                };

                process.Exited += (_, __) => exitTcs.TrySetResult(process.ExitCode);

                if (!process.Start())
                {
                    return CreateResult(ProcessOutcome.Failed, null, startedAt, stdout, stderr);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completedTask = await Task.WhenAny(exitTcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);

                if (completedTask == exitTcs.Task)
                {
                    var exitCode = await exitTcs.Task.ConfigureAwait(false);
                    await WaitForOutputDrainAsync(stdoutClosedTcs.Task, stderrClosedTcs.Task, request.TerminateGracePeriod).ConfigureAwait(false);
                    return CreateResult(ProcessOutcome.Completed, exitCode, startedAt, stdout, stderr);
                }

                if (timeoutCts != null && timeoutCts.IsCancellationRequested)
                {
                    TryTerminate(process);
                    return CreateResult(ProcessOutcome.TimedOut, null, startedAt, stdout, stderr);
                }

                if (request.CancellationMode == ProcessCancellationMode.DetachOnCancel)
                {
                    return CreateResult(ProcessOutcome.Detached, null, startedAt, stdout, stderr);
                }

                TryTerminate(process);
                return CreateResult(ProcessOutcome.Terminated, null, startedAt, stdout, stderr);
            }
        }

        private static ProcessExecutionResult CreateResult(
            ProcessOutcome outcome,
            int? exitCode,
            DateTime startedAtUtc,
            ProcessOutputBuffer stdout,
            ProcessOutputBuffer stderr)
        {
            return new ProcessExecutionResult
            {
                Outcome = outcome,
                ExitCode = exitCode,
                Stdout = stdout.GetStdout(),
                Stderr = stderr.GetStderr(),
                Duration = DateTime.UtcNow - startedAtUtc,
            };
        }

        private static async Task WaitForOutputDrainAsync(Task stdoutClosed, Task stderrClosed, TimeSpan gracePeriod)
        {
            if (stdoutClosed == null || stderrClosed == null)
            {
                return;
            }

            var timeout = gracePeriod > TimeSpan.Zero ? gracePeriod : TimeSpan.FromMilliseconds(250);
            var allClosed = Task.WhenAll(stdoutClosed, stderrClosed);
            var completed = await Task.WhenAny(allClosed, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed == allClosed)
            {
                await allClosed.ConfigureAwait(false);
            }
        }

        private static void TryTerminate(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string BuildArguments(IReadOnlyList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[arguments.Count];
            for (var index = 0; index < arguments.Count; index++)
            {
                parts[index] = QuoteArgument(arguments[index] ?? string.Empty);
            }

            return string.Join(" ", parts);
        }

        private static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
