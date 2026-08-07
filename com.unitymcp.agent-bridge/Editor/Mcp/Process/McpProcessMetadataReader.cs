using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace UnityMcp.AgentBridge.Mcp
{
    internal interface IMcpProcessMetadataReader
    {
        McpProcessMetadata Read(int processId);
    }

    internal sealed class McpProcessMetadata
    {
        public string CommandLine { get; set; } = string.Empty;
        public string CommandLineSource { get; set; } = "unavailable";
        public string Error { get; set; } = string.Empty;
    }

    internal interface IMcpProcessMetadataCommandRunner
    {
        McpProcessMetadataCommandResult Run(
            string filePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout);
    }

    internal sealed class McpProcessMetadataCommandResult
    {
        public bool TimedOut { get; set; }
        public int? ExitCode { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    internal sealed class SystemMcpProcessMetadataReader : IMcpProcessMetadataReader
    {
        internal const string UnavailableSource = "unavailable";
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(750);

        private const int MaxCommandLineLength = 4096;
        private const int MaxErrorLength = 240;
        private readonly IMcpProcessMetadataCommandRunner _commandRunner;
        private readonly PlatformID _platform;
        private readonly string _procfsRoot;
        private readonly TimeSpan _timeout;

        public SystemMcpProcessMetadataReader()
            : this(
                new SystemMcpProcessMetadataCommandRunner(),
                Environment.OSVersion.Platform,
                "/proc",
                DefaultTimeout)
        {
        }

        internal SystemMcpProcessMetadataReader(
            IMcpProcessMetadataCommandRunner commandRunner,
            PlatformID platform,
            string procfsRoot,
            TimeSpan timeout)
        {
            _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
            _platform = platform;
            _procfsRoot = string.IsNullOrWhiteSpace(procfsRoot) ? "/proc" : procfsRoot;
            _timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
        }

        public McpProcessMetadata Read(int processId)
        {
            if (processId <= 0)
            {
                return Unavailable("process id is unavailable");
            }

            switch (_platform)
            {
                case PlatformID.Win32NT:
                    return ReadWindowsWmic(processId);
                case PlatformID.MacOSX:
                    return ReadPs(processId);
                case PlatformID.Unix:
                    return Directory.Exists(_procfsRoot)
                        ? ReadProcfs(processId)
                        : ReadPs(processId);
                default:
                    return Unavailable("process metadata is unsupported on this platform");
            }
        }

        internal static string ParseWmicOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                const string prefix = "CommandLine=";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeCommandLine(line.Substring(prefix.Length));
                }
            }

            return string.Empty;
        }

        internal static string ParseProcfsOutput(string output)
        {
            return NormalizeCommandLine(output);
        }

        internal static string ParsePsOutput(string output)
        {
            return NormalizeCommandLine(output);
        }

        internal static string NormalizeCommandLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, MaxCommandLineLength));
            var pendingSpace = false;
            for (var index = 0; index < value.Length && builder.Length < MaxCommandLineLength; index++)
            {
                var character = value[index];
                if (character == '\0' || char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace && builder.Length < MaxCommandLineLength)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                if (builder.Length < MaxCommandLineLength)
                {
                    builder.Append(character);
                }
            }

            if (value.Length > MaxCommandLineLength && builder.Length >= 3)
            {
                builder.Length = Math.Min(builder.Length, MaxCommandLineLength - 3);
                builder.Append("...");
            }

            return builder.ToString();
        }

        private McpProcessMetadata ReadWindowsWmic(int processId)
        {
            var result = _commandRunner.Run(
                "wmic.exe",
                new[]
                {
                    "process",
                    "where",
                    "(ProcessId=" + processId.ToString(CultureInfo.InvariantCulture) + ")",
                    "get",
                    "CommandLine",
                    "/value",
                },
                _timeout);

            return FromCommandResult("wmic", result, ParseWmicOutput);
        }

        private McpProcessMetadata ReadProcfs(int processId)
        {
            var commandLinePath = Path.Combine(
                _procfsRoot,
                processId.ToString(CultureInfo.InvariantCulture),
                "cmdline");
            try
            {
                var commandLine = ParseProcfsOutput(File.ReadAllText(commandLinePath, Encoding.UTF8));
                return string.IsNullOrWhiteSpace(commandLine)
                    ? Unavailable("procfs returned no command line")
                    : Available(commandLine, "procfs");
            }
            catch (Exception exception)
            {
                return Unavailable("procfs: " + exception.Message);
            }
        }

        private McpProcessMetadata ReadPs(int processId)
        {
            var result = _commandRunner.Run(
                "ps",
                new[]
                {
                    "-p",
                    processId.ToString(CultureInfo.InvariantCulture),
                    "-o",
                    "command=",
                },
                _timeout);

            return FromCommandResult("ps", result, ParsePsOutput);
        }

        private static McpProcessMetadata FromCommandResult(
            string source,
            McpProcessMetadataCommandResult result,
            Func<string, string> parser)
        {
            if (result == null)
            {
                return Unavailable(source + ": metadata reader returned no result");
            }

            if (result.TimedOut)
            {
                return Unavailable(source + ": metadata inspection timed out");
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return Unavailable(source + ": " + result.Error);
            }

            if (result.ExitCode.HasValue && result.ExitCode.Value != 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.Stderr)
                    ? "metadata command exited with code " + result.ExitCode.Value.ToString(CultureInfo.InvariantCulture)
                    : result.Stderr;
                return Unavailable(source + ": " + detail);
            }

            var commandLine = parser(result.Stdout ?? string.Empty);
            return string.IsNullOrWhiteSpace(commandLine)
                ? Unavailable(source + ": metadata source returned no command line")
                : Available(commandLine, source);
        }

        private static McpProcessMetadata Available(string commandLine, string source)
        {
            return new McpProcessMetadata
            {
                CommandLine = NormalizeCommandLine(commandLine),
                CommandLineSource = source,
            };
        }

        private static McpProcessMetadata Unavailable(string error)
        {
            return new McpProcessMetadata
            {
                CommandLineSource = UnavailableSource,
                Error = SummarizeError(error),
            };
        }

        internal static string SummarizeError(string error)
        {
            var normalized = NormalizeCommandLine(error);
            if (normalized.Length <= MaxErrorLength)
            {
                return normalized;
            }

            return normalized.Substring(0, MaxErrorLength - 3) + "...";
        }
    }

    internal sealed class SystemMcpProcessMetadataCommandRunner : IMcpProcessMetadataCommandRunner
    {
        public McpProcessMetadataCommandResult Run(
            string filePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = BuildArguments(arguments),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        return Failed("metadata command did not start");
                    }

                    var timeoutMilliseconds = (int)Math.Min(
                        int.MaxValue,
                        Math.Max(1, timeout.TotalMilliseconds));
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        TryTerminate(process);
                        return new McpProcessMetadataCommandResult { TimedOut = true };
                    }

                    return new McpProcessMetadataCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Stdout = process.StandardOutput.ReadToEnd(),
                        Stderr = process.StandardError.ReadToEnd(),
                    };
                }
            }
            catch (Exception exception)
            {
                return Failed(exception.Message);
            }
        }

        private static McpProcessMetadataCommandResult Failed(string error)
        {
            return new McpProcessMetadataCommandResult
            {
                Error = error ?? string.Empty,
            };
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
            catch
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
