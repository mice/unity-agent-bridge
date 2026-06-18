using System;
using System.Collections;
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
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.RoslynExecution
{
    internal static class RoslynExecutionContracts
    {
        public const string MetricsContractVersion = "execute_csharp.v1";
        public const string CompilerExecutableName = "unity-roslyn-compiler.exe";
        public const string PhaseInvalidArgs = "invalid_args";
        public const string PhaseValidationFailed = "validation_failed";
        public const string PhaseProxyFailed = "proxy_failed";
        public const string PhaseCompileFailed = "compile_failed";
        public const string PhaseLoadFailed = "load_failed";
        public const string PhaseExecutionFailed = "execution_failed";
        public const string PhaseSerializationFailed = "serialization_failed";
        public const string PhaseExecuted = "executed";
        public const string PhaseTimeout = "timeout";
        public const string SerializationFailurePrefix = "serialization_failed:";
        public const int DefaultTimeoutMs = 2000;
        public const int MinimumTimeoutMs = 100;
        public const int MaximumTimeoutMs = 10000;
        public const int MaxBytes = 65536;
        public const int MaxCollectionLength = 200;
        public const int MaxDepth = 6;
        public const int MaxStringLength = 4096;
        public const string EntrySourceFileName = "Entry.g.cs";
        public const string ReportPrefix = "execute_csharp";
        public const string ArgsSchemaJson =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.execute_csharp args\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"code\"],\"properties\":{\"code\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":65536,\"description\":\"AI-generated C# body for private static object __Run(). The Unity tool wraps this body in the fixed Entry.Run JSON wrapper.\"},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":10000,\"default\":2000}}}";

        public static RoslynExecutionLimit CreateLimit()
        {
            return new RoslynExecutionLimit
            {
                maxBytes = MaxBytes,
                maxCollectionLength = MaxCollectionLength,
                maxDepth = MaxDepth,
                maxStringLength = MaxStringLength
            };
        }
    }

    [Serializable]
    internal sealed class ExecuteCSharpArgs
    {
        public string code;
        public int timeoutMs = RoslynExecutionContracts.DefaultTimeoutMs;
    }

    internal sealed class RoslynExecutionAvailability
    {
        public string ProjectRoot { get; set; }

        public string CompilerPath { get; set; }
    }

    internal static class RoslynExecutionRuntimeState
    {
        public static bool TryResolveToolAvailability(string projectRoot, out RoslynExecutionAvailability availability)
        {
            availability = null;

            var resolvedProjectRoot = projectRoot;
            if (string.IsNullOrWhiteSpace(resolvedProjectRoot))
            {
                resolvedProjectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(resolvedProjectRoot) || !IsEnabled(resolvedProjectRoot))
            {
                return false;
            }

            var compilerPath = GetCompilerPath(resolvedProjectRoot);
            if (!File.Exists(compilerPath))
            {
                UnityEngine.Debug.LogWarning("Roslyn execution is enabled but the prepared runtime compiler proxy is missing.");
                return false;
            }

            availability = new RoslynExecutionAvailability
            {
                ProjectRoot = Path.GetFullPath(resolvedProjectRoot),
                CompilerPath = Path.GetFullPath(compilerPath)
            };
            return true;
        }

        public static string GetCompilerPath(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                ".unitymcp",
                "runtime",
                "UnityAgentBridge",
                "roslyn-execution",
                "out",
                "win-x64",
                RoslynExecutionContracts.CompilerExecutableName);
        }

        private static bool IsEnabled(string projectRoot)
        {
            foreach (var candidatePath in EnumerateSettingsPaths(projectRoot))
            {
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                var content = File.ReadAllText(candidatePath);
                using var reader = new StringReader(content);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("roslynExecutionEnabled:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var value = trimmed.Substring("roslynExecutionEnabled:".Length).Trim();
                    return string.Equals(value, "1", StringComparison.Ordinal) ||
                           string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateSettingsPaths(string projectRoot)
        {
            yield return Path.Combine(projectRoot, "Assets", "Settings", "AgentBridgeSettings.asset");

            var assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
            {
                yield break;
            }

            foreach (var path in Directory.EnumerateFiles(assetsRoot, "AgentBridgeSettings.asset", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    internal static class RoslynExecutionJson
    {
        public static bool TryDeserializeArgs(string rawArgsJson, out ExecuteCSharpArgs args, out UnityMcpToolResult failure)
        {
            args = null;
            failure = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = RoslynExecutionResultFactory.InvalidArgs("ROSLYN_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                if (!(JToken.Parse(rawArgsJson) is JObject))
                {
                    failure = RoslynExecutionResultFactory.InvalidArgs("ROSLYN_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                    return false;
                }

                args = JsonConvert.DeserializeObject<ExecuteCSharpArgs>(rawArgsJson) ?? new ExecuteCSharpArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = RoslynExecutionResultFactory.InvalidArgs("ROSLYN_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            return value == null ? "{}" : JsonConvert.SerializeObject(value, Formatting.None);
        }
    }

    internal static class RoslynExecutionValidation
    {
        private static readonly string[] BlockedTokens =
        {
            "System.IO",
            "System.Net",
            "System.Diagnostics.Process",
            "System.Reflection",
            "unsafe",
            "DllImport",
            "Environment.Exit",
            "Application.Quit",
            "EditorApplication.Exit",
            "while(true)",
            "for(;;)",
            "Thread.Sleep",
            "WaitOne(",
            "WaitAny(",
            "WaitAll("
        };

        public static bool TryValidate(ExecuteCSharpArgs args, out string validationMessage)
        {
            if (args == null)
            {
                validationMessage = "args are required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(args.code))
            {
                validationMessage = "code must not be empty.";
                return false;
            }

            if (args.code.Length > 65536)
            {
                validationMessage = "code exceeds the maximum source length.";
                return false;
            }

            if (args.timeoutMs < RoslynExecutionContracts.MinimumTimeoutMs || args.timeoutMs > RoslynExecutionContracts.MaximumTimeoutMs)
            {
                validationMessage = "timeoutMs must be in the range 100..10000.";
                return false;
            }

            if (args.code.IndexOf("class Entry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                args.code.IndexOf("static string Run(", StringComparison.OrdinalIgnoreCase) >= 0 ||
                args.code.IndexOf("static object __Run(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                validationMessage = "Submit only the __Run() method body, not a full C# file.";
                return false;
            }

            for (var index = 0; index < BlockedTokens.Length; index++)
            {
                if (args.code.IndexOf(BlockedTokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    validationMessage = "Blocked API or pattern detected: " + BlockedTokens[index];
                    return false;
                }
            }

            validationMessage = null;
            return true;
        }
    }

    internal static class RoslynExecutionResultFactory
    {
        public static UnityMcpToolResult InvalidArgs(string code, string message)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = RoslynExecutionContracts.PhaseInvalidArgs,
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

        public static UnityMcpToolResult ValidationFailed(UnityMcpToolContext context, string phase, string message, string projectRoot)
        {
            var invocationId = RoslynExecutionUtility.CreateInvocationId(context != null ? context.CommandId : null);
            var metrics = RoslynExecutionMetrics.CreateFailure(invocationId, string.Empty, phase, message, null);
            var report = new RoslynExecutionReport
            {
                metrics = metrics,
                compilerPath = null,
                generatedSourcePath = null,
                generatedDllPath = null,
                rawExecutionJson = null,
                diagnostics = new List<RoslynCompileDiagnostic>(),
                exception = null
            };
            metrics.reportPath = RoslynExecutionReportWriter.Write(projectRoot, invocationId, report);

            return new UnityMcpToolResult
            {
                Success = false,
                Status = phase,
                Summary = message,
                Errors = new List<UnityMcpToolError>
                {
                    new UnityMcpToolError
                    {
                        Code = "ROSLYN_EXECUTION_VALIDATION_FAILED",
                        Message = message
                    }
                },
                MetricsObjectJson = RoslynExecutionJson.Serialize(metrics),
                ReportPath = metrics.reportPath
            };
        }
    }

    internal sealed class RoslynExecutionService
    {
        private readonly RoslynExecutionAvailability _availability;

        public RoslynExecutionService(RoslynExecutionAvailability availability)
        {
            _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        }

        public UnityMcpToolResult Execute(UnityMcpToolContext context, ExecuteCSharpArgs args, IUnityMcpCancellation cancellation)
        {
            var invocationId = RoslynExecutionUtility.CreateInvocationId(context != null ? context.CommandId : null);
            var projectRoot = _availability.ProjectRoot;
            var runtimeRoot = Path.Combine(projectRoot, ".unitymcp", "runtime", "UnityAgentBridge", "roslyn-execution");
            var tempRoot = Path.Combine(projectRoot, "Temp", "AgentBridge", "RoslynExecution", invocationId);
            var generatedRoot = Path.Combine(runtimeRoot, "generated", invocationId);
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(generatedRoot);

            var wrappedSource = RoslynExecutionUtility.BuildWrappedSource(args.code);
            var sourceHash = RoslynExecutionUtility.ComputeSha256(wrappedSource);
            var sourcePath = Path.Combine(tempRoot, RoslynExecutionContracts.EntrySourceFileName);
            var outputDllPath = Path.Combine(generatedRoot, "RuntimeScript.dll");
            File.WriteAllText(sourcePath, wrappedSource, new UTF8Encoding(false));

            var effectiveTimeoutMs = args.timeoutMs;
            if (context != null && context.TimeoutMs > 0)
            {
                effectiveTimeoutMs = Math.Min(effectiveTimeoutMs, context.TimeoutMs);
            }

            var compileResponse = ExecuteCompile(sourcePath, wrappedSource, outputDllPath, invocationId, effectiveTimeoutMs);
            if (compileResponse.ProcessTimedOut)
            {
                return CreateFailureResult(invocationId, sourceHash, RoslynExecutionContracts.PhaseTimeout, "Compilation timed out.", compileResponse, null, null, projectRoot, sourcePath, outputDllPath);
            }

            if (compileResponse.ProtocolResponse == null)
            {
                return CreateFailureResult(invocationId, sourceHash, RoslynExecutionContracts.PhaseProxyFailed, compileResponse.ErrorSummary, compileResponse, null, null, projectRoot, sourcePath, outputDllPath);
            }

            if (!compileResponse.ProtocolResponse.success)
            {
                return CreateCompileFailure(invocationId, sourceHash, compileResponse, projectRoot, sourcePath, outputDllPath);
            }

            cancellation?.ThrowIfCancellationRequested();

            var loadStartedAt = DateTime.UtcNow;
            try
            {
                var assemblyBytes = File.ReadAllBytes(outputDllPath);
                var assembly = Assembly.Load(assemblyBytes);
                var entryType = assembly.GetType("Entry", throwOnError: true);
                var runMethod = entryType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                if (runMethod == null)
                {
                    return CreateFailureResult(invocationId, sourceHash, RoslynExecutionContracts.PhaseLoadFailed, "Entry.Run could not be found.", compileResponse, null, null, projectRoot, sourcePath, outputDllPath);
                }

                var rawExecutionJson = runMethod.Invoke(null, null) as string;
                var loadDurationMs = (int)Math.Max(0, (DateTime.UtcNow - loadStartedAt).TotalMilliseconds);
                var executionEnvelope = RoslynExecutionUtility.ParseExecutionEnvelope(rawExecutionJson);
                if (executionEnvelope.error.StartsWith(RoslynExecutionContracts.SerializationFailurePrefix, StringComparison.Ordinal))
                {
                    var serializationMessage = executionEnvelope.error.Substring(RoslynExecutionContracts.SerializationFailurePrefix.Length).Trim();
                    if (string.IsNullOrWhiteSpace(serializationMessage))
                    {
                        serializationMessage = "Result serialization failed.";
                    }

                    return CreateFailureResult(
                        invocationId,
                        sourceHash,
                        RoslynExecutionContracts.PhaseSerializationFailed,
                        serializationMessage,
                        compileResponse,
                        rawExecutionJson,
                        null,
                        projectRoot,
                        sourcePath,
                        outputDllPath);
                }

                var metrics = RoslynExecutionMetrics.CreateSuccess(invocationId, sourceHash, compileResponse, loadDurationMs, compileResponse.ProtocolResponse.assemblyName, executionEnvelope);
                var report = new RoslynExecutionReport
                {
                    metrics = metrics,
                    compilerPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, _availability.CompilerPath),
                    generatedSourcePath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, sourcePath),
                    generatedDllPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, outputDllPath),
                    rawExecutionJson = rawExecutionJson,
                    diagnostics = compileResponse.ProtocolResponse.diagnostics ?? new List<RoslynCompileDiagnostic>()
                };
                metrics.reportPath = RoslynExecutionReportWriter.Write(projectRoot, invocationId, report);

                return new UnityMcpToolResult
                {
                    Success = true,
                    Status = UnityMcpToolStatus.Success,
                    Summary = "Roslyn execution completed.",
                    MetricsObjectJson = RoslynExecutionJson.Serialize(metrics),
                    ChangedFiles = new List<string>
                    {
                        RoslynExecutionUtility.MakeProjectRelative(projectRoot, sourcePath),
                        RoslynExecutionUtility.MakeProjectRelative(projectRoot, outputDllPath)
                    },
                    ReportPath = metrics.reportPath
                };
            }
            catch (TargetInvocationException exception)
            {
                var inner = exception.InnerException ?? exception;
                return CreateFailureResult(invocationId, sourceHash, RoslynExecutionContracts.PhaseExecutionFailed, inner.Message, compileResponse, null, inner, projectRoot, sourcePath, outputDllPath);
            }
            catch (Exception exception)
            {
                return CreateFailureResult(invocationId, sourceHash, RoslynExecutionContracts.PhaseLoadFailed, exception.Message, compileResponse, null, exception, projectRoot, sourcePath, outputDllPath);
            }
        }

        private CompileExecutionResult ExecuteCompile(string sourcePath, string sourceText, string outputDllPath, string invocationId, int timeoutMs)
        {
            var request = new RoslynCompileRequest
            {
                requestId = invocationId,
                sourcePath = sourcePath,
                sourceText = sourceText,
                outputDllPath = outputDllPath,
                assemblyName = "MCP_Runtime_Script_" + invocationId,
                timeoutMs = timeoutMs,
                referenceProfile = RoslynExecutionUtility.BuildReferenceProfile()
            };

            var requestPath = Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".", invocationId + ".request.json");
            File.WriteAllText(requestPath, JsonConvert.SerializeObject(request, Formatting.None), new UTF8Encoding(false));

            var processResult = RoslynExecutionUtility.ExecuteCompilerProcess(
                _availability.CompilerPath,
                requestPath,
                Path.GetDirectoryName(_availability.CompilerPath) ?? Path.GetDirectoryName(sourcePath) ?? ".",
                timeoutMs);

            var result = new CompileExecutionResult
            {
                DurationMs = processResult.DurationMs,
                ExitCode = processResult.ExitCode,
                Stdout = processResult.Stdout ?? string.Empty,
                Stderr = processResult.Stderr ?? string.Empty,
                ProcessTimedOut = processResult.TimedOut
            };

            if (processResult.TimedOut)
            {
                result.ErrorSummary = "Compilation timed out.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(processResult.Stdout))
            {
                result.ErrorSummary = string.IsNullOrWhiteSpace(processResult.Stderr)
                    ? "Compiler proxy returned empty stdout."
                    : processResult.Stderr;
                return result;
            }

            try
            {
                result.ProtocolResponse = JsonConvert.DeserializeObject<RoslynCompileResponse>(processResult.Stdout);
                if (result.ProtocolResponse == null)
                {
                    result.ErrorSummary = "Compiler proxy returned an empty protocol payload.";
                }
            }
            catch (Exception exception)
            {
                result.ErrorSummary = exception.Message;
            }

            return result;
        }

        private UnityMcpToolResult CreateCompileFailure(string invocationId, string sourceHash, CompileExecutionResult compileResponse, string projectRoot, string sourcePath, string outputDllPath)
        {
            var summary = compileResponse.ProtocolResponse != null && !string.IsNullOrWhiteSpace(compileResponse.ProtocolResponse.errorSummary)
                ? compileResponse.ProtocolResponse.errorSummary
                : "Compilation failed.";
            var metrics = RoslynExecutionMetrics.CreateFailure(invocationId, sourceHash, RoslynExecutionContracts.PhaseCompileFailed, summary, compileResponse);
            var report = new RoslynExecutionReport
            {
                metrics = metrics,
                compilerPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, _availability.CompilerPath),
                generatedSourcePath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, sourcePath),
                generatedDllPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, outputDllPath),
                rawExecutionJson = null,
                diagnostics = compileResponse.ProtocolResponse != null
                    ? compileResponse.ProtocolResponse.diagnostics ?? new List<RoslynCompileDiagnostic>()
                    : new List<RoslynCompileDiagnostic>()
            };
            metrics.reportPath = RoslynExecutionReportWriter.Write(projectRoot, invocationId, report);

            var errors = new List<UnityMcpToolError>();
            if (compileResponse.ProtocolResponse != null && compileResponse.ProtocolResponse.diagnostics != null)
            {
                for (var index = 0; index < compileResponse.ProtocolResponse.diagnostics.Count; index++)
                {
                    var diagnostic = compileResponse.ProtocolResponse.diagnostics[index];
                    errors.Add(new UnityMcpToolError
                    {
                        Code = string.IsNullOrWhiteSpace(diagnostic.id) ? "ROSLYN_COMPILE_ERROR" : diagnostic.id,
                        Message = diagnostic.message,
                        Line = diagnostic.line ?? 0,
                        Column = diagnostic.column ?? 0
                    });
                }
            }

            if (errors.Count == 0)
            {
                errors.Add(new UnityMcpToolError
                {
                    Code = "ROSLYN_COMPILE_FAILED",
                    Message = summary
                });
            }

            return new UnityMcpToolResult
            {
                Success = false,
                Status = RoslynExecutionContracts.PhaseCompileFailed,
                Summary = summary,
                Errors = errors,
                MetricsObjectJson = RoslynExecutionJson.Serialize(metrics),
                ReportPath = metrics.reportPath
            };
        }

        private UnityMcpToolResult CreateFailureResult(
            string invocationId,
            string sourceHash,
            string phase,
            string summary,
            CompileExecutionResult compileResponse,
            string rawExecutionJson,
            Exception exception,
            string projectRoot,
            string sourcePath,
            string outputDllPath)
        {
            var metrics = RoslynExecutionMetrics.CreateFailure(invocationId, sourceHash, phase, summary, compileResponse);
            var report = new RoslynExecutionReport
            {
                metrics = metrics,
                compilerPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, _availability.CompilerPath),
                generatedSourcePath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, sourcePath),
                generatedDllPath = RoslynExecutionUtility.MakeProjectRelative(projectRoot, outputDllPath),
                rawExecutionJson = rawExecutionJson,
                diagnostics = compileResponse != null && compileResponse.ProtocolResponse != null
                    ? compileResponse.ProtocolResponse.diagnostics ?? new List<RoslynCompileDiagnostic>()
                    : new List<RoslynCompileDiagnostic>(),
                exception = exception != null ? exception.ToString() : null
            };
            metrics.reportPath = RoslynExecutionReportWriter.Write(projectRoot, invocationId, report);

            return new UnityMcpToolResult
            {
                Success = false,
                Status = phase,
                Summary = summary,
                Errors = new List<UnityMcpToolError>
                {
                    new UnityMcpToolError
                    {
                        Code = "ROSLYN_EXECUTION_FAILED",
                        Message = summary
                    }
                },
                MetricsObjectJson = RoslynExecutionJson.Serialize(metrics),
                ReportPath = metrics.reportPath
            };
        }
    }

    public static class RoslynExecutionRuntimeSerializer
    {
        public static string SerializeSuccess(object result)
        {
            try
            {
                var state = new RoslynExecutionSerializationState(RoslynExecutionContracts.CreateLimit());
                var boundedResult = SerializeValue(result, state, 0);
                var envelope = new JObject
                {
                    ["result"] = boundedResult,
                    ["error"] = string.Empty,
                    ["resultKind"] = DetermineResultKind(boundedResult),
                    ["truncated"] = state.Truncated,
                    ["truncationReason"] = state.TruncationReason != null ? JToken.FromObject(state.TruncationReason) : JValue.CreateNull()
                };

                EnforceByteLimit(envelope, state);
                return envelope.ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                return SerializeSerializationFailure(exception);
            }
        }

        public static string SerializeExecutionException(Exception exception)
        {
            var envelope = new JObject
            {
                ["result"] = JValue.CreateNull(),
                ["error"] = TrimError(exception),
                ["resultKind"] = "null",
                ["truncated"] = false,
                ["truncationReason"] = JValue.CreateNull()
            };
            return envelope.ToString(Formatting.None);
        }

        public static string SerializeSerializationFailure(Exception exception)
        {
            var envelope = new JObject
            {
                ["result"] = JValue.CreateNull(),
                ["error"] = RoslynExecutionContracts.SerializationFailurePrefix + " " + TrimError(exception),
                ["resultKind"] = "null",
                ["truncated"] = false,
                ["truncationReason"] = JValue.CreateNull()
            };
            EnforceByteLimit(envelope, new RoslynExecutionSerializationState(RoslynExecutionContracts.CreateLimit()));
            return envelope.ToString(Formatting.None);
        }

        private static JToken SerializeValue(object value, RoslynExecutionSerializationState state, int depth)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            if (depth >= RoslynExecutionContracts.MaxDepth)
            {
                state.MarkTruncated("maxDepth");
                return new JValue("[truncated:maxDepth]");
            }

            if (value is JToken token)
            {
                return BoundToken(token, state, depth);
            }

            if (value is UnityEngine.Object unityObject)
            {
                return CreateUnityObjectSummary(unityObject, state);
            }

            if (value is string text)
            {
                return new JValue(state.TruncateString(text));
            }

            if (value is char character)
            {
                return new JValue(state.TruncateString(character.ToString()));
            }

            if (value is bool || IsNumeric(value))
            {
                return JToken.FromObject(value);
            }

            if (value is Enum enumValue)
            {
                return new JValue(state.TruncateString(enumValue.ToString()));
            }

            if (value is IDictionary dictionary)
            {
                return SerializeDictionary(dictionary, state, depth + 1);
            }

            if (value is IEnumerable enumerable)
            {
                return SerializeEnumerable(enumerable, state, depth + 1);
            }

            try
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });
                serializer.Converters.Add(new UnityObjectSummaryJsonConverter(state));
                return BoundToken(JToken.FromObject(value, serializer), state, depth + 1);
            }
            catch
            {
                state.MarkTruncated("unsupported");
                return new JObject
                {
                    ["type"] = value.GetType().FullName ?? value.GetType().Name,
                    ["text"] = state.TruncateString(value.ToString())
                };
            }
        }

        private static JToken SerializeDictionary(IDictionary dictionary, RoslynExecutionSerializationState state, int depth)
        {
            var result = new JObject();
            var count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count >= RoslynExecutionContracts.MaxCollectionLength)
                {
                    state.MarkTruncated("maxCollectionLength");
                    break;
                }

                var propertyName = state.TruncateString(entry.Key != null ? entry.Key.ToString() : "null");
                result[propertyName] = SerializeValue(entry.Value, state, depth);
                count++;
            }

            return result;
        }

        private static JToken SerializeEnumerable(IEnumerable enumerable, RoslynExecutionSerializationState state, int depth)
        {
            var result = new JArray();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count >= RoslynExecutionContracts.MaxCollectionLength)
                {
                    state.MarkTruncated("maxCollectionLength");
                    break;
                }

                result.Add(SerializeValue(item, state, depth));
                count++;
            }

            return result;
        }

        private static JToken BoundToken(JToken token, RoslynExecutionSerializationState state, int depth)
        {
            if (token == null)
            {
                return JValue.CreateNull();
            }

            switch (token.Type)
            {
                case JTokenType.Array:
                    if (depth >= RoslynExecutionContracts.MaxDepth)
                    {
                        state.MarkTruncated("maxDepth");
                        return new JValue("[truncated:maxDepth]");
                    }

                    var boundedArray = new JArray();
                    var array = (JArray)token;
                    for (var index = 0; index < array.Count && index < RoslynExecutionContracts.MaxCollectionLength; index++)
                    {
                        boundedArray.Add(BoundToken(array[index], state, depth + 1));
                    }

                    if (array.Count > RoslynExecutionContracts.MaxCollectionLength)
                    {
                        state.MarkTruncated("maxCollectionLength");
                    }

                    return boundedArray;

                case JTokenType.Object:
                    if (depth >= RoslynExecutionContracts.MaxDepth)
                    {
                        state.MarkTruncated("maxDepth");
                        return new JValue("[truncated:maxDepth]");
                    }

                    var boundedObject = new JObject();
                    var count = 0;
                    foreach (var property in ((JObject)token).Properties())
                    {
                        if (count >= RoslynExecutionContracts.MaxCollectionLength)
                        {
                            state.MarkTruncated("maxCollectionLength");
                            break;
                        }

                        boundedObject[state.TruncateString(property.Name)] = BoundToken(property.Value, state, depth + 1);
                        count++;
                    }

                    return boundedObject;

                case JTokenType.String:
                    return new JValue(state.TruncateString(token.Value<string>()));

                default:
                    return token.DeepClone();
            }
        }

        internal static JObject CreateUnityObjectSummary(UnityEngine.Object target, RoslynExecutionSerializationState state)
        {
            var gameObject = target as GameObject ?? (target as Component)?.gameObject;
            var assetPath = AssetDatabase.GetAssetPath(target);
            var normalizedAssetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Replace('\\', '/');
            var scenePath = gameObject != null && gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(gameObject.scene.path)
                ? gameObject.scene.path.Replace('\\', '/')
                : null;

            return new JObject
            {
                ["type"] = target.GetType().FullName ?? target.GetType().Name,
                ["name"] = state.TruncateString(target.name),
                ["instanceId"] = target.GetInstanceID(),
                ["assetPath"] = normalizedAssetPath != null ? JToken.FromObject(state.TruncateString(normalizedAssetPath)) : JValue.CreateNull(),
                ["scenePath"] = scenePath != null ? JToken.FromObject(state.TruncateString(scenePath)) : JValue.CreateNull(),
                ["hierarchyPath"] = gameObject != null ? JToken.FromObject(state.TruncateString(GetHierarchyPath(gameObject))) : JValue.CreateNull()
            };
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var segments = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments.ToArray());
        }

        private static string DetermineResultKind(JToken result)
        {
            if (result == null || result.Type == JTokenType.Null)
            {
                return "null";
            }

            if (LooksLikeUnityObjectSummary(result))
            {
                return "unity_object";
            }

            if (result.Type == JTokenType.Array)
            {
                var items = (JArray)result;
                if (items.Count > 0 && items.All(item => item.Type == JTokenType.Null || LooksLikeUnityObjectSummary(item)))
                {
                    return "unity_objects";
                }

                return "array";
            }

            if (result.Type == JTokenType.Object)
            {
                return "object";
            }

            return result.Type switch
            {
                JTokenType.Boolean => "bool",
                JTokenType.Integer => "number",
                JTokenType.Float => "number",
                JTokenType.String => "string",
                _ => "json"
            };
        }

        private static bool LooksLikeUnityObjectSummary(JToken token)
        {
            if (token is not JObject obj)
            {
                return false;
            }

            return obj["type"]?.Type == JTokenType.String &&
                   obj["name"]?.Type == JTokenType.String &&
                   obj["instanceId"] != null &&
                   obj.Property("assetPath") != null &&
                   obj.Property("scenePath") != null &&
                   obj.Property("hierarchyPath") != null;
        }

        private static void EnforceByteLimit(JObject envelope, RoslynExecutionSerializationState state)
        {
            if (GetUtf8ByteCount(envelope) <= RoslynExecutionContracts.MaxBytes)
            {
                return;
            }

            state.MarkTruncated("maxBytes");
            envelope["truncated"] = true;
            envelope["truncationReason"] = "maxBytes";
            envelope["result"] = new JObject
            {
                ["truncated"] = true,
                ["reason"] = "maxBytes"
            };

            var error = envelope.Value<string>("error") ?? string.Empty;
            while (GetUtf8ByteCount(envelope) > RoslynExecutionContracts.MaxBytes && error.Length > 0)
            {
                error = error.Substring(0, error.Length / 2);
                envelope["error"] = error;
            }
        }

        private static int GetUtf8ByteCount(JToken token)
        {
            return Encoding.UTF8.GetByteCount(token.ToString(Formatting.None));
        }

        private static bool IsNumeric(object value)
        {
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        private static string TrimError(Exception exception)
        {
            var text = exception != null ? exception.ToString() : "Unknown serialization failure.";
            if (text.Length <= RoslynExecutionContracts.MaxStringLength)
            {
                return text;
            }

            return text.Substring(0, RoslynExecutionContracts.MaxStringLength);
        }
    }

    internal sealed class RoslynExecutionSerializationState
    {
        public RoslynExecutionSerializationState(RoslynExecutionLimit limit)
        {
            Limit = limit ?? RoslynExecutionContracts.CreateLimit();
        }

        public RoslynExecutionLimit Limit { get; }

        public bool Truncated { get; private set; }

        public string TruncationReason { get; private set; }

        public void MarkTruncated(string reason)
        {
            Truncated = true;
            if (string.IsNullOrWhiteSpace(TruncationReason))
            {
                TruncationReason = reason;
            }
        }

        public string TruncateString(string value)
        {
            if (value == null)
            {
                return null;
            }

            if (value.Length <= Limit.maxStringLength)
            {
                return value;
            }

            MarkTruncated("maxStringLength");
            return value.Substring(0, Limit.maxStringLength);
        }
    }

    internal sealed class UnityObjectSummaryJsonConverter : JsonConverter
    {
        private readonly RoslynExecutionSerializationState _state;

        public UnityObjectSummaryJsonConverter(RoslynExecutionSerializationState state)
        {
            _state = state;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            RoslynExecutionRuntimeSerializer.CreateUnityObjectSummary((UnityEngine.Object)value, _state).WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }
    }

    internal static class RoslynExecutionUtility
    {
        public static string CreateInvocationId(string commandId)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                return commandId.Replace(':', '_').Replace('/', '_');
            }

            return "exec_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        }

        public static string BuildWrappedSource(string body)
        {
            return
@"using System;
using UnityMcp.BuiltInPlugins.RoslynExecution;

public static class Entry
{
    public static string Run()
    {
        try
        {
            var result = __Run();
            try
            {
                return RoslynExecutionRuntimeSerializer.SerializeSuccess(result);
            }
            catch (Exception exception)
            {
                return RoslynExecutionRuntimeSerializer.SerializeSerializationFailure(exception);
            }
        }
        catch (Exception exception)
        {
            return RoslynExecutionRuntimeSerializer.SerializeExecutionException(exception);
        }
    }

    private static object __Run()
    {
        " + body + @"
    }
}";
        }

        public static RoslynReferenceProfile BuildReferenceProfile()
        {
            var references = AppDomain.CurrentDomain.GetAssemblies()
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

            return new RoslynReferenceProfile
            {
                profileId = "unity-editor-loaded-assemblies",
                unityVersion = Application.unityVersion,
                references = references
            };
        }

        public static CompilerProcessResult ExecuteCompilerProcess(string filePath, string requestPath, string workingDirectory, int timeoutMs)
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

                return new CompilerProcessResult
                {
                    TimedOut = true,
                    DurationMs = (int)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds),
                    Stdout = stdoutTask.GetAwaiter().GetResult(),
                    Stderr = stderrTask.GetAwaiter().GetResult()
                };
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return new CompilerProcessResult
            {
                TimedOut = false,
                ExitCode = process.ExitCode,
                DurationMs = (int)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds),
                Stdout = stdoutTask.Result,
                Stderr = stderrTask.Result
            };
        }

        public static RoslynExecutionEnvelope ParseExecutionEnvelope(string rawExecutionJson)
        {
            if (string.IsNullOrWhiteSpace(rawExecutionJson))
            {
                throw new InvalidOperationException("Entry.Run returned an empty payload.");
            }

            var token = JToken.Parse(rawExecutionJson);
            if (!(token is JObject obj))
            {
                throw new InvalidOperationException("Entry.Run returned a non-object JSON payload.");
            }

            return new RoslynExecutionEnvelope
            {
                result = obj["result"],
                error = obj.Value<string>("error") ?? string.Empty,
                resultKind = obj.Value<string>("resultKind") ?? string.Empty,
                truncated = obj.Value<bool?>("truncated") ?? false,
                truncationReason = obj.Value<string>("truncationReason")
            };
        }

        public static string ComputeSha256(string content)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string MakeProjectRelative(string projectRoot, string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(absolutePath))
            {
                return absolutePath;
            }

            return Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class RoslynExecutionReportWriter
    {
        public static string Write(string projectRoot, string invocationId, object report)
        {
            var relativePath = Path.Combine("Temp", "AgentBridge", "reports", RoslynExecutionContracts.ReportPrefix + "_" + invocationId + ".json")
                .Replace('\\', '/');
            var absolutePath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, JsonConvert.SerializeObject(report, Formatting.None), new UTF8Encoding(false));
            return relativePath;
        }
    }

    internal sealed class CompileExecutionResult
    {
        public int DurationMs { get; set; }

        public int? ExitCode { get; set; }

        public string Stdout { get; set; }

        public string Stderr { get; set; }

        public bool ProcessTimedOut { get; set; }

        public string ErrorSummary { get; set; }

        public RoslynCompileResponse ProtocolResponse { get; set; }
    }

    internal sealed class CompilerProcessResult
    {
        public bool TimedOut { get; set; }

        public int? ExitCode { get; set; }

        public int DurationMs { get; set; }

        public string Stdout { get; set; }

        public string Stderr { get; set; }
    }

    internal sealed class RoslynCompileRequest
    {
        public string requestId;
        public string sourcePath;
        public string sourceText;
        public string outputDllPath;
        public string assemblyName;
        public RoslynReferenceProfile referenceProfile;
        public int timeoutMs;
    }

    internal sealed class RoslynReferenceProfile
    {
        public string profileId;
        public string unityVersion;
        public List<string> references = new List<string>();
    }

    internal sealed class RoslynCompileResponse
    {
        public bool success;
        public string exitStatus;
        public string errorSummary;
        public string outputDllPath;
        public string assemblyName;
        public List<RoslynCompileDiagnostic> diagnostics = new List<RoslynCompileDiagnostic>();
    }

    internal sealed class RoslynCompileDiagnostic
    {
        public string severity;
        public string id;
        public string message;
        public int? line;
        public int? column;
    }

    internal sealed class RoslynExecutionEnvelope
    {
        public JToken result;
        public string error;
        public string resultKind;
        public bool truncated;
        public string truncationReason;
    }

    internal sealed class RoslynExecutionReport
    {
        public RoslynExecutionMetrics metrics;
        public string compilerPath;
        public string generatedSourcePath;
        public string generatedDllPath;
        public string rawExecutionJson;
        public List<RoslynCompileDiagnostic> diagnostics;
        public string exception;
    }

    internal sealed class RoslynExecutionMetrics
    {
        public string contractVersion;
        public bool success;
        public string phase;
        public string invocationId;
        public string sourceHash;
        public RoslynExecutionMetricsStages stages;
        public RoslynExecutionResultEnvelope result;
        public string error;
        public string reportPath;
        public RoslynExecutionLimit limit;

        public static RoslynExecutionMetrics CreateSuccess(
            string invocationId,
            string sourceHash,
            CompileExecutionResult compileResponse,
            int loadDurationMs,
            string assemblyName,
            RoslynExecutionEnvelope executionEnvelope)
        {
            return new RoslynExecutionMetrics
            {
                contractVersion = RoslynExecutionContracts.MetricsContractVersion,
                success = true,
                phase = RoslynExecutionContracts.PhaseExecuted,
                invocationId = invocationId,
                sourceHash = sourceHash,
                stages = RoslynExecutionMetricsStages.Create(compileResponse, loadDurationMs, assemblyName),
                result = new RoslynExecutionResultEnvelope
                {
                    kind = string.IsNullOrWhiteSpace(executionEnvelope.resultKind) ? "json" : executionEnvelope.resultKind,
                    value = new RoslynExecutionWrapperValue
                    {
                        result = executionEnvelope.result,
                        error = executionEnvelope.error
                    },
                    truncated = executionEnvelope.truncated,
                    truncationReason = executionEnvelope.truncationReason,
                    limit = RoslynExecutionContracts.CreateLimit()
                },
                error = string.Empty,
                reportPath = null,
                limit = RoslynExecutionContracts.CreateLimit()
            };
        }

        public static RoslynExecutionMetrics CreateFailure(
            string invocationId,
            string sourceHash,
            string phase,
            string error,
            CompileExecutionResult compileResponse)
        {
            var normalizedError = error ?? string.Empty;
            if (string.Equals(phase, RoslynExecutionContracts.PhaseProxyFailed, StringComparison.Ordinal) &&
                !normalizedError.StartsWith("proxy_failed:", StringComparison.Ordinal))
            {
                normalizedError = "proxy_failed: " + normalizedError;
            }

            return new RoslynExecutionMetrics
            {
                contractVersion = RoslynExecutionContracts.MetricsContractVersion,
                success = false,
                phase = phase,
                invocationId = invocationId,
                sourceHash = sourceHash,
                stages = RoslynExecutionMetricsStages.Create(
                    compileResponse,
                    0,
                    compileResponse != null && compileResponse.ProtocolResponse != null ? compileResponse.ProtocolResponse.assemblyName : null),
                result = RoslynExecutionResultEnvelope.CreateNull(),
                error = normalizedError,
                reportPath = null,
                limit = RoslynExecutionContracts.CreateLimit()
            };
        }
    }

    internal sealed class RoslynExecutionMetricsStages
    {
        public RoslynExecutionCompilerStage compiler;
        public RoslynExecutionLoadStage load;

        public static RoslynExecutionMetricsStages Create(CompileExecutionResult compileResponse, int loadDurationMs, string assemblyName)
        {
            return new RoslynExecutionMetricsStages
            {
                compiler = new RoslynExecutionCompilerStage
                {
                    strategy = "external_proxy",
                    proxy = RoslynExecutionContracts.CompilerExecutableName,
                    durationMs = compileResponse != null ? compileResponse.DurationMs : 0,
                    exitCode = compileResponse != null ? compileResponse.ExitCode : null
                },
                load = new RoslynExecutionLoadStage
                {
                    durationMs = loadDurationMs,
                    assemblyName = assemblyName,
                    entryType = "Entry",
                    entryMethod = "Run",
                    scriptBodyMethod = "__Run"
                }
            };
        }

        public static RoslynExecutionMetricsStages CreateEmpty()
        {
            return Create(null, 0, null);
        }
    }

    internal sealed class RoslynExecutionCompilerStage
    {
        public string strategy;
        public string proxy;
        public int durationMs;
        public int? exitCode;
    }

    internal sealed class RoslynExecutionLoadStage
    {
        public int durationMs;
        public string assemblyName;
        public string entryType;
        public string entryMethod;
        public string scriptBodyMethod;
    }

    internal sealed class RoslynExecutionResultEnvelope
    {
        public string kind;
        public object value;
        public bool truncated;
        public string truncationReason;
        public RoslynExecutionLimit limit;

        public static RoslynExecutionResultEnvelope CreateNull()
        {
            return new RoslynExecutionResultEnvelope
            {
                kind = "null",
                value = null,
                truncated = false,
                truncationReason = null,
                limit = RoslynExecutionContracts.CreateLimit()
            };
        }
    }

    internal sealed class RoslynExecutionWrapperValue
    {
        public object result;
        public string error;
    }

    internal sealed class RoslynExecutionLimit
    {
        public int maxBytes;
        public int maxCollectionLength;
        public int maxDepth;
        public int maxStringLength;
    }
}
