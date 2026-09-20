using System;
using System.Diagnostics;
using System.Text;
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

        private sealed class SystemPiProcess : IPiProcess
        {
            private readonly Process process;
            private readonly StringBuilder stdout = new StringBuilder();
            private readonly StringBuilder stderr = new StringBuilder();

            public SystemPiProcess(string executablePath)
            {
                if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Pi executable path is required.", nameof(executablePath));
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
            }

            public string WorkingDirectory { get => process.StartInfo.WorkingDirectory; set => process.StartInfo.WorkingDirectory = value ?? string.Empty; }
            public string Arguments { get => process.StartInfo.Arguments; set => process.StartInfo.Arguments = value ?? string.Empty; }
            public bool HasExited => process.HasExited;
            public int ExitCode => process.ExitCode;
            public string StandardOutput => stdout.ToString() + (process.HasExited ? process.StandardOutput.ReadToEnd() : string.Empty);
            public string StandardError => stderr.ToString() + (process.HasExited ? process.StandardError.ReadToEnd() : string.Empty);
            public void Start() { process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); }; process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); }; }
            public void Kill() { if (!process.HasExited) process.Kill(); }
        }
    }
}
