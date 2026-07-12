using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.LuaTools
{
    internal static class LuaToolsContracts
    {
        public const string LinterExecutableName = "lua-gc-lint.exe";
        public const string ContractVersion = "lua_tools.v1";
        public const string StatusLintFailed = "lint_failed";
        public const string StatusCompileFailed = "compile_failed";
        public const string StatusToolFailed = "tool_failed";
        public const int DefaultTimeoutMs = 30000;
        public const int MinimumTimeoutMs = 100;
        public const int MaximumTimeoutMs = 300000;
        public const int DefaultLimit = 50;
        public const int MaximumLimit = 500;
        public const string ParserDialect = "lua-gc-lint parser";
    }

    [Serializable]
    internal sealed class LuaLintArgs
    {
        public string path;
        public string[] checks;
        public string failOn = "error";
        public int timeoutMs = LuaToolsContracts.DefaultTimeoutMs;
        public int limit = LuaToolsContracts.DefaultLimit;
        public int offset;
    }

    [Serializable]
    internal sealed class LuaCompileArgs
    {
        public string path;
        public int timeoutMs = LuaToolsContracts.DefaultTimeoutMs;
        public int limit = LuaToolsContracts.DefaultLimit;
        public int offset;
    }

    internal sealed class LuaToolsAvailability
    {
        public string ProjectRoot { get; set; }

        public string LinterPath { get; set; }
    }

    internal static class LuaToolsRuntimeState
    {
        public static bool TryResolveToolAvailability(string projectRoot, out LuaToolsAvailability availability)
        {
            availability = null;
            var resolvedProjectRoot = ResolveProjectRoot(projectRoot);
            if (string.IsNullOrWhiteSpace(resolvedProjectRoot))
            {
                return false;
            }

            var linterPath = GetLinterPath(resolvedProjectRoot);
            if (!File.Exists(linterPath))
            {
                UnityEngine.Debug.LogWarning("LuaTools is enabled but the prepared lua-gc-lint.exe runtime payload is missing.");
                return false;
            }

            availability = new LuaToolsAvailability
            {
                ProjectRoot = Path.GetFullPath(resolvedProjectRoot),
                LinterPath = Path.GetFullPath(linterPath)
            };
            return true;
        }

        public static string GetLinterPath(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                ".unitymcp",
                "runtime",
                "UnityAgentBridge",
                "lua-gc-lint",
                "out",
                "win-x64",
                LuaToolsContracts.LinterExecutableName);
        }

        private static string ResolveProjectRoot(string projectRoot)
        {
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                return projectRoot;
            }

            return Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        }
    }

    internal sealed class LuaToolsService
    {
        private readonly LuaToolsAvailability _availability;

        public LuaToolsService(LuaToolsAvailability availability)
        {
            _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        }

        public UnityMcpToolResult RunLint(UnityMcpToolContext context, LuaLintArgs args, IUnityMcpCancellation cancellation)
        {
            if (!TryNormalizeCommonArgs(args.path, args.failOn, args.timeoutMs, args.limit, args.offset, out var common, out var failure))
            {
                return failure;
            }

            if (!TryNormalizeChecks(args.checks, out var effectiveChecks, out failure))
            {
                return failure;
            }

            cancellation?.ThrowIfCancellationRequested();
            var process = LuaToolsProcess.Run(_availability.LinterPath, common.AbsolutePath, common.FailOn, common.TimeoutMs);
            var diagnostics = LuaToolsDiagnostics.ParseDiagnostics(process.Stdout, out var parseError);
            if (diagnostics == null)
            {
                return CreateProtocolFailure(context, "lint", common, effectiveChecks, process, parseError);
            }

            var metrics = LuaToolsMetrics.Create("lint", true, common, effectiveChecks, process, diagnostics, null);
            var report = LuaToolsReport.Create(context, _availability, "lint", common, effectiveChecks, process, diagnostics, parseError, metrics);

            if (process.TimedOut)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_lint", false, UnityMcpToolStatus.Timeout, "Lua lint timed out.", metrics, report);
            }

            if (process.ExitCode == 2)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_lint", false, LuaToolsContracts.StatusToolFailed, BuildToolFailedSummary(process), metrics, report);
            }

            if (process.ExitCode == 1)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_lint", false, LuaToolsContracts.StatusLintFailed, "Lua lint found diagnostics that meet the fail threshold.", metrics, report);
            }

            if (process.ExitCode != 0)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_lint", false, LuaToolsContracts.StatusToolFailed, BuildToolFailedSummary(process), metrics, report);
            }

            return CreateResult(_availability.ProjectRoot, context, "lua_lint", true, UnityMcpToolStatus.Success, "Lua lint completed.", metrics, report);
        }

        public UnityMcpToolResult RunCompile(UnityMcpToolContext context, LuaCompileArgs args, IUnityMcpCancellation cancellation)
        {
            if (!TryResolveCompileTargets(args.path, out var targets, out var failure))
            {
                return failure;
            }

            if (!TryNormalizeExecutionArgs("error", args.timeoutMs, args.limit, args.offset, out var execution, out failure))
            {
                return failure;
            }

            var allDiagnostics = new List<LuaDiagnostic>();
            var processResults = new List<LuaToolsProcessResult>();
            string protocolFailure = null;
            foreach (var target in targets)
            {
                cancellation?.ThrowIfCancellationRequested();
                var process = LuaToolsProcess.Run(_availability.LinterPath, target.AbsolutePath, "error", execution.TimeoutMs);
                processResults.Add(process);
                var diagnostics = LuaToolsDiagnostics.ParseDiagnostics(process.Stdout, out var parseError);
                if (diagnostics == null)
                {
                    protocolFailure = parseError;
                    break;
                }

                allDiagnostics.AddRange(diagnostics);
                if (process.TimedOut || process.ExitCode == 2)
                {
                    break;
                }
            }

            var aggregate = LuaToolsProcessResult.Aggregate(processResults);
            var common = new LuaToolsExecutionArgs
            {
                ProjectRelativePath = string.Join(";", targets.Select(target => target.ProjectRelativePath)),
                AbsolutePath = string.Join(";", targets.Select(target => target.AbsolutePath)),
                FailOn = "error",
                TimeoutMs = execution.TimeoutMs,
                Limit = execution.Limit,
                Offset = execution.Offset
            };
            var metrics = LuaToolsMetrics.Create("compile", true, common, new[] { "syntax" }, aggregate, allDiagnostics, targets.Select(target => target.ProjectRelativePath).ToArray());
            metrics.parserDialect = LuaToolsContracts.ParserDialect;
            var report = LuaToolsReport.Create(context, _availability, "compile", common, new[] { "syntax" }, aggregate, allDiagnostics, protocolFailure, metrics);

            if (protocolFailure != null)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_compile", false, LuaToolsContracts.StatusToolFailed, protocolFailure, metrics, report);
            }

            if (aggregate.TimedOut)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_compile", false, UnityMcpToolStatus.Timeout, "Lua syntax validation timed out.", metrics, report);
            }

            if (aggregate.ExitCode == 2)
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_compile", false, LuaToolsContracts.StatusToolFailed, BuildToolFailedSummary(aggregate), metrics, report);
            }

            if (allDiagnostics.Any(LuaToolsDiagnostics.IsParseDiagnostic))
            {
                return CreateResult(_availability.ProjectRoot, context, "lua_compile", false, LuaToolsContracts.StatusCompileFailed, "Lua syntax diagnostics were found.", metrics, report);
            }

            return CreateResult(_availability.ProjectRoot, context, "lua_compile", true, UnityMcpToolStatus.Success, "Lua syntax validation completed.", metrics, report);
        }

        private bool TryNormalizeCommonArgs(string rawPath, string failOn, int timeoutMs, int limit, int offset, out LuaToolsExecutionArgs args, out UnityMcpToolResult failure)
        {
            args = null;
            if (!TryNormalizeExecutionArgs(failOn, timeoutMs, limit, offset, out var execution, out failure))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_REQUIRED", "path is required.");
                return false;
            }

            if (!LuaToolsPath.TryResolveProjectPath(_availability.ProjectRoot, rawPath, out var resolved, out failure))
            {
                return false;
            }

            args = execution;
            args.ProjectRelativePath = resolved.ProjectRelativePath;
            args.AbsolutePath = resolved.AbsolutePath;
            return true;
        }

        private bool TryResolveCompileTargets(string rawPath, out List<LuaToolsResolvedPath> targets, out UnityMcpToolResult failure)
        {
            targets = null;
            failure = null;

            if (!string.IsNullOrWhiteSpace(rawPath))
            {
                if (!LuaToolsPath.TryResolveProjectPath(_availability.ProjectRoot, rawPath, out var resolved, out failure))
                {
                    return false;
                }

                targets = new List<LuaToolsResolvedPath> { resolved };
                return true;
            }

            var roots = LuaToolsSettingsReader.ReadLuaSourceRoots(_availability.ProjectRoot);
            if (roots.Count == 0)
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_LUA_ROOTS_REQUIRED", "Lua source roots are not configured and a path argument is required.");
                return false;
            }

            targets = new List<LuaToolsResolvedPath>();
            foreach (var root in roots)
            {
                if (!LuaToolsPath.TryResolveProjectPath(_availability.ProjectRoot, root, out var resolved, out failure))
                {
                    return false;
                }

                targets.Add(resolved);
            }

            return true;
        }

        private static bool TryNormalizeExecutionArgs(string failOn, int timeoutMs, int limit, int offset, out LuaToolsExecutionArgs args, out UnityMcpToolResult failure)
        {
            args = null;
            failure = null;
            var normalizedFailOn = string.IsNullOrWhiteSpace(failOn) ? "error" : failOn.Trim().ToLowerInvariant();
            if (normalizedFailOn != "error" && normalizedFailOn != "warning")
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_FAIL_ON_INVALID", "failOn must be either 'error' or 'warning'.");
                return false;
            }

            if (timeoutMs <= 0)
            {
                timeoutMs = LuaToolsContracts.DefaultTimeoutMs;
            }

            if (timeoutMs < LuaToolsContracts.MinimumTimeoutMs || timeoutMs > LuaToolsContracts.MaximumTimeoutMs)
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_TIMEOUT_INVALID", $"timeoutMs must be in the range {LuaToolsContracts.MinimumTimeoutMs}..{LuaToolsContracts.MaximumTimeoutMs}.");
                return false;
            }

            if (limit <= 0)
            {
                limit = LuaToolsContracts.DefaultLimit;
            }

            if (limit > LuaToolsContracts.MaximumLimit)
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_LIMIT_INVALID", $"limit must be in the range 1..{LuaToolsContracts.MaximumLimit}.");
                return false;
            }

            if (offset < 0)
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_OFFSET_INVALID", "offset must be greater than or equal to 0.");
                return false;
            }

            args = new LuaToolsExecutionArgs
            {
                FailOn = normalizedFailOn,
                TimeoutMs = timeoutMs,
                Limit = limit,
                Offset = offset
            };
            return true;
        }

        private static bool TryNormalizeChecks(string[] rawChecks, out string[] effectiveChecks, out UnityMcpToolResult failure)
        {
            failure = null;
            var checks = new List<string>();
            if (rawChecks != null)
            {
                foreach (var rawCheck in rawChecks)
                {
                    var check = (rawCheck ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(check))
                    {
                        continue;
                    }

                    if (check != "gc")
                    {
                        effectiveChecks = null;
                        failure = LuaToolsResult.InvalidArgs("LUATOOLS_CHECK_UNSUPPORTED", $"Unsupported Lua lint check '{rawCheck}'.");
                        return false;
                    }

                    if (!checks.Contains(check))
                    {
                        checks.Add(check);
                    }
                }
            }

            effectiveChecks = checks.Count == 0 ? new[] { "gc" } : checks.ToArray();
            return true;
        }

        private UnityMcpToolResult CreateProtocolFailure(UnityMcpToolContext context, string operation, LuaToolsExecutionArgs args, string[] effectiveChecks, LuaToolsProcessResult process, string parseError)
        {
            var metrics = LuaToolsMetrics.Create(operation, false, args, effectiveChecks, process, new List<LuaDiagnostic>(), null);
            metrics.status = LuaToolsContracts.StatusToolFailed;
            var report = LuaToolsReport.Create(context, _availability, operation, args, effectiveChecks, process, new List<LuaDiagnostic>(), parseError, metrics);
            return CreateResult(_availability.ProjectRoot, context, "lua_" + operation, false, LuaToolsContracts.StatusToolFailed, parseError, metrics, report);
        }

        private static UnityMcpToolResult CreateResult(string projectRoot, UnityMcpToolContext context, string reportPrefix, bool success, string status, string summary, LuaToolsMetrics metrics, LuaToolsReport report)
        {
            metrics.success = success;
            metrics.status = status;
            metrics.reportPath = LuaToolsReportWriter.Write(projectRoot, context, reportPrefix, metrics.invocationId, report);
            return new UnityMcpToolResult
            {
                Success = success,
                Status = status,
                Summary = summary,
                Errors = success ? new List<UnityMcpToolError>() : LuaToolsDiagnostics.ToErrors(metrics.diagnostics),
                MetricsObjectJson = LuaToolsJson.Serialize(metrics),
                ReportPath = metrics.reportPath
            };
        }

        private static string BuildToolFailedSummary(LuaToolsProcessResult process)
        {
            if (!string.IsNullOrWhiteSpace(process.Stderr))
            {
                return process.Stderr.Trim();
            }

            return "lua-gc-lint failed with exit code " + process.ExitCode + ".";
        }
    }

    internal static class LuaToolsProcess
    {
        internal static Func<string, string, string, int, LuaToolsProcessResult> TestRunnerOverride { get; set; }

        public static LuaToolsProcessResult Run(string executablePath, string scanPath, string failOn, int timeoutMs)
        {
            if (TestRunnerOverride != null)
            {
                return TestRunnerOverride(executablePath, scanPath, failOn, timeoutMs);
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = QuoteArgument(scanPath) + " --format json --fail-on " + failOn,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var startedAt = DateTime.UtcNow;
            try
            {
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    return new LuaToolsProcessResult
                    {
                        TimedOut = true,
                        DurationMs = ElapsedMs(startedAt),
                        Stdout = ReadTask(stdoutTask),
                        Stderr = ReadTask(stderrTask)
                    };
                }

                Task.WaitAll(stdoutTask, stderrTask);
                return new LuaToolsProcessResult
                {
                    ExitCode = process.ExitCode,
                    DurationMs = ElapsedMs(startedAt),
                    Stdout = stdoutTask.Result,
                    Stderr = stderrTask.Result
                };
            }
            catch (Exception exception)
            {
                return new LuaToolsProcessResult
                {
                    ExitCode = 2,
                    DurationMs = ElapsedMs(startedAt),
                    Stdout = string.Empty,
                    Stderr = exception.Message
                };
            }
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static int ElapsedMs(DateTime startedAt)
        {
            return (int)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds);
        }

        private static string ReadTask(Task<string> task)
        {
            try
            {
                return task.GetAwaiter().GetResult();
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal sealed class LuaToolsProcessResult
    {
        public int DurationMs { get; set; }

        public int? ExitCode { get; set; }

        public string Stdout { get; set; }

        public string Stderr { get; set; }

        public bool TimedOut { get; set; }

        public static LuaToolsProcessResult Aggregate(List<LuaToolsProcessResult> results)
        {
            var list = results ?? new List<LuaToolsProcessResult>();
            return new LuaToolsProcessResult
            {
                DurationMs = list.Sum(result => result.DurationMs),
                ExitCode = list.Any(result => result.ExitCode == 2) ? 2 : list.Any(result => result.ExitCode == 1) ? 1 : 0,
                Stdout = string.Join("\n", list.Select(result => result.Stdout).Where(text => !string.IsNullOrWhiteSpace(text))),
                Stderr = string.Join("\n", list.Select(result => result.Stderr).Where(text => !string.IsNullOrWhiteSpace(text))),
                TimedOut = list.Any(result => result.TimedOut)
            };
        }
    }

    internal static class LuaToolsDiagnostics
    {
        public static List<LuaDiagnostic> ParseDiagnostics(string stdout, out string parseError)
        {
            parseError = null;
            try
            {
                if (string.IsNullOrWhiteSpace(stdout))
                {
                    return new List<LuaDiagnostic>();
                }

                var token = JToken.Parse(stdout);
                if (token is JArray array)
                {
                    return array.ToObject<List<LuaDiagnostic>>() ?? new List<LuaDiagnostic>();
                }

                if (token is JObject obj && obj["diagnostics"] is JArray diagnostics)
                {
                    return diagnostics.ToObject<List<LuaDiagnostic>>() ?? new List<LuaDiagnostic>();
                }

                parseError = "lua-gc-lint JSON output must be a diagnostics array or object with diagnostics array.";
                return null;
            }
            catch (Exception exception)
            {
                parseError = exception.Message;
                return null;
            }
        }

        public static bool IsParseDiagnostic(LuaDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return false;
            }

            return string.Equals(diagnostic.rule, "R000", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(diagnostic.rule) && diagnostic.rule.IndexOf("parse", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrWhiteSpace(diagnostic.message) && diagnostic.message.IndexOf("parse", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static List<UnityMcpToolError> ToErrors(List<LuaDiagnosticPageItem> diagnostics)
        {
            return (diagnostics ?? new List<LuaDiagnosticPageItem>())
                .Select(diagnostic => new UnityMcpToolError
                {
                    Code = string.IsNullOrWhiteSpace(diagnostic.rule) ? "LUATOOLS_DIAGNOSTIC" : diagnostic.rule,
                    Message = diagnostic.message,
                    File = diagnostic.file,
                    Line = diagnostic.line ?? 0,
                    Column = diagnostic.column ?? 0
                })
                .ToList();
        }
    }

    internal static class LuaToolsPath
    {
        public static bool TryResolveProjectPath(string projectRoot, string rawPath, out LuaToolsResolvedPath resolved, out UnityMcpToolResult failure)
        {
            resolved = null;
            failure = null;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_REQUIRED", "path is required.");
                return false;
            }

            var normalized = rawPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_ABSOLUTE", "Absolute paths are not allowed.");
                return false;
            }

            if (normalized.Split('/').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_TRAVERSAL", "Path traversal segments are not allowed.");
                return false;
            }

            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) &&
                !string.Equals(normalized, "Assets", StringComparison.Ordinal) &&
                !normalized.StartsWith("Packages/", StringComparison.Ordinal) &&
                !string.Equals(normalized, "Packages", StringComparison.Ordinal))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_ROOT_INVALID", "path must resolve under Assets/ or Packages/.");
                return false;
            }

            var projectFullPath = Path.GetFullPath(projectRoot);
            var absolute = Path.GetFullPath(Path.Combine(projectFullPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!absolute.StartsWith(projectFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_OUTSIDE_PROJECT", "path must resolve inside the Unity project root.");
                return false;
            }

            var assetsRoot = Path.GetFullPath(Path.Combine(projectFullPath, "Assets"));
            var packagesRoot = Path.GetFullPath(Path.Combine(projectFullPath, "Packages"));
            if (!IsAtOrUnder(absolute, assetsRoot) && !IsAtOrUnder(absolute, packagesRoot))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_ROOT_INVALID", "path must resolve under Assets/ or Packages/.");
                return false;
            }

            if (!File.Exists(absolute) && !Directory.Exists(absolute))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_PATH_NOT_FOUND", "path does not exist.");
                return false;
            }

            resolved = new LuaToolsResolvedPath
            {
                ProjectRelativePath = normalized,
                AbsolutePath = absolute
            };
            return true;
        }

        private static bool IsAtOrUnder(string absolutePath, string rootPath)
        {
            var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(absolutePath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   absolutePath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class LuaToolsSettingsReader
    {
        public static List<string> ReadLuaSourceRoots(string projectRoot)
        {
            var settingsPath = Path.Combine(projectRoot, "Assets", "Settings", "AgentBridgeSettings.asset");
            if (!File.Exists(settingsPath))
            {
                return new List<string>();
            }

            var roots = new List<string>();
            var lines = File.ReadAllLines(settingsPath);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!string.Equals(lines[index].Trim(), "luaSourceRoots:", StringComparison.Ordinal))
                {
                    var inlineTrimmed = lines[index].Trim();
                    if (inlineTrimmed.StartsWith("luaSourceRoots:", StringComparison.Ordinal))
                    {
                        var inlineValue = inlineTrimmed.Substring("luaSourceRoots:".Length).Trim();
                        if (string.Equals(inlineValue, "[]", StringComparison.Ordinal))
                        {
                            return roots;
                        }
                    }

                    continue;
                }

                for (var item = index + 1; item < lines.Length; item++)
                {
                    var trimmed = lines[item].Trim();
                    if (trimmed.StartsWith("-", StringComparison.Ordinal))
                    {
                        roots.Add(trimmed.Substring(1).Trim().Trim('"'));
                        continue;
                    }

                    if (trimmed.Length > 0)
                    {
                        break;
                    }
                }

                break;
            }

            return roots.Where(root => !string.IsNullOrWhiteSpace(root)).ToList();
        }
    }

    internal static class LuaToolsJson
    {
        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            args = null;
            failure = null;
            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                if (!(JToken.Parse(rawArgsJson) is JObject))
                {
                    failure = LuaToolsResult.InvalidArgs("LUATOOLS_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                    return false;
                }

                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson) ?? new TArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = LuaToolsResult.InvalidArgs("LUATOOLS_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value ?? new object(), Formatting.None);
        }
    }

    internal static class LuaToolsResult
    {
        public static UnityMcpToolResult InvalidArgs(string code, string message)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = UnityMcpToolStatus.InvalidArgs,
                Summary = message,
                Errors = new List<UnityMcpToolError>
                {
                    new UnityMcpToolError
                    {
                        Code = code,
                        Message = message
                    }
                }
            };
        }
    }

    internal static class LuaToolsReportWriter
    {
        public static string Write(string projectRoot, UnityMcpToolContext context, string prefix, string invocationId, object report)
        {
            var tempRoot = context != null && !string.IsNullOrWhiteSpace(context.TempRoot) ? context.TempRoot : "Temp/AgentBridge";
            var relativePath = Path.Combine(tempRoot.Replace('/', Path.DirectorySeparatorChar), "reports", prefix + "_" + invocationId + ".json");
            var absolutePath = Path.Combine(projectRoot, relativePath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, JsonConvert.SerializeObject(report, Formatting.None), new UTF8Encoding(false));
            return Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }
    }

    internal sealed class LuaToolsResolvedPath
    {
        public string ProjectRelativePath { get; set; }

        public string AbsolutePath { get; set; }
    }

    internal sealed class LuaToolsExecutionArgs
    {
        public string ProjectRelativePath { get; set; }

        public string AbsolutePath { get; set; }

        public string FailOn { get; set; }

        public int TimeoutMs { get; set; }

        public int Limit { get; set; }

        public int Offset { get; set; }
    }

    internal sealed class LuaDiagnostic
    {
        public string file;
        public int? line;
        public int? column;
        public string rule;
        public string ruleId;
        public string legacyRule;
        public string severity;
        public string function;
        public string message;
        public string evidence;
        public string suggestion;
        public string confidence;
    }

    internal sealed class LuaDiagnosticPageItem
    {
        public string file;
        public int? line;
        public int? column;
        public string rule;
        public string ruleId;
        public string legacyRule;
        public string severity;
        public string function;
        public string message;
        public string evidence;
        public string suggestion;
        public string confidence;

        public static LuaDiagnosticPageItem FromDiagnostic(LuaDiagnostic diagnostic)
        {
            return new LuaDiagnosticPageItem
            {
                file = diagnostic.file,
                line = diagnostic.line,
                column = diagnostic.column,
                rule = diagnostic.rule,
                ruleId = diagnostic.ruleId,
                legacyRule = diagnostic.legacyRule,
                severity = diagnostic.severity,
                function = diagnostic.function,
                message = diagnostic.message,
                evidence = diagnostic.evidence,
                suggestion = diagnostic.suggestion,
                confidence = diagnostic.confidence
            };
        }
    }

    internal sealed class LuaToolsMetrics
    {
        public string contractVersion;
        public string operation;
        public string status;
        public bool success;
        public string invocationId;
        public string scanPath;
        public string failOn;
        public string[] effectiveChecks;
        public string[] effectiveRoots;
        public int? exitCode;
        public int durationMs;
        public bool timeout;
        public int diagnosticCount;
        public int warningCount;
        public int errorCount;
        public int offset;
        public int limit;
        public bool truncated;
        public int? nextOffset;
        public string parserDialect;
        public List<LuaDiagnosticPageItem> diagnostics;
        public string reportPath;

        public static LuaToolsMetrics Create(string operation, bool success, LuaToolsExecutionArgs args, string[] effectiveChecks, LuaToolsProcessResult process, List<LuaDiagnostic> diagnostics, string[] effectiveRoots)
        {
            var allDiagnostics = diagnostics ?? new List<LuaDiagnostic>();
            var page = allDiagnostics.Skip(args.Offset).Take(args.Limit).Select(LuaDiagnosticPageItem.FromDiagnostic).ToList();
            var truncated = args.Offset + page.Count < allDiagnostics.Count;
            return new LuaToolsMetrics
            {
                contractVersion = LuaToolsContracts.ContractVersion,
                operation = operation,
                status = success ? UnityMcpToolStatus.Success : UnityMcpToolStatus.Failed,
                success = success,
                invocationId = CreateInvocationId(null),
                scanPath = args.ProjectRelativePath,
                failOn = args.FailOn,
                effectiveChecks = effectiveChecks ?? new string[0],
                effectiveRoots = effectiveRoots,
                exitCode = process != null ? process.ExitCode : null,
                durationMs = process != null ? process.DurationMs : 0,
                timeout = process != null && process.TimedOut,
                diagnosticCount = allDiagnostics.Count,
                warningCount = allDiagnostics.Count(diagnostic => string.Equals(diagnostic.severity, "warning", StringComparison.OrdinalIgnoreCase)),
                errorCount = allDiagnostics.Count(diagnostic => string.Equals(diagnostic.severity, "error", StringComparison.OrdinalIgnoreCase) || LuaToolsDiagnostics.IsParseDiagnostic(diagnostic)),
                offset = args.Offset,
                limit = args.Limit,
                truncated = truncated,
                nextOffset = truncated ? args.Offset + page.Count : (int?)null,
                diagnostics = page
            };
        }

        public static string CreateInvocationId(string commandId)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                return SanitizeFileName(commandId);
            }

            return "lua_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }
    }

    internal sealed class LuaToolsReport
    {
        public LuaToolsMetrics metrics;
        public string operation;
        public string projectRoot;
        public string executablePath;
        public string scanPath;
        public string failOn;
        public string[] effectiveChecks;
        public string stdout;
        public string stderr;
        public int? exitCode;
        public bool timeout;
        public string protocolError;
        public List<LuaDiagnostic> diagnostics;

        public static LuaToolsReport Create(UnityMcpToolContext context, LuaToolsAvailability availability, string operation, LuaToolsExecutionArgs args, string[] effectiveChecks, LuaToolsProcessResult process, List<LuaDiagnostic> diagnostics, string protocolError, LuaToolsMetrics metrics)
        {
            metrics.invocationId = LuaToolsMetrics.CreateInvocationId(context != null ? context.CommandId : null);
            return new LuaToolsReport
            {
                metrics = metrics,
                operation = operation,
                projectRoot = availability.ProjectRoot.Replace('\\', '/'),
                executablePath = MakeProjectRelative(availability.ProjectRoot, availability.LinterPath),
                scanPath = args.ProjectRelativePath,
                failOn = args.FailOn,
                effectiveChecks = effectiveChecks ?? new string[0],
                stdout = process != null ? process.Stdout : string.Empty,
                stderr = process != null ? process.Stderr : string.Empty,
                exitCode = process != null ? process.ExitCode : null,
                timeout = process != null && process.TimedOut,
                protocolError = protocolError,
                diagnostics = diagnostics ?? new List<LuaDiagnostic>()
            };
        }

        private static string MakeProjectRelative(string projectRoot, string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            return Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }
    }
}
