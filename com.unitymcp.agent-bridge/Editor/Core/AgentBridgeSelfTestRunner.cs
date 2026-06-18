using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UnityMcp.AgentBridge
{
    public sealed class AgentBridgeSelfTestRunner
    {
        private readonly IUnityToolFacade _facade;
        private readonly AgentCommandQueue _queue;
        private readonly AgentBridgeSettings _settings;
        private readonly FileAgentBridgeLogger _logger;
        private readonly Func<DateTime> _utcNowProvider;
        private ActiveSuiteState _activeSuite;
        private bool _isUpdateHooked;

        public AgentBridgeSelfTestRunner(
            IUnityToolFacade facade,
            AgentCommandQueue queue,
            AgentBridgeSettings settings,
            FileAgentBridgeLogger logger = null,
            Func<DateTime> utcNowProvider = null)
        {
            _facade = facade ?? throw new ArgumentNullException(nameof(facade));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        internal bool HasActiveSuite => _activeSuite != null && !_activeSuite.IsCompleted;

        internal string StatePath => Path.Combine(_queue.ProcessingDirectory, "self_test.state.json");

        public ToolResult StartOrResume(AgentCommand command, SelfTestRunOptions options, IAgentCancellation cancellation)
        {
            if (command == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_SELF_TEST_COMMAND_NULL", "Command is required.");
            }

            cancellation?.ThrowIfCancellationRequested();
            var effectiveOptions = NormalizeOptions(options);

            if (_activeSuite == null || _activeSuite.IsCompleted)
            {
                _activeSuite = CreateSuiteState(command, effectiveOptions);
            }

            RunUntilYieldOrCompletion(cancellation);
            PersistSuiteState();
            return BuildToolResult(command);
        }

        internal void Tick()
        {
            RunUntilYieldOrCompletion(NoOpAgentCancellation.Instance);
            PersistSuiteState();
        }

        private void RunUntilYieldOrCompletion(IAgentCancellation cancellation)
        {
            if (_activeSuite == null || _activeSuite.IsCompleted)
            {
                UnhookUpdate();
                return;
            }

            while (_activeSuite != null && !_activeSuite.IsCompleted)
            {
                cancellation?.ThrowIfCancellationRequested();
                if (CheckSuiteDeadline())
                {
                    break;
                }

                if (_activeSuite.WaitingForTerminalResult)
                {
                    HookUpdate();
                    if (!TryCompleteDeferredCase())
                    {
                        break;
                    }

                    continue;
                }

                if (_activeSuite.CurrentCaseIndex >= _activeSuite.Cases.Count)
                {
                    CompleteSuite();
                    break;
                }

                var definition = _activeSuite.Cases[_activeSuite.CurrentCaseIndex];
                var result = ExecuteCase(definition, cancellation);
                if (_activeSuite == null || _activeSuite.IsCompleted)
                {
                    break;
                }

                if (definition.IsOperationCase && IsIntermediateStatus(result.status))
                {
                    _activeSuite.WaitingForTerminalResult = true;
                    _activeSuite.CurrentOperationCommandId = _activeSuite.CurrentCaseCommand.commandId;
                    _activeSuite.CurrentCaseStartedAtUtc = ParseUtc(result.startedAt) ?? _utcNowProvider().ToUniversalTime();
                    _activeSuite.CurrentIntermediateStatus = result.status;
                    HookUpdate();
                    break;
                }

                RecordCaseResult(definition, result, _activeSuite.CurrentCaseStartedAtUtc);
                _activeSuite.CurrentCaseIndex++;
                if (!_activeSuite.Options.continueOnFailure && !_activeSuite.LastCasePassed)
                {
                    CancelRemainingCases();
                    CompleteSuite();
                    break;
                }
            }
        }

        private ToolResult ExecuteCase(SelfTestCaseDefinition definition, IAgentCancellation cancellation)
        {
            var remainingBudgetMs = GetRemainingBudgetMs();
            var command = definition.BuildCommand(_activeSuite.SuiteId, remainingBudgetMs, _utcNowProvider);
            _activeSuite.CurrentCaseCommand = command;
            _activeSuite.CurrentCaseStartedAtUtc = _utcNowProvider().ToUniversalTime();
            return _facade.Execute(command, cancellation ?? NoOpAgentCancellation.Instance);
        }

        private bool TryCompleteDeferredCase()
        {
            var resultPath = Path.Combine(_queue.OutboxDirectory, _activeSuite.CurrentOperationCommandId + ".result.json");
            if (!File.Exists(resultPath))
            {
                return false;
            }

            ToolResult result;
            try
            {
                var rawJson = File.ReadAllText(resultPath);
                result = ParseToolResult(rawJson);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (result == null || IsIntermediateStatus(result.status))
            {
                return false;
            }

            var definition = _activeSuite.Cases[_activeSuite.CurrentCaseIndex];
            RecordCaseResult(definition, result, _activeSuite.CurrentCaseStartedAtUtc);
            _activeSuite.WaitingForTerminalResult = false;
            _activeSuite.CurrentOperationCommandId = null;
            _activeSuite.CurrentIntermediateStatus = null;
            _activeSuite.CurrentCaseIndex++;
            if (!_activeSuite.Options.continueOnFailure && !_activeSuite.LastCasePassed)
            {
                CancelRemainingCases();
                CompleteSuite();
                return true;
            }

            if (_activeSuite.CurrentCaseIndex >= _activeSuite.Cases.Count)
            {
                CompleteSuite();
            }

            return true;
        }

        private bool CheckSuiteDeadline()
        {
            if (_activeSuite == null || _activeSuite.IsCompleted)
            {
                return false;
            }

            if (_utcNowProvider().ToUniversalTime() <= _activeSuite.DeadlineUtc)
            {
                return false;
            }

            if (_activeSuite.CurrentCaseIndex < _activeSuite.CaseResults.Length)
            {
                var current = _activeSuite.CaseResults[_activeSuite.CurrentCaseIndex];
                current.actualStatus = ToolResultStatus.Timeout;
                current.summary = "Self-test suite deadline exceeded.";
                current.startedAt = current.startedAt ?? FormatUtc(_activeSuite.CurrentCaseStartedAtUtc);
                current.finishedAt = FormatUtc(_utcNowProvider());
                current.durationMs = Math.Max(0, (long)(_utcNowProvider().ToUniversalTime() - _activeSuite.CurrentCaseStartedAtUtc).TotalMilliseconds);
                current.passed = false;
            }

            for (var index = _activeSuite.CurrentCaseIndex + 1; index < _activeSuite.CaseResults.Length; index++)
            {
                _activeSuite.CaseResults[index].actualStatus = ToolResultStatus.Cancelled;
                _activeSuite.CaseResults[index].summary = "Cancelled because the self-test suite deadline was exceeded.";
                _activeSuite.CaseResults[index].passed = false;
            }

            CompleteSuite();
            return true;
        }

        private void RecordCaseResult(SelfTestCaseDefinition definition, ToolResult result, DateTime startedAtUtc)
        {
            var caseResult = _activeSuite.CaseResults[_activeSuite.CurrentCaseIndex];
            caseResult.actualStatus = result.status;
            caseResult.summary = result.summary;
            caseResult.startedAt = result.startedAt ?? FormatUtc(startedAtUtc);
            caseResult.finishedAt = result.finishedAt ?? FormatUtc(_utcNowProvider());
            caseResult.durationMs = result.durationMs;
            caseResult.reportPath = result.reportPath;
            caseResult.warnings = result.warnings ?? new List<ToolWarning>();
            caseResult.errors = result.errors ?? new List<ToolError>();
            caseResult.metricsObjectJson = string.IsNullOrWhiteSpace(result.metricsObjectJson) ? "{}" : result.metricsObjectJson;
            caseResult.metrics = caseResult.metricsObjectJson;
            caseResult.passed = string.Equals(caseResult.expectedStatus, caseResult.actualStatus, StringComparison.Ordinal);
            _activeSuite.LastCasePassed = caseResult.passed;
        }

        private void CancelRemainingCases()
        {
            for (var index = _activeSuite.CurrentCaseIndex + 1; index < _activeSuite.CaseResults.Length; index++)
            {
                var caseResult = _activeSuite.CaseResults[index];
                if (string.IsNullOrWhiteSpace(caseResult.actualStatus))
                {
                    caseResult.actualStatus = ToolResultStatus.Cancelled;
                    caseResult.summary = "Cancelled because continueOnFailure was disabled and a previous case failed.";
                }
            }
        }

        private void CompleteSuite()
        {
            if (_activeSuite == null)
            {
                return;
            }

            _activeSuite.IsCompleted = true;
            _activeSuite.CompletedAtUtc = _utcNowProvider().ToUniversalTime();
            _activeSuite.Metrics = BuildMetrics();
            CompleteOuterCommandIfNeeded();
            DeleteStateFile();
            UnhookUpdate();
        }

        private void CompleteOuterCommandIfNeeded()
        {
            if (_activeSuite == null || _activeSuite.OuterCommandCompleted || _activeSuite.OuterCommand == null)
            {
                return;
            }

            var finalResult = BuildToolResult(_activeSuite.OuterCommand);
            _queue.Complete(_activeSuite.OuterCommand.commandId, finalResult);
            _activeSuite.OuterCommandCompleted = true;
        }

        private ToolResult BuildToolResult(AgentCommand command)
        {
            var metrics = _activeSuite?.Metrics ?? BuildMetrics();
            var result = new ToolResult
            {
                commandId = command.commandId,
                tool = command.tool,
                startedAt = _activeSuite?.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture) ?? _utcNowProvider().ToString("O", CultureInfo.InvariantCulture),
                finishedAt = (_activeSuite?.IsCompleted == true ? _activeSuite.CompletedAtUtc : _utcNowProvider().ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture),
                durationMs = _activeSuite == null ? 0 : Math.Max(0, (long)(((_activeSuite.IsCompleted ? _activeSuite.CompletedAtUtc : _utcNowProvider().ToUniversalTime()) - _activeSuite.StartedAtUtc).TotalMilliseconds)),
                metricsObjectJson = JsonUtil.SerializeObject(metrics)
            };

            if (_activeSuite != null && !_activeSuite.IsCompleted)
            {
                result.status = _activeSuite.HasReturnedInitialResponse ? ToolResultStatus.Resuming : ToolResultStatus.Running;
                result.success = false;
                result.summary = $"AgentBridge self-test is in progress: {CountCompletedCases()}/{metrics.caseCount} completed.";
                _activeSuite.HasReturnedInitialResponse = true;
                return result;
            }

            result.status = metrics.overallPassed ? ToolResultStatus.Success : ToolResultStatus.Failed;
            result.success = metrics.overallPassed;
            result.summary = metrics.overallPassed
                ? $"AgentBridge self-test passed: {metrics.passedCount}/{metrics.caseCount}."
                : $"AgentBridge self-test failed: {metrics.passedCount}/{metrics.caseCount} passed, {metrics.failedCount} failed.";
            return result;
        }

        private AgentBridgeSelfTestMetrics BuildMetrics()
        {
            var cases = _activeSuite?.CaseResults ?? Array.Empty<AgentBridgeSelfTestCaseResult>();
            var passedCount = 0;
            var failedCount = 0;
            var cancelledCount = 0;
            foreach (var caseResult in cases)
            {
                if (caseResult.passed)
                {
                    passedCount++;
                    continue;
                }

                if (string.Equals(caseResult.actualStatus, ToolResultStatus.Cancelled, StringComparison.Ordinal))
                {
                    cancelledCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(caseResult.actualStatus))
                {
                    failedCount++;
                }
            }

            return new AgentBridgeSelfTestMetrics
            {
                suiteVersion = "1.0",
                overallPassed = failedCount == 0 && cancelledCount == 0 && passedCount == cases.Length,
                caseCount = cases.Length,
                passedCount = passedCount,
                failedCount = failedCount,
                cancelledCount = cancelledCount,
                cases = cases
            };
        }

        private ActiveSuiteState CreateSuiteState(AgentCommand command, SelfTestRunOptions options)
        {
            var definitions = BuildCaseDefinitions(options);
            var state = new ActiveSuiteState
            {
                OuterCommand = command,
                SuiteId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = _utcNowProvider().ToUniversalTime(),
                DeadlineUtc = _utcNowProvider().ToUniversalTime().AddMilliseconds(options.timeoutMs),
                Options = options,
                Cases = definitions,
                CaseResults = CreateInitialCaseResults(definitions)
            };
            PersistSuiteState(state);
            return state;
        }

        private List<SelfTestCaseDefinition> BuildCaseDefinitions(SelfTestRunOptions options)
        {
            var cases = new List<SelfTestCaseDefinition>
            {
                SelfTestCaseDefinition.Immediate("ping", "basic", "unity.ping", ToolResultStatus.Success, "{}"),
                SelfTestCaseDefinition.Immediate("project_get_info", "read_only", "unity.project.get_info", ToolResultStatus.Success, "{}"),
                SelfTestCaseDefinition.Immediate("console", "read_only", "unity.get_console", ToolResultStatus.Success, "{\"types\":[\"error\"],\"count\":1}"),
                SelfTestCaseDefinition.Immediate("static_method_ok", "whitelist", "unity.run_static_method", ToolResultStatus.Success, "{\"typeName\":\"UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests\",\"methodName\":\"SelfTestOk\"}"),
                SelfTestCaseDefinition.Immediate("static_method_not_allowed", "whitelist", "unity.run_static_method", ToolResultStatus.InvalidArgs, "{\"typeName\":\"System.Console\",\"methodName\":\"Clear\"}"),
                SelfTestCaseDefinition.Immediate("unsupported_tool", "dispatch", "unity.__self_test_nonexistent__", ToolResultStatus.Unsupported, "{}")
            };

            if (options.includeEditModeCase)
            {
                cases.Add(SelfTestCaseDefinition.Operation("editmode_minimal", "tests", "unity.run_editmode_tests", ToolResultStatus.Success, "{\"filter\":\"UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests.DemoEditModeProbe_Passes\"}"));
            }

            if (options.includeDiagnosticCase)
            {
                cases.Add(SelfTestCaseDefinition.Immediate("diagnostic_scene", "diagnostic", "unity.run_diagnostic", ToolResultStatus.Success, "{\"diagnosticType\":\"scene\",\"targetPath\":\"Assets/Scenes/AppMain.unity\"}"));
            }

            return cases;
        }

        private static AgentBridgeSelfTestCaseResult[] CreateInitialCaseResults(IReadOnlyList<SelfTestCaseDefinition> definitions)
        {
            var results = new AgentBridgeSelfTestCaseResult[definitions.Count];
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                results[index] = new AgentBridgeSelfTestCaseResult
                {
                    id = definition.Id,
                    scenario = definition.Scenario,
                    tool = definition.ToolName,
                    expectedStatus = definition.ExpectedStatus,
                    actualStatus = string.Empty,
                    passed = false,
                    summary = string.Empty
                };
            }

            return results;
        }

        private void HookUpdate()
        {
            if (_isUpdateHooked)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            _isUpdateHooked = true;
        }

        private void UnhookUpdate()
        {
            if (!_isUpdateHooked)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _isUpdateHooked = false;
        }

        private void OnEditorUpdate()
        {
            Tick();
        }

        private int CountCompletedCases()
        {
            var count = 0;
            foreach (var caseResult in _activeSuite?.CaseResults ?? Array.Empty<AgentBridgeSelfTestCaseResult>())
            {
                if (!string.IsNullOrWhiteSpace(caseResult.actualStatus))
                {
                    count++;
                }
            }

            return count;
        }

        private long GetRemainingBudgetMs()
        {
            var remaining = (long)Math.Max(1, (_activeSuite.DeadlineUtc - _utcNowProvider().ToUniversalTime()).TotalMilliseconds);
            return Math.Min(remaining, _settings.maxToolDurationMs);
        }

        private SelfTestRunOptions NormalizeOptions(SelfTestRunOptions options)
        {
            if (options == null)
            {
                return new SelfTestRunOptions();
            }

            if (options.timeoutMs <= 0)
            {
                options.timeoutMs = 120000;
            }

            return options;
        }

        private void PersistSuiteState()
        {
            PersistSuiteState(_activeSuite);
        }

        private void PersistSuiteState(ActiveSuiteState suite)
        {
            if (suite == null || suite.IsCompleted)
            {
                DeleteStateFile();
                return;
            }

            var persisted = new PersistedSuiteState
            {
                suiteId = suite.SuiteId,
                startedAt = suite.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                deadlineAt = suite.DeadlineUtc.ToString("O", CultureInfo.InvariantCulture),
                currentCaseIndex = suite.CurrentCaseIndex,
                currentOperationCommandId = suite.CurrentOperationCommandId,
                currentIntermediateStatus = suite.CurrentIntermediateStatus,
                waitingForTerminalResult = suite.WaitingForTerminalResult,
                metricsObjectJson = JsonUtil.SerializeObject(BuildMetrics())
            };

            File.WriteAllText(StatePath, JsonUtil.SerializeObject(persisted));
        }

        private void DeleteStateFile()
        {
            if (File.Exists(StatePath))
            {
                File.Delete(StatePath);
            }
        }

        private static bool IsIntermediateStatus(string status)
        {
            return string.Equals(status, ToolResultStatus.Pending, StringComparison.Ordinal) ||
                   string.Equals(status, ToolResultStatus.Running, StringComparison.Ordinal) ||
                   string.Equals(status, ToolResultStatus.Resuming, StringComparison.Ordinal);
        }

        private static string FormatUtc(DateTime utcNow)
        {
            return utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseUtc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static ToolResult ParseToolResult(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            var json = JObject.Parse(rawJson);
            return new ToolResult
            {
                schemaVersion = json.Value<string>("schemaVersion") ?? JsonUtil.CurrentSchemaVersion,
                commandId = json.Value<string>("commandId"),
                tool = json.Value<string>("tool"),
                success = json.Value<bool?>("success") ?? false,
                status = json.Value<string>("status"),
                startedAt = json.Value<string>("startedAt"),
                finishedAt = json.Value<string>("finishedAt"),
                durationMs = json.Value<long?>("durationMs") ?? 0L,
                summary = json.Value<string>("summary"),
                reportPath = json.Value<string>("reportPath"),
                metricsObjectJson = json.TryGetValue("metrics", out var metricsToken) ? metricsToken.ToString() : "{}",
                warnings = ParseWarnings(json["warnings"]),
                errors = ParseErrors(json["errors"])
            };
        }

        private static List<ToolWarning> ParseWarnings(JToken token)
        {
            var warnings = new List<ToolWarning>();
            if (token is not JArray array)
            {
                return warnings;
            }

            foreach (var item in array)
            {
                warnings.Add(new ToolWarning
                {
                    code = item.Value<string>("code"),
                    message = item.Value<string>("message")
                });
            }

            return warnings;
        }

        private static List<ToolError> ParseErrors(JToken token)
        {
            var errors = new List<ToolError>();
            if (token is not JArray array)
            {
                return errors;
            }

            foreach (var item in array)
            {
                errors.Add(new ToolError
                {
                    code = item.Value<string>("code"),
                    message = item.Value<string>("message"),
                    file = item.Value<string>("file"),
                    line = item.Value<int?>("line") ?? 0,
                    column = item.Value<int?>("column") ?? 0
                });
            }

            return errors;
        }

        [Serializable]
        private sealed class PersistedSuiteState
        {
            public string suiteId;
            public string startedAt;
            public string deadlineAt;
            public int currentCaseIndex;
            public string currentOperationCommandId;
            public string currentIntermediateStatus;
            public bool waitingForTerminalResult;
            public string metricsObjectJson;
        }

        private sealed class ActiveSuiteState
        {
            public string SuiteId;
            public DateTime StartedAtUtc;
            public DateTime DeadlineUtc;
            public DateTime CompletedAtUtc;
            public SelfTestRunOptions Options;
            public List<SelfTestCaseDefinition> Cases;
            public AgentBridgeSelfTestCaseResult[] CaseResults;
            public AgentBridgeSelfTestMetrics Metrics;
            public int CurrentCaseIndex;
            public bool WaitingForTerminalResult;
            public bool IsCompleted;
            public bool HasReturnedInitialResponse;
            public bool OuterCommandCompleted;
            public AgentCommand OuterCommand;
            public string CurrentOperationCommandId;
            public string CurrentIntermediateStatus;
            public AgentCommand CurrentCaseCommand;
            public DateTime CurrentCaseStartedAtUtc;
            public bool LastCasePassed;
        }

        private sealed class SelfTestCaseDefinition
        {
            private readonly string _rawArgsJson;

            private SelfTestCaseDefinition(string id, string scenario, string toolName, string expectedStatus, string rawArgsJson, bool isOperationCase)
            {
                Id = id;
                Scenario = scenario;
                ToolName = toolName;
                ExpectedStatus = expectedStatus;
                _rawArgsJson = rawArgsJson;
                IsOperationCase = isOperationCase;
            }

            public string Id { get; }

            public string Scenario { get; }

            public string ToolName { get; }

            public string ExpectedStatus { get; }

            public bool IsOperationCase { get; }

            public AgentCommand BuildCommand(string suiteId, long timeoutMs, Func<DateTime> utcNowProvider)
            {
                return new AgentCommand
                {
                    schemaVersion = JsonUtil.CurrentSchemaVersion,
                    commandId = $"{suiteId}_{Id}",
                    tool = ToolName,
                    timeoutMs = (int)Math.Max(1, timeoutMs),
                    createdAt = utcNowProvider().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    rawArgsJson = _rawArgsJson
                };
            }

            public static SelfTestCaseDefinition Immediate(string id, string scenario, string toolName, string expectedStatus, string rawArgsJson)
            {
                return new SelfTestCaseDefinition(id, scenario, toolName, expectedStatus, rawArgsJson, false);
            }

            public static SelfTestCaseDefinition Operation(string id, string scenario, string toolName, string expectedStatus, string rawArgsJson)
            {
                return new SelfTestCaseDefinition(id, scenario, toolName, expectedStatus, rawArgsJson, true);
            }
        }
    }
}
