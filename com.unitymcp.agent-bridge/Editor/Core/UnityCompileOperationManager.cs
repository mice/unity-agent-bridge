using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class UnityCompileOperationManager
    {
        private static readonly Dictionary<string, CompileOperationState> OperationsById = new Dictionary<string, CompileOperationState>(StringComparer.Ordinal);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex CompilerCodeRegex = new Regex(@"\b([A-Z]{2}\d{4})\b", RegexOptions.Compiled);
        private const int DiagnosticSampleLimit = 20;
        private static bool _subscribed;
        private static FileAgentBridgeLogger _logger;

        static UnityCompileOperationManager()
        {
            Subscribe();
            RestoreOperations();
            EditorApplication.delayCall += TryFinalizeRestoredOperations;
            EditorApplication.update += OnEditorUpdate;
        }

        public static ToolResult StartOrResume(AgentCommand command, AgentBridgeSettings settings)
        {
            if (command == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_NULL", "Command is required.");
            }

            if (settings == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_SETTINGS_NULL", "Settings are required.");
            }

            Subscribe();

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Exception,
                    summary = "Project root could not be resolved."
                };
            }

            var effectiveTimeoutMs = Math.Min(command.timeoutMs, settings.maxToolDurationMs);
            var timeoutTruncated = effectiveTimeoutMs != command.timeoutMs;
            var operationPath = GetOperationPath(projectRoot, settings.tempRoot, command.commandId);
            var lifecycleStore = new CompileLifecycleStore(projectRoot, settings.tempRoot);
            var lifecycle = CompileLifecycleStateMachine.EnsureInitialized(lifecycleStore.Read(), projectRoot);

            if (!OperationsById.TryGetValue(command.commandId, out var state))
            {
                state = new CompileOperationState
                {
                    commandId = command.commandId,
                    tool = command.tool,
                    projectRoot = projectRoot,
                    tempRoot = settings.tempRoot,
                    startedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    effectiveTimeoutMs = effectiveTimeoutMs,
                    timeoutTruncated = timeoutTruncated,
                    observedEpochAtStart = lifecycle.compileEpoch,
                    lifecycleStage = EditorApplication.isCompiling ? "waiting_for_finish" : "waiting_for_start",
                    lastTransition = EditorApplication.isCompiling ? "joined_active_compile" : "compile_requested",
                    lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    domainReloadRestored = false
                };
                LogCompileStage(state, "compile_requested", ToolResultStatus.Running, "compile command accepted");
            }
            else
            {
                state.projectRoot = projectRoot;
                state.tempRoot = settings.tempRoot;
                state.effectiveTimeoutMs = effectiveTimeoutMs;
                state.timeoutTruncated = timeoutTruncated;
                state.domainReloadRestored = true;
                state.reloadRestored = true;
                state.lastTransition = "compile_restored";
                state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (state.targetEpoch <= 0)
                {
                    state.lifecycleStage = "unknown_epoch_restored";
                    state.unknownEpochRestored = true;
                }
                LogCompileStage(state, "compile_restored", ToolResultStatus.Resuming, state.targetEpoch <= 0 ? "restored without known target epoch" : "restored with persisted target epoch");
            }

            if (EditorApplication.isCompiling)
            {
                var activeEpoch = lifecycle.lastStartedEpoch > 0 ? lifecycle.lastStartedEpoch : lifecycle.compileEpoch;
                state.targetEpoch = activeEpoch;
                state.lifecycleStage = "waiting_for_finish";
                state.lastTransition = state.domainReloadRestored ? "compile_restored" : "joined_active_compile";
                state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                LogCompileStage(state, "compile_started", ToolResultStatus.Running, $"joined active compile epoch {activeEpoch}");
            }
            else if (state.targetEpoch <= 0)
            {
                state.targetEpoch = 0;
                state.lifecycleStage = state.domainReloadRestored ? "unknown_epoch_restored" : "waiting_for_start";
                state.lastTransition = state.domainReloadRestored ? "compile_restored_unknown_epoch" : "compile_requested";
                state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }

            OperationsById[command.commandId] = state;
            PersistState(operationPath, state);

            lifecycle = CompileLifecycleStateMachine.RegisterWaitingCommand(lifecycle, state.commandId, state.targetEpoch, state.lifecycleStage, projectRoot);
            lifecycleStore.Write(lifecycle);

            if (!EditorApplication.isCompiling && !state.domainReloadRestored)
            {
                CompilationPipeline.RequestScriptCompilation();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            return new ToolResult
            {
                success = false,
                status = state.domainReloadRestored ? ToolResultStatus.Resuming : ToolResultStatus.Running,
                summary = BuildPendingSummary(state, lifecycle)
            };
        }

        private static void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            _subscribed = true;
        }

        private static void RestoreOperations()
        {
            OperationsById.Clear();

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var processingDirectory = Path.Combine(projectRoot, "Temp", "AgentBridge", "processing");
            if (!Directory.Exists(processingDirectory))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(processingDirectory, "*.compile.data"))
            {
                try
                {
                    var content = File.ReadAllText(path, Utf8NoBom);
                    var state = Newtonsoft.Json.JsonConvert.DeserializeObject<CompileOperationState>(content);
                    if (state == null || string.IsNullOrWhiteSpace(state.commandId))
                    {
                        continue;
                    }

                    state.projectRoot = projectRoot;
                    state.tempRoot = string.IsNullOrWhiteSpace(state.tempRoot) ? "Temp/AgentBridge" : state.tempRoot;
                    state.domainReloadRestored = true;
                    state.reloadRestored = true;
                    if (state.targetEpoch <= 0)
                    {
                        state.lifecycleStage = "unknown_epoch_restored";
                        state.unknownEpochRestored = true;
                    }

                    OperationsById[state.commandId] = state;
                }
                catch
                {
                }
            }
        }

        private static void TryFinalizeRestoredOperations()
        {
            EvaluateOperations(forceLifecycleRefresh: true);
        }

        private static void OnEditorUpdate()
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var timedOutIds = new List<string>();
            foreach (var pair in OperationsById)
            {
                if (pair.Value == null || pair.Value.effectiveTimeoutMs <= 0)
                {
                    continue;
                }

                if (!DateTime.TryParse(pair.Value.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAtUtc))
                {
                    continue;
                }

                if ((nowUtc - startedAtUtc).TotalMilliseconds > pair.Value.effectiveTimeoutMs)
                {
                    timedOutIds.Add(pair.Key);
                }
            }

            foreach (var commandId in timedOutIds)
            {
                if (!OperationsById.TryGetValue(commandId, out var state))
                {
                    continue;
                }

                state.timeoutReason = state.targetEpoch <= 0 ? "compile_start_timeout" : "compile_finish_timeout";
                state.lifecycleStage = "timeout";
                state.lastTransition = "compile_timeout";
                state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
                CompleteOperation(state, BuildTimeoutResult(state));
            }

            EvaluateOperations(forceLifecycleRefresh: false);
        }

        private static void OnCompilationStarted(object context)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var tempRoot = OperationsById.Values.FirstOrDefault()?.tempRoot ?? "Temp/AgentBridge";
            var lifecycleStore = new CompileLifecycleStore(projectRoot, tempRoot);
            var lifecycle = CompileLifecycleStateMachine.RecordCompilationStarted(lifecycleStore.Read(), nowUtc, projectRoot);

            foreach (var state in OperationsById.Values)
            {
                if (state == null)
                {
                    continue;
                }

                if (state.targetEpoch <= 0)
                {
                    state.targetEpoch = lifecycle.compileEpoch;
                    state.startedCallbackObserved = true;
                    state.lifecycleStage = "waiting_for_finish";
                    state.lastTransition = state.unknownEpochRestored ? "compile_rebound_after_restore" : "compile_started";
                    state.lastTransitionAtUtc = CompileLifecycleStateMachine.FormatUtc(nowUtc);
                    PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
                    LogCompileStage(state, "compile_started", ToolResultStatus.Running, $"compile epoch {state.targetEpoch} started");
                }

                lifecycle = CompileLifecycleStateMachine.RegisterWaitingCommand(lifecycle, state.commandId, state.targetEpoch, state.lifecycleStage, projectRoot);
            }

            lifecycleStore.Write(lifecycle);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var tempRoot = OperationsById.Values.FirstOrDefault()?.tempRoot ?? "Temp/AgentBridge";
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                var errorCount = messages?.Count(message => message.type == CompilerMessageType.Error) ?? 0;
                var warningCount = messages?.Count(message => message.type == CompilerMessageType.Warning) ?? 0;
                var lifecycleStore = new CompileLifecycleStore(projectRoot, tempRoot);
                var lifecycle = CompileLifecycleStateMachine.RecordAssemblyFinished(lifecycleStore.Read(), nowUtc, assemblyPath, errorCount, warningCount);
                lifecycleStore.Write(lifecycle);
            }

            foreach (var state in OperationsById.Values)
            {
                if (state == null)
                {
                    continue;
                }

                if (state.targetEpoch <= 0)
                {
                    continue;
                }

                state.startedCallbackObserved = true;
                state.lastAssemblyPath = NormalizePath(assemblyPath);
                state.assemblyFinishedCount++;
                AppendCompilerMessages(state, messages);
                state.lifecycleStage = "assembly_finished";
                state.lastTransition = "assembly_finished";
                state.lastTransitionAtUtc = CompileLifecycleStateMachine.FormatUtc(nowUtc);
                PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
                LogCompileStage(state, "assembly_finished", ToolResultStatus.Running, $"assembly {state.lastAssemblyPath ?? string.Empty} finished");
            }
        }

        private static void OnCompilationFinished(object context)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var tempRoot = OperationsById.Values.FirstOrDefault()?.tempRoot ?? "Temp/AgentBridge";
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                var lifecycleStore = new CompileLifecycleStore(projectRoot, tempRoot);
                lifecycleStore.Write(CompileLifecycleStateMachine.RecordCompilationFinished(lifecycleStore.Read(), DateTime.UtcNow));
            }

            EvaluateOperations(forceLifecycleRefresh: true);
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(condition) || condition.IndexOf("error CS", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            foreach (var state in OperationsById.Values)
            {
                if (state == null)
                {
                    continue;
                }

                state.consoleFallbackEntries.Add(new ConsoleFallbackEntry
                {
                    condition = condition,
                    stackTrace = stackTrace ?? string.Empty
                });
                PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
            }
        }

        private static void EvaluateOperations(bool forceLifecycleRefresh)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var tempRoot = OperationsById.Values.FirstOrDefault()?.tempRoot ?? "Temp/AgentBridge";
            var lifecycleStore = new CompileLifecycleStore(projectRoot, tempRoot);
            var lifecycle = CompileLifecycleStateMachine.EnsureInitialized(lifecycleStore.Read(), projectRoot);
            if (forceLifecycleRefresh)
            {
                lifecycle.projectPath = projectRoot;
                lifecycle.currentStage = lifecycle.currentStage ?? string.Empty;
            }

            foreach (var state in OperationsById.Values.ToArray())
            {
                if (state == null)
                {
                    continue;
                }

                if (state.targetEpoch <= 0 && state.unknownEpochRestored && lifecycle.lastStartedEpoch > state.observedEpochAtStart)
                {
                    state.targetEpoch = lifecycle.lastStartedEpoch;
                    state.lifecycleStage = "waiting_for_finish";
                    state.lastTransition = "compile_rebound_after_restore";
                    state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
                }

                if (state.targetEpoch > 0 && CompileLifecycleStateMachine.HasFinishedEpoch(lifecycle, state.targetEpoch))
                {
                    state.completedEpoch = state.targetEpoch;
                    state.finishedCallbackObserved = true;
                    state.lifecycleStage = "finished";
                    state.lastTransition = "compile_finished";
                    state.lastTransitionAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    PersistState(GetOperationPath(state.projectRoot, state.tempRoot, state.commandId), state);
                    LogCompileStage(state, "compile_finished", ToolResultStatus.Success, $"compile epoch {state.completedEpoch} finished");
                    CompleteOperation(state, BuildCompileResult(state, lifecycle));
                }
            }

            lifecycleStore.Write(RefreshActiveLifecycleState(lifecycle, projectRoot));
        }

        private static CompileLifecycleState RefreshActiveLifecycleState(CompileLifecycleState lifecycle, string projectRoot)
        {
            lifecycle = CompileLifecycleStateMachine.EnsureInitialized(lifecycle, projectRoot);
            lifecycle.activeCommandIds = OperationsById.Values
                .Where(state => state != null)
                .Select(state => state.commandId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            lifecycle.activeTargetEpochs = OperationsById.Values
                .Where(state => state != null && state.targetEpoch > 0)
                .Select(state => state.targetEpoch)
                .Distinct()
                .OrderBy(epoch => epoch)
                .ToList();
            if (OperationsById.Count == 0)
            {
                lifecycle.timeoutReason = string.Empty;
            }

            return lifecycle;
        }

        private static void CompleteOperation(CompileOperationState state, ToolResult result)
        {
            try
            {
                var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
                if (!string.IsNullOrWhiteSpace(state.tempRoot))
                {
                    settings.tempRoot = state.tempRoot;
                }

                if (result.warnings == null)
                {
                    result.warnings = new List<ToolWarning>();
                }

                if (state.timeoutTruncated)
                {
                    result.warnings.Add(new ToolWarning
                    {
                        code = "AGENTBRIDGE_TIMEOUT_TRUNCATED",
                        message = "effectiveTimeoutMs was truncated by settings.maxToolDurationMs."
                    });
                }

                var report = new CompileReport
                {
                    contractVersion = CompileMetrics.ContractVersion,
                    commandId = state.commandId,
                    targetEpoch = state.targetEpoch,
                    completedEpoch = state.completedEpoch,
                    observedEpochAtStart = state.observedEpochAtStart,
                    lifecycleStage = state.lifecycleStage,
                    timeoutReason = state.timeoutReason,
                    reloadRestored = state.reloadRestored,
                    unknownEpochRestored = state.unknownEpochRestored,
                    diagnostics = BuildFullDiagnostics(state),
                    consoleFallbackCount = state.consoleFallbackEntries?.Count ?? 0,
                    assemblyFinishedCount = state.assemblyFinishedCount,
                    warningCount = state.warningCount,
                    lastTransition = state.lastTransition,
                    lastTransitionAtUtc = state.lastTransitionAtUtc
                };
                result.reportPath = AgentBridgeReportWriter.WriteReport(settings, state.commandId, "compile", report);
                if (!string.IsNullOrWhiteSpace(result.metricsObjectJson))
                {
                    var metricsJson = JObject.Parse(result.metricsObjectJson);
                    if (metricsJson["details"] is JObject details)
                    {
                        details["reportPath"] = result.reportPath;
                    }

                    if (metricsJson["followUp"] is JObject followUp &&
                        followUp["recommended"]?.Value<bool>() == true &&
                        followUp["options"] is JArray options)
                    {
                        foreach (var option in options.OfType<JObject>())
                        {
                            if (string.Equals(option["tool"]?.Value<string>(), "unity.read_report", StringComparison.Ordinal) &&
                                option["args"] is JObject args &&
                                args["reportPath"] == null)
                            {
                                args["reportPath"] = result.reportPath;
                            }
                        }
                    }

                    result.metricsObjectJson = metricsJson.ToString(Newtonsoft.Json.Formatting.None);
                }

                if (string.IsNullOrWhiteSpace(result.startedAt))
                {
                    result.startedAt = state.startedAt;
                }

                result.finishedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (DateTime.TryParse(result.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAtUtc) &&
                    DateTime.TryParse(result.finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var finishedAtUtc))
                {
                    result.durationMs = (long)Math.Max(0, (finishedAtUtc - startedAtUtc).TotalMilliseconds);
                }

                result.commandId = state.commandId;
                result.tool = state.tool;
                result.logs ??= new List<ToolLog>();
                result.logs.Add(new ToolLog
                {
                    level = "info",
                    message = state.lastTransition ?? string.Empty,
                    timestamp = state.lastTransitionAtUtc ?? string.Empty
                });
                AttachLifecycleDiagnostics(state, result);

                var queue = new AgentCommandQueue(state.projectRoot, state.tempRoot);
                queue.Complete(state.commandId, result);
                LogCompileStage(state, "compile_result_written", result.status ?? ToolResultStatus.Exception, result.summary ?? "compile result written");
            }
            finally
            {
                DeleteOperation(state);
            }
        }

        private static ToolResult BuildCompileResult(CompileOperationState state, CompileLifecycleState lifecycle)
        {
            var fullErrors = (state.compilerErrors ?? new List<CompilerErrorRecord>())
                .Select(record => new ToolError
                {
                    code = string.IsNullOrWhiteSpace(record.code) ? "AGENTBRIDGE_COMPILE_ERROR" : record.code,
                    message = record.message,
                    file = record.file,
                    line = record.line,
                    column = record.column
                })
                .ToList();

            if (fullErrors.Count == 0)
            {
                fullErrors.AddRange(ParseFallbackErrors(state.consoleFallbackEntries));
            }

            var warningCount = state.warningCount;
            var success = fullErrors.Count == 0;
            var boundedErrors = fullErrors.Take(DiagnosticSampleLimit).ToList();
            var diagnosticSamples = BuildDiagnosticSamples(fullErrors, state.compilerWarnings);
            var summary = success
                ? warningCount > 0
                    ? $"Compile succeeded with {warningCount} warning(s)."
                    : "Compile succeeded."
                : $"Compile failed with {fullErrors.Count} error(s).";

            var details = ToolResultMetadata.CreateDetails(
                true,
                !success || warningCount > 0,
                "/diagnostics");
            var followUp = (!success || warningCount > 0)
                ? ToolResultMetadata.Recommended(
                    ToolResultMetadata.Option(
                        "unity.read_report",
                        "Read the compile report for full diagnostics and lifecycle evidence.",
                        new JObject()))
                : ToolResultMetadata.None();

            return new ToolResult
            {
                success = success,
                status = success ? ToolResultStatus.Success : ToolResultStatus.Failed,
                summary = summary,
                errors = boundedErrors,
                metricsObjectJson = JsonUtil.SerializeObject(new CompileMetrics
                {
                    contractVersion = CompileMetrics.ContractVersion,
                    errorCount = fullErrors.Count,
                    warningCount = warningCount,
                    diagnosticSampleCount = diagnosticSamples.Length,
                    diagnosticSamples = diagnosticSamples,
                    isCompiling = EditorApplication.isCompiling,
                    observedEpochAtStart = state.observedEpochAtStart,
                    targetEpoch = state.targetEpoch,
                    completedEpoch = state.completedEpoch,
                    lifecycleStage = state.lifecycleStage,
                    timeoutReason = state.timeoutReason ?? string.Empty,
                    reloadRestored = state.reloadRestored,
                    unknownEpochRestored = state.unknownEpochRestored,
                    lastTransition = state.lastTransition ?? string.Empty,
                    lastTransitionAtUtc = state.lastTransitionAtUtc ?? string.Empty,
                    compileEpoch = lifecycle?.compileEpoch ?? 0,
                    details = details,
                    followUp = followUp
                })
            };
        }

        private static ToolResult BuildTimeoutResult(CompileOperationState state)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Timeout,
                summary = state.targetEpoch <= 0 ? "Compilation did not start before timeout." : "Compilation timed out before the target epoch finished.",
                logs = new List<ToolLog>
                {
                    new ToolLog
                    {
                        level = "warning",
                        message = state.timeoutReason ?? "compile_timeout",
                        timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    }
                },
                metricsObjectJson = JsonUtil.SerializeObject(new CompileMetrics
                {
                    contractVersion = CompileMetrics.ContractVersion,
                    errorCount = 0,
                    warningCount = state.warningCount,
                    diagnosticSampleCount = 0,
                    diagnosticSamples = Array.Empty<CompileDiagnosticSample>(),
                    isCompiling = EditorApplication.isCompiling,
                    observedEpochAtStart = state.observedEpochAtStart,
                    targetEpoch = state.targetEpoch,
                    completedEpoch = state.completedEpoch,
                    lifecycleStage = state.lifecycleStage,
                    timeoutReason = state.timeoutReason ?? string.Empty,
                    reloadRestored = state.reloadRestored,
                    unknownEpochRestored = state.unknownEpochRestored,
                    lastTransition = state.lastTransition ?? string.Empty,
                    lastTransitionAtUtc = state.lastTransitionAtUtc ?? string.Empty,
                    compileEpoch = 0,
                    details = ToolResultMetadata.CreateDetails(true, true, "/diagnostics"),
                    followUp = ToolResultMetadata.Recommended(
                        ToolResultMetadata.Option(
                            "unity.read_report",
                            "Read the compile report for timeout diagnostics and lifecycle evidence.",
                            new JObject()))
                })
            };
        }

        private static ToolError[] BuildFullDiagnostics(CompileOperationState state)
        {
            var errors = (state.compilerErrors ?? new List<CompilerErrorRecord>())
                .Select(record => new ToolError
                {
                    code = string.IsNullOrWhiteSpace(record.code) ? "AGENTBRIDGE_COMPILE_ERROR" : record.code,
                    message = record.message,
                    file = record.file,
                    line = record.line,
                    column = record.column
                })
                .ToList();

            if (errors.Count == 0)
            {
                errors.AddRange(ParseFallbackErrors(state.consoleFallbackEntries));
            }

            errors.AddRange((state.compilerWarnings ?? new List<CompilerWarningRecord>())
                .Select(record => new ToolError
                {
                    code = string.IsNullOrWhiteSpace(record.code) ? "AGENTBRIDGE_COMPILE_WARNING" : record.code,
                    message = record.message,
                    file = record.file,
                    line = record.line,
                    column = record.column
                }));
            return errors.ToArray();
        }

        private static CompileDiagnosticSample[] BuildDiagnosticSamples(IReadOnlyList<ToolError> errors, IReadOnlyList<CompilerWarningRecord> warnings)
        {
            var samples = new List<CompileDiagnosticSample>(DiagnosticSampleLimit);
            foreach (var error in errors.Take(DiagnosticSampleLimit))
            {
                samples.Add(new CompileDiagnosticSample
                {
                    severity = "error",
                    code = error.code,
                    message = error.message,
                    file = error.file,
                    line = error.line,
                    column = error.column
                });
            }

            if (samples.Count < DiagnosticSampleLimit && warnings != null)
            {
                foreach (var warning in warnings.Take(DiagnosticSampleLimit - samples.Count))
                {
                    samples.Add(new CompileDiagnosticSample
                    {
                        severity = "warning",
                        code = warning.code,
                        message = warning.message,
                        file = warning.file,
                        line = warning.line,
                        column = warning.column
                    });
                }
            }

            return samples.ToArray();
        }

        private static void AttachLifecycleDiagnostics(CompileOperationState state, ToolResult result)
        {
            if (state == null || result == null || string.IsNullOrWhiteSpace(state.projectRoot))
            {
                return;
            }

            StaleBridgeStateDiagnostics diagnostics = null;
            if (string.Equals(result.status, ToolResultStatus.Timeout, StringComparison.Ordinal))
            {
                diagnostics = StaleBridgeStateDiagnosticsCollector.CollectForInvalidCommand(state.projectRoot, state.tempRoot, string.Empty, "createdAt", ToolResult.InvalidArgs("AGENTBRIDGE_COMPILE_TIMEOUT", state.timeoutReason ?? "compile_timeout"));
            }
            else if (string.Equals(result.status, ToolResultStatus.Failed, StringComparison.Ordinal) &&
                     result.errors != null &&
                     result.errors.Count > 0)
            {
                diagnostics = new StaleBridgeStateDiagnostics
                {
                    primaryClassification = "source_or_compiler",
                    evidencePriorityPath = "source_or_compiler",
                    detectedProjectPath = state.projectRoot.Replace('\\', '/'),
                    configuredProjectPath = state.projectRoot.Replace('\\', '/'),
                    projectBindingKind = "bound",
                    runtimeIdentity = Application.unityVersion ?? string.Empty,
                    executableIdentity = Environment.CommandLine ?? string.Empty,
                    sourceDiagnosticHint = result.errors[0].code ?? string.Empty
                };
            }

            if (diagnostics != null)
            {
                StaleBridgeStateDiagnosticsCollector.AttachToResultMetrics(result, diagnostics);
            }
        }

        private static string BuildPendingSummary(CompileOperationState state, CompileLifecycleState lifecycle)
        {
            if (state.targetEpoch > 0)
            {
                return $"Compilation is in progress for epoch {state.targetEpoch}.";
            }

            if (state.unknownEpochRestored)
            {
                return "Compilation resumed after reload and is waiting for a new compile epoch.";
            }

            return lifecycle.isCompiling ? "Compilation is in progress." : "Compilation requested.";
        }

        private static List<ToolError> ParseFallbackErrors(IReadOnlyList<ConsoleFallbackEntry> entries)
        {
            var results = new List<ToolError>();
            if (entries == null)
            {
                return results;
            }

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.condition))
                {
                    continue;
                }

                var condition = entry.condition;
                var codeIndex = condition.IndexOf("CS", StringComparison.OrdinalIgnoreCase);
                var code = codeIndex >= 0 && condition.Length >= codeIndex + 7
                    ? condition.Substring(codeIndex, Math.Min(7, condition.Length - codeIndex)).TrimEnd(':')
                    : "AGENTBRIDGE_COMPILE_ERROR";

                results.Add(new ToolError
                {
                    code = code,
                    message = condition
                });
            }

            return results;
        }

        private static void AppendCompilerMessages(CompileOperationState state, CompilerMessage[] messages)
        {
            if (state.compilerErrors == null)
            {
                state.compilerErrors = new List<CompilerErrorRecord>();
            }
            if (state.compilerWarnings == null)
            {
                state.compilerWarnings = new List<CompilerWarningRecord>();
            }

            if (messages == null)
            {
                return;
            }

            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    state.compilerErrors.Add(new CompilerErrorRecord
                    {
                        code = ExtractCompilerCode(message.message),
                        message = message.message,
                        file = NormalizePath(message.file),
                        line = message.line,
                        column = message.column
                    });
                }
                else if (message.type == CompilerMessageType.Warning)
                {
                    state.warningCount++;
                    state.compilerWarnings.Add(new CompilerWarningRecord
                    {
                        code = ExtractCompilerCode(message.message),
                        message = message.message,
                        file = NormalizePath(message.file),
                        line = message.line,
                        column = message.column
                    });
                }
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return path.Replace('\\', '/');
        }

        private static string ExtractCompilerCode(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "AGENTBRIDGE_COMPILE_ERROR";
            }

            var match = CompilerCodeRegex.Match(message);
            return match.Success ? match.Groups[1].Value : "AGENTBRIDGE_COMPILE_ERROR";
        }

        private static string GetOperationPath(string projectRoot, string tempRoot, string commandId)
        {
            var queueRoot = Path.GetFullPath(Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar)));
            return Path.Combine(queueRoot, "processing", commandId + ".compile.data");
        }

        private static void PersistState(string path, CompileOperationState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var tempPath = path + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(state, Newtonsoft.Json.Formatting.None));
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }

        private static void DeleteOperation(CompileOperationState state)
        {
            OperationsById.Remove(state.commandId);
            var path = GetOperationPath(state.projectRoot, state.tempRoot, state.commandId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var lifecycleStore = new CompileLifecycleStore(state.projectRoot, state.tempRoot);
            var lifecycle = RefreshActiveLifecycleState(lifecycleStore.Read(), state.projectRoot);
            lifecycle = CompileLifecycleStateMachine.UnregisterCommand(lifecycle, state.commandId, state.targetEpoch);
            lifecycleStore.Write(lifecycle);
        }

        private static void LogCompileStage(CompileOperationState state, string stage, string status, string message)
        {
            if (state == null)
            {
                return;
            }

            if (_logger == null && !string.IsNullOrWhiteSpace(state.projectRoot))
            {
                try
                {
                    var settings = AgentBridgeSettingsLoader.CreateDefaultSettings();
                    if (!string.IsNullOrWhiteSpace(state.tempRoot))
                    {
                        settings.tempRoot = state.tempRoot;
                    }

                    var paths = new AgentBridgePaths(state.projectRoot, settings);
                    paths.EnsureDirectories();
                    _logger = new FileAgentBridgeLogger(paths.BridgeLogPath);
                }
                catch
                {
                    return;
                }
            }

            _logger?.Stage(stage, state.commandId, state.tool, status, message);
        }
    }

    [Serializable]
    internal sealed class CompileOperationState
    {
        public string commandId;
        public string tool;
        public string projectRoot;
        public string tempRoot;
        public string startedAt;
        public int effectiveTimeoutMs;
        public bool timeoutTruncated;
        public int warningCount;
        public int observedEpochAtStart;
        public int targetEpoch;
        public int completedEpoch;
        public bool startedCallbackObserved;
        public bool finishedCallbackObserved;
        public bool reloadRestored;
        public bool domainReloadRestored;
        public bool unknownEpochRestored;
        public string lifecycleStage;
        public string timeoutReason;
        public string lastTransition;
        public string lastTransitionAtUtc;
        public string lastAssemblyPath;
        public int assemblyFinishedCount;
        public List<CompilerErrorRecord> compilerErrors = new List<CompilerErrorRecord>();
        public List<CompilerWarningRecord> compilerWarnings = new List<CompilerWarningRecord>();
        public List<ConsoleFallbackEntry> consoleFallbackEntries = new List<ConsoleFallbackEntry>();
    }

    [Serializable]
    internal sealed class CompilerErrorRecord
    {
        public string code;
        public string message;
        public string file;
        public int line;
        public int column;
    }

    [Serializable]
    internal sealed class ConsoleFallbackEntry
    {
        public string condition;
        public string stackTrace;
    }

    [Serializable]
    internal sealed class CompilerWarningRecord
    {
        public string code;
        public string message;
        public string file;
        public int line;
        public int column;
    }

    [Serializable]
    internal sealed class CompileMetrics
    {
        public const string ContractVersion = "compile.v1";

        public string contractVersion = ContractVersion;
        public int errorCount;
        public int warningCount;
        public int diagnosticSampleCount;
        public CompileDiagnosticSample[] diagnosticSamples = Array.Empty<CompileDiagnosticSample>();
        public bool isCompiling;
        public int observedEpochAtStart;
        public int targetEpoch;
        public int completedEpoch;
        public int compileEpoch;
        public string lifecycleStage;
        public string timeoutReason;
        public bool reloadRestored;
        public bool unknownEpochRestored;
        public string lastTransition;
        public string lastTransitionAtUtc;
        public ToolResultDetailsMetadata details;
        public ToolFollowUpMetadata followUp;
    }

    [Serializable]
    internal sealed class CompileDiagnosticSample
    {
        public string severity;
        public string code;
        public string message;
        public string file;
        public int line;
        public int column;
    }

    [Serializable]
    internal sealed class CompileReport
    {
        public string contractVersion;
        public string commandId;
        public int observedEpochAtStart;
        public int targetEpoch;
        public int completedEpoch;
        public string lifecycleStage;
        public string timeoutReason;
        public bool reloadRestored;
        public bool unknownEpochRestored;
        public int assemblyFinishedCount;
        public int warningCount;
        public string lastTransition;
        public string lastTransitionAtUtc;
        public ToolError[] diagnostics;
        public int consoleFallbackCount;
    }
}
