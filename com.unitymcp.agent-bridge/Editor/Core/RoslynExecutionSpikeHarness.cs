using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace UnityMcp.AgentBridge
{
    internal static class RoslynExecutionSpikeHarness
    {
        private const string CompilerExecutableName = "unity-roslyn-compiler.exe";
        private const string ContractVersion = "roslyn_spike.v1";
        private const string EntrySourceFileName = "Entry.g.cs";
        private const string InvalidSourceFileName = "Entry.invalid.g.cs";

        public static RoslynSpikeResult Run(RoslynSpikeArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            args.timeoutMs = Math.Max(1000, args.timeoutMs);

            var loadResult = AgentBridgeSettingsLoader.Load();
            var settings = loadResult.Settings ?? AgentBridgeSettingsLoader.CreateDefaultSettings();
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root could not be resolved.");
            var runtimeRoot = Path.Combine(projectRoot, ".unitymcp", "runtime");
            var invocationId = "spike_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var tempRoot = Path.Combine(projectRoot, "Temp", "AgentBridge", "RoslynExecution", invocationId);
            var generatedRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "generated", invocationId);
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(generatedRoot);

            var validBody = "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;";
            var validSource = BuildWrappedSource(validBody);
            var invalidSource = BuildWrappedSource("return ;");
            var validSourcePath = Path.Combine(tempRoot, EntrySourceFileName);
            var invalidSourcePath = Path.Combine(tempRoot, InvalidSourceFileName);
            File.WriteAllText(validSourcePath, validSource, new UTF8Encoding(false));
            File.WriteAllText(invalidSourcePath, invalidSource, new UTF8Encoding(false));

            var outputDllPath = Path.Combine(generatedRoot, "RuntimeSpike.dll");
            var compilerPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", CompilerExecutableName);
            var referenceProfile = BuildReferenceProfile();

            var validCompile = ExecuteCompileRequest(
                tempRoot,
                compilerPath,
                new SpikeCompileRequest
                {
                    requestId = invocationId + "_valid",
                    sourcePath = validSourcePath,
                    sourceText = validSource,
                    outputDllPath = outputDllPath,
                    assemblyName = "MCP_Runtime_Script_" + invocationId,
                    timeoutMs = args.timeoutMs,
                    referenceProfile = referenceProfile
                });

            var result = new RoslynSpikeResult
            {
                contractVersion = ContractVersion,
                invocationId = invocationId,
                unityVersion = Application.unityVersion,
                activeScenePath = SceneManager.GetActiveScene().path,
                generatedSourcePath = MakeProjectRelative(projectRoot, validSourcePath),
                invalidSourcePath = MakeProjectRelative(projectRoot, invalidSourcePath),
                generatedDllPath = MakeProjectRelative(projectRoot, outputDllPath),
                compilerPath = MakeProjectRelative(projectRoot, compilerPath),
                loadApi = "Assembly.Load(byte[])",
                referenceProfile = referenceProfile,
                validCompile = validCompile
            };

            if (validCompile.success && File.Exists(outputDllPath))
            {
                var loadStartedAt = DateTime.UtcNow;
                var assemblyBytes = File.ReadAllBytes(outputDllPath);
                var assembly = Assembly.Load(assemblyBytes);
                var entryType = assembly.GetType("Entry", throwOnError: true);
                var runMethod = entryType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (runMethod == null)
                {
                    throw new MissingMethodException("Entry.Run");
                }

                var rawJson = runMethod.Invoke(null, null) as string;
                result.loadDurationMs = (long)Math.Max(0, (DateTime.UtcNow - loadStartedAt).TotalMilliseconds);
                result.rawExecutionJson = rawJson ?? string.Empty;
                result.executionEnvelope = ParseExecutionEnvelope(rawJson);
            }

            result.invalidCompile = ExecuteCompileRequest(
                tempRoot,
                compilerPath,
                new SpikeCompileRequest
                {
                    requestId = invocationId + "_invalid",
                    sourcePath = invalidSourcePath,
                    sourceText = invalidSource,
                    outputDllPath = Path.Combine(generatedRoot, "RuntimeSpike.invalid.dll"),
                    assemblyName = "MCP_Runtime_Script_Invalid_" + invocationId,
                    timeoutMs = args.timeoutMs,
                    referenceProfile = referenceProfile
                });

            result.sourceHash = ComputeSha256(validSource);
            result.reportPath = AgentBridgeReportWriter.WriteReport(settings, invocationId, "roslyn_spike", JObject.FromObject(result));
            return result;
        }

        private static string BuildWrappedSource(string body)
        {
            return
@"using System;
using Newtonsoft.Json;

public static class Entry
{
    public static string Run()
    {
        try
        {
            var result = __Run();
            return JsonConvert.SerializeObject(new { result, error = """" });
        }
        catch (Exception exception)
        {
            return JsonConvert.SerializeObject(new { result = (object)null, error = exception.ToString() });
        }
    }

    private static object __Run()
    {
        " + body + @"
    }
}";
        }

        private static SpikeReferenceProfile BuildReferenceProfile()
        {
            var assemblyPaths = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly =>
                {
                    try
                    {
                        return assembly.Location;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                })
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SpikeReferenceProfile
            {
                profileId = "unity-editor-loaded-assemblies",
                unityVersion = Application.unityVersion,
                references = assemblyPaths
            };
        }

        private static SpikeCompileResult ExecuteCompileRequest(string tempRoot, string compilerPath, SpikeCompileRequest request)
        {
            if (!File.Exists(compilerPath))
            {
                return new SpikeCompileResult
                {
                    success = false,
                    exitStatus = "proxy_missing",
                    errorSummary = "Compiler proxy executable is missing.",
                    diagnostics = new List<SpikeDiagnostic>()
                };
            }

            var requestPath = Path.Combine(tempRoot, request.requestId + ".request.json");
            File.WriteAllText(requestPath, JsonConvert.SerializeObject(request, Formatting.None), new UTF8Encoding(false));
            var execution = ExecuteCompilerProcess(
                compilerPath,
                requestPath,
                Path.GetDirectoryName(compilerPath) ?? tempRoot,
                request.timeoutMs);

            var result = new SpikeCompileResult
            {
                durationMs = execution.durationMs,
                stdout = execution.Stdout ?? string.Empty,
                stderr = execution.Stderr ?? string.Empty,
                processOutcome = execution.timedOut ? "TimedOut" : "Completed",
                exitCode = execution.exitCode
            };

            if (string.IsNullOrWhiteSpace(execution.Stdout))
            {
                result.success = false;
                result.exitStatus = execution.timedOut ? "timeout" : "proxy_failed";
                result.errorSummary = string.IsNullOrWhiteSpace(execution.Stderr) ? "Compiler proxy returned empty stdout." : execution.Stderr;
                result.diagnostics = new List<SpikeDiagnostic>();
                return result;
            }

            var parsed = JsonConvert.DeserializeObject<SpikeCompileResponse>(execution.Stdout) ?? new SpikeCompileResponse();
            result.success = parsed.success;
            result.exitStatus = parsed.exitStatus ?? string.Empty;
            result.errorSummary = parsed.errorSummary ?? string.Empty;
            result.outputDllPath = parsed.outputDllPath;
            result.assemblyName = parsed.assemblyName;
            result.diagnostics = parsed.diagnostics ?? new List<SpikeDiagnostic>();
            return result;
        }

        private static SpikeExecutionEnvelope ParseExecutionEnvelope(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new SpikeExecutionEnvelope
                {
                    result = null,
                    error = "Entry.Run returned an empty payload."
                };
            }

            var token = JToken.Parse(rawJson);
            if (token is not JObject obj)
            {
                throw new InvalidOperationException("Entry.Run returned a non-object JSON payload.");
            }

            return new SpikeExecutionEnvelope
            {
                result = obj["result"],
                error = obj.Value<string>("error") ?? string.Empty
            };
        }

        private static string MakeProjectRelative(string projectRoot, string absolutePath)
        {
            var relative = Path.GetRelativePath(projectRoot, absolutePath);
            return relative.Replace('\\', '/');
        }

        private static SpikeProcessResult ExecuteCompilerProcess(string filePath, string requestPath, string workingDirectory, int timeoutMs)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = QuoteArgument(requestPath),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var startedAt = DateTime.UtcNow;
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

                return new SpikeProcessResult
                {
                    timedOut = true,
                    durationMs = (long)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds),
                    Stdout = stdoutTask.GetAwaiter().GetResult(),
                    Stderr = stderrTask.GetAwaiter().GetResult()
                };
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return new SpikeProcessResult
            {
                timedOut = false,
                exitCode = process.ExitCode,
                durationMs = (long)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds),
                Stdout = stdoutTask.Result,
                Stderr = stderrTask.Result
            };
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string ComputeSha256(string content)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = sha.ComputeHash(bytes);
            return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    [Serializable]
    public sealed class RoslynSpikeArgs : IStaticMethodArgsValidator
    {
        public int timeoutMs = 10000;

        public bool Validate(out string validationMessage)
        {
            if (timeoutMs < 1000 || timeoutMs > 60000)
            {
                validationMessage = "parameters.timeoutMs must be in the range 1000..60000.";
                return false;
            }

            validationMessage = null;
            return true;
        }
    }

    [Serializable]
    internal sealed class RoslynSpikeResult
    {
        public string contractVersion;
        public string invocationId;
        public string unityVersion;
        public string activeScenePath;
        public string generatedSourcePath;
        public string invalidSourcePath;
        public string generatedDllPath;
        public string compilerPath;
        public string loadApi;
        public string sourceHash;
        public long loadDurationMs;
        public string rawExecutionJson;
        public SpikeExecutionEnvelope executionEnvelope;
        public SpikeReferenceProfile referenceProfile;
        public SpikeCompileResult validCompile;
        public SpikeCompileResult invalidCompile;
        public string reportPath;
    }

    [Serializable]
    internal sealed class SpikeExecutionEnvelope
    {
        public JToken result;
        public string error;
    }

    [Serializable]
    internal sealed class SpikeReferenceProfile
    {
        public string profileId;
        public string unityVersion;
        public List<string> references = new List<string>();
    }

    [Serializable]
    internal sealed class SpikeCompileRequest
    {
        public string requestId;
        public string sourcePath;
        public string sourceText;
        public string outputDllPath;
        public string assemblyName;
        public SpikeReferenceProfile referenceProfile;
        public int timeoutMs;
    }

    [Serializable]
    internal sealed class SpikeCompileResponse
    {
        public bool success;
        public string exitStatus;
        public string errorSummary;
        public string outputDllPath;
        public string assemblyName;
        public List<SpikeDiagnostic> diagnostics = new List<SpikeDiagnostic>();
    }

    [Serializable]
    internal sealed class SpikeCompileResult
    {
        public bool success;
        public string exitStatus;
        public string errorSummary;
        public string outputDllPath;
        public string assemblyName;
        public string processOutcome;
        public int? exitCode;
        public long durationMs;
        public string stdout;
        public string stderr;
        public List<SpikeDiagnostic> diagnostics = new List<SpikeDiagnostic>();
    }

    [Serializable]
    internal sealed class SpikeProcessResult
    {
        public bool timedOut;
        public int? exitCode;
        public long durationMs;
        public string Stdout;
        public string Stderr;
    }

    [Serializable]
    internal sealed class SpikeDiagnostic
    {
        public string severity;
        public string id;
        public string message;
        public int? line;
        public int? column;
    }
}
