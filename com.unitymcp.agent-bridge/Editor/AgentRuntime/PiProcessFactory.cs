using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge
{
    public sealed class PiAgentRunnerFactory : IAgentRunnerFactory
    {
        private readonly PiAgentRunnerOptions options;
        public PiAgentRunnerFactory(PiAgentRunnerOptions options = null) { this.options = options ?? new PiAgentRunnerOptions(); }
        public IAgentRunner Create(AgentRunRequest request) => new PiAgentRunner(options, new PiProcessFactory());
    }

    public sealed class PiProcessFactory : IPiProcessFactory
    {
        public IPiProcess Create(string executablePath) => new SystemPiProcess(executablePath);

        private sealed class SystemPiProcess : IPiBudgetProcess, IDisposable
        {
            private readonly Process process;
            private readonly string executablePath;
            private readonly object gate = new object();
            private readonly object inputGate = new object();
            private readonly StringBuilder diagnostics = new StringBuilder();
            private readonly string nonce = Guid.NewGuid().ToString("N");
            private PiRpcSession session;
            private Task stdout;
            private Task stderr;
            private string launcherPrefix = string.Empty;
            private bool started;
            private bool disposed;

            public SystemPiProcess(string executablePath)
            {
                if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Pi executable path is required.", nameof(executablePath));
                this.executablePath = executablePath;
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        CreateNoWindow = true
                    }
                };
            }

            public string WorkingDirectory { get => process.StartInfo.WorkingDirectory; set => process.StartInfo.WorkingDirectory = value ?? string.Empty; }
            public string Arguments { get => process.StartInfo.Arguments; set => process.StartInfo.Arguments = value ?? string.Empty; }
            public bool GuardReady { get { lock (gate) return session?.GuardReady ?? false; } }
            public string FailureCode
            {
                get
                {
                    lock (gate) { if (session?.FailureCode != null) return session.FailureCode; }
                    FinalizeStreams();
                    lock (gate) return session?.FailureCode;
                }
            }
            public string FailureMessage { get { lock (gate) return session?.FailureMessage; } }
            public bool HasExited { get { FinalizeStreams(); return started && !disposed && process.HasExited && stdout.IsCompleted && stderr.IsCompleted; } }
            public int ExitCode => process.ExitCode;
            public string StandardOutput { get { lock (gate) return session.Output; } }
            public string StandardError { get { lock (gate) return diagnostics.ToString(); } }

            public void ConfigureBudget(string task, int maxTurns, int maxOutputBytes)
            {
                if (started) throw new InvalidOperationException("Pi policy cannot change after startup.");
                if (maxTurns < 1 || maxTurns > 64 || maxOutputBytes < 1024 || maxOutputBytes > 1048576)
                    throw new PiProcessException("PI_CONFIGURATION_INVALID", "Pi budget is outside supported bounds.");
                session = new PiRpcSession(nonce, maxTurns, maxOutputBytes, task);
                process.StartInfo.EnvironmentVariables["UNITY_AGENT_BRIDGE_PI_GUARD_NONCE"] = nonce;
                process.StartInfo.EnvironmentVariables["UNITY_AGENT_BRIDGE_PI_MAX_TURNS"] = maxTurns.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            public void Start()
            {
                if (session == null) throw new PiProcessException("PI_GUARD_INVALID", "Pi budget must be configured before startup.");
                var package = PackageInfo.FindForAssembly(typeof(PiProcessFactory).Assembly);
                var guardPath = package == null ? null : Path.Combine(package.resolvedPath, "Tools~", "Pi", "turn-budget-guard.mjs");
                if (string.IsNullOrWhiteSpace(guardPath) || !File.Exists(guardPath))
                    throw new PiProcessException("PI_GUARD_INVALID", "The fixed package turn-budget guard is missing.");
                process.StartInfo.Arguments += " --extension " + QuoteArgument(guardPath);
                ConfigureExecutable();
                VerifyCompatibility();
                process.Start();
                started = true;
                stdout = PumpAsync(process.StandardOutput, AcceptOutput);
                stderr = PumpAsync(process.StandardError, AcceptDiagnostic);
            }

            private void VerifyCompatibility()
            {
                using (var probe = new Process { StartInfo = new ProcessStartInfo
                {
                    FileName = process.StartInfo.FileName,
                    Arguments = launcherPrefix + "--version",
                    WorkingDirectory = process.StartInfo.WorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                } })
                {
                    probe.Start();
                    var output = probe.StandardOutput.ReadToEndAsync();
                    var error = probe.StandardError.ReadToEndAsync();
                    if (!probe.WaitForExit(5000))
                    {
                        try { probe.Kill(); } catch { }
                        throw new PiProcessException("PI_TURN_LIMIT_UNSUPPORTED", "Pi version probe timed out; install the verified Pi 0.85.1 baseline.");
                    }
                    if (!Task.WaitAll(new Task[] { output, error }, 1000) || probe.ExitCode != 0 || output.Result.Trim() != "0.85.1")
                        throw new PiProcessException("PI_TURN_LIMIT_UNSUPPORTED", "Hard turn enforcement requires the verified Pi 0.85.1 baseline.");
                }
            }

            private async Task PumpAsync(StreamReader reader, Action<string> accept)
            {
                try
                {
                    var buffer = new char[4096];
                    var line = new StringBuilder();
                    int count;
                    while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            if (buffer[i] == '\n')
                            {
                                accept(line.ToString().TrimEnd('\r'));
                                line.Clear();
                            }
                            else
                            {
                                if (line.Length >= PiRpcSession.MaxFrameCharacters)
                                    throw new InvalidOperationException("Pi stream frame exceeds the size limit.");
                                line.Append(buffer[i]);
                            }
                        }
                    }
                    if (line.Length > 0) accept(line.ToString().TrimEnd('\r'));
                }
                catch (Exception exception)
                {
                    lock (gate) session.Fail("PI_RPC_PROTOCOL_INVALID", "Pi stream could not be read: " + exception.Message);
                    Kill();
                }
            }

            private void AcceptOutput(string line)
            {
                bool stop;
                bool complete;
                lock (gate)
                {
                    session.AcceptEventLine(line);
                    stop = session.FailureCode != null;
                    complete = session.Completed;
                }
                if (stop) Kill();
                else if (complete) CloseInput();
            }

            private void AcceptDiagnostic(string line)
            {
                string prompt;
                bool stop;
                lock (gate)
                {
                    prompt = session.AcceptGuardLine(line);
                    stop = session.FailureCode != null;
                    if (!line.StartsWith(PiRpcSession.MarkerPrefix, StringComparison.Ordinal) && diagnostics.Length < 32768)
                        diagnostics.AppendLine(line.Substring(0, Math.Min(line.Length, 32768 - diagnostics.Length)));
                }
                if (stop) Kill();
                else if (prompt != null)
                {
                    lock (inputGate)
                    {
                        process.StandardInput.WriteLine(prompt);
                        process.StandardInput.Flush();
                    }
                }
            }

            private void FinalizeStreams()
            {
                if (!started || disposed || stdout == null || stderr == null || !process.HasExited || !stdout.IsCompleted || !stderr.IsCompleted) return;
                lock (gate) session.EndOfStream(process.ExitCode);
            }

            private void CloseInput()
            {
                lock (inputGate) process.StandardInput.Close();
            }

            public void Kill()
            {
                try { if (started && !disposed && !process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            }

            public void Dispose()
            {
                if (disposed) return;
                try
                {
                    Kill();
                    if (started)
                    {
                        process.WaitForExit(2000);
                        Task.WaitAll(new[] { stdout, stderr }, 2000);
                    }
                }
                finally { disposed = true; process.Dispose(); }
            }

            private void ConfigureExecutable()
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
                var resolved = ResolveWindowsExecutable(executablePath);
                var extension = Path.GetExtension(resolved);
                if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    var directory = Path.GetDirectoryName(resolved);
                    string entry = null;
                    foreach (var package in new[] { "@earendil-works/pi-coding-agent", "@mariozechner/pi-coding-agent" })
                    {
                        foreach (var relative in new[] { "dist/bundle/cli.js", "dist/cli.js" })
                        {
                            var candidate = Path.Combine(directory, "node_modules", package, relative);
                            if (File.Exists(candidate)) { entry = candidate; break; }
                        }
                        if (entry != null) break;
                    }
                    if (entry == null) throw new FileNotFoundException("Pi npm entry point was not found beside " + resolved + ". Configure UNITY_AGENT_BRIDGE_PI_PATH with a Pi executable or JavaScript entry point.");
                    resolved = entry;
                    extension = ".js";
                }
                if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase))
                {
                    process.StartInfo.FileName = ResolveWindowsExecutable("node.exe");
                    launcherPrefix = QuoteArgument(resolved) + " ";
                    process.StartInfo.Arguments = launcherPrefix + process.StartInfo.Arguments;
                }
                else process.StartInfo.FileName = resolved;
            }

            private static string ResolveWindowsExecutable(string value)
            {
                var directories = Path.IsPathRooted(value) || value.IndexOfAny(new[] { '/', '\\' }) >= 0
                    ? new[] { string.Empty }
                    : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
                foreach (var directory in directories)
                {
                    if (directories.Length > 1 && string.IsNullOrWhiteSpace(directory)) continue;
                    var candidate = Path.Combine(directory.Trim('"'), value);
                    var suffixes = string.IsNullOrEmpty(Path.GetExtension(value)) ? new[] { ".exe", ".cmd", ".ps1", "" } : new[] { "" };
                    foreach (var suffix in suffixes)
                        if (File.Exists(candidate + suffix)) return Path.GetFullPath(candidate + suffix);
                }
                throw new FileNotFoundException("Executable not found: " + value + ". Check the Unity Editor PATH or UNITY_AGENT_BRIDGE_PI_PATH.");
            }
        }

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
