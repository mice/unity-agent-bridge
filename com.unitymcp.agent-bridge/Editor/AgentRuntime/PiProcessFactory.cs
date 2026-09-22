using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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

        private sealed class SystemPiProcess : IPiProcess, IDisposable
        {
            private readonly Process process;
            private readonly string executablePath;
            private Task<string> stdout;
            private Task<string> stderr;
            private bool started;

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
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8,
                        CreateNoWindow = true
                    }
                };
            }

            public string WorkingDirectory { get => process.StartInfo.WorkingDirectory; set => process.StartInfo.WorkingDirectory = value ?? string.Empty; }
            public string Arguments { get => process.StartInfo.Arguments; set => process.StartInfo.Arguments = value ?? string.Empty; }
            // Process exit alone does not guarantee that the final pipe bytes have arrived.
            public bool HasExited => started && process.HasExited && stdout.IsCompleted && stderr.IsCompleted;
            public int ExitCode => process.ExitCode;
            public string StandardOutput => stdout.GetAwaiter().GetResult();
            public string StandardError => stderr.GetAwaiter().GetResult();
            public void Start()
            {
                ConfigureExecutable();
                process.Start();
                started = true;
                stdout = process.StandardOutput.ReadToEndAsync();
                stderr = process.StandardError.ReadToEndAsync();
            }
            public void Kill() { if (started && !process.HasExited) process.Kill(); }
            public void Dispose() { try { Kill(); } finally { process.Dispose(); } }

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
                    process.StartInfo.Arguments = "\"" + resolved + "\" " + process.StartInfo.Arguments;
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
    }
}
