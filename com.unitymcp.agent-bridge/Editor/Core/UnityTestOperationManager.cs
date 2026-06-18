using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class UnityTestOperationManager
    {
        private static readonly Dictionary<string, TestRunOperationState> OperationsById = new Dictionary<string, TestRunOperationState>(StringComparer.Ordinal);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Regex TestIdRegex = new Regex(@"^AGB_\d{3}$", RegexOptions.Compiled);
        private static TestRunnerApi _api;
        private static UnityTestCallbacks _callbacks;
        private static bool _callbacksRegistered;
        private static Func<bool> _testRunActiveProvider = IsUnityTestRunActive;

        static UnityTestOperationManager()
        {
            RestoreOperations();
            EditorApplication.update += OnEditorUpdate;
        }

        public static ToolResult StartOrResume(AgentCommand command, AgentBridgeSettings settings, TestMode testMode, UnityTestRunArgs args)
        {
            if (command == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_NULL", "Command is required.");
            }

            if (settings == null)
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_SETTINGS_NULL", "Settings are required.");
            }

            args ??= new UnityTestRunArgs();
            var validationError = ValidateRunArgs(args);
            if (validationError != null)
            {
                return validationError;
            }

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

            if (testMode == TestMode.PlayMode)
            {
                if (EditorApplication.isPlaying)
                {
                    return new ToolResult
                    {
                        success = false,
                        status = ToolResultStatus.Blocked,
                        summary = "PlayMode tests cannot start while the Editor is already playing."
                    };
                }

                if (EditorApplication.isCompiling)
                {
                    return new ToolResult
                    {
                        success = false,
                        status = ToolResultStatus.Blocked,
                        summary = "PlayMode tests cannot start while the Editor is compiling."
                    };
                }
            }

            if (_testRunActiveProvider())
            {
                return new ToolResult
                {
                    success = false,
                    status = ToolResultStatus.Blocked,
                    summary = "Unity Test Runner is already running. Nested test runs are blocked."
                };
            }

            EnsureApi();

            var effectiveTimeoutMs = Math.Min(command.timeoutMs, settings.maxToolDurationMs);
            var timeoutTruncated = effectiveTimeoutMs != command.timeoutMs;
            var statePath = GetStatePath(projectRoot, settings.tempRoot, command.commandId);

            if (OperationsById.TryGetValue(command.commandId, out var existingState))
            {
                return BuildDeferredResult(existingState, timeoutTruncated);
            }

            var state = new TestRunOperationState
            {
                commandId = command.commandId,
                tool = command.tool,
                projectRoot = projectRoot,
                tempRoot = settings.tempRoot,
                startedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                effectiveTimeoutMs = effectiveTimeoutMs,
                timeoutTruncated = timeoutTruncated,
                filter = DescribeFilterArgs(args),
                testMode = testMode == TestMode.PlayMode ? "PlayMode" : "EditMode",
                status = ToolResultStatus.Running
            };

            PersistState(statePath, state);
            OperationsById[state.commandId] = state;

            var runnerFilter = CreateRunnerFilter(testMode, args);

            var executionSettings = new ExecutionSettings(runnerFilter)
            {
                runSynchronously = false
            };
            state.runGuid = _api.Execute(executionSettings);
            PersistState(statePath, state);

            return BuildDeferredResult(state, timeoutTruncated);
        }

        private static void EnsureApi()
        {
            if (_api == null)
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            }

            if (_callbacks == null)
            {
                _callbacks = new UnityTestCallbacks();
            }

            if (_callbacksRegistered)
            {
                return;
            }

            _api.RegisterCallbacks(_callbacks);
            _callbacksRegistered = true;
        }

        private static bool IsUnityTestRunActive()
        {
            try
            {
                var method = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.NonPublic);
                return method != null && method.Invoke(null, Array.Empty<object>()) is bool isActive && isActive;
            }
            catch
            {
                return false;
            }
        }

        internal static void SetTestRunActiveProviderForTests(Func<bool> provider)
        {
            _testRunActiveProvider = provider ?? IsUnityTestRunActive;
        }

        internal static Filter CreateRunnerFilter(TestMode testMode, UnityTestRunArgs args)
        {
            var runnerFilter = new Filter
            {
                testMode = testMode
            };

            if (args == null)
            {
                return runnerFilter;
            }

            if (!string.IsNullOrWhiteSpace(args.filter))
            {
                runnerFilter.testNames = new[] { args.filter };
            }

            if (HasValues(args.testNames))
            {
                runnerFilter.testNames = args.testNames;
            }

            if (HasValues(args.assemblyNames))
            {
                runnerFilter.assemblyNames = args.assemblyNames;
            }

            if (HasValues(args.categoryNames))
            {
                runnerFilter.categoryNames = args.categoryNames;
            }

            if (HasValues(args.groupNames))
            {
                runnerFilter.groupNames = args.groupNames;
            }

            return runnerFilter;
        }

        private static ToolResult ValidateRunArgs(UnityTestRunArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.filter) && HasValues(args.testNames))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_CONFLICT", "Legacy filter cannot be combined with structured testNames.");
            }

            var wildcardError = ValidateNoWildcards(args.testNames, "testNames")
                ?? ValidateNoWildcards(args.assemblyNames, "assemblyNames")
                ?? ValidateNoWildcards(args.categoryNames, "categoryNames")
                ?? ValidateNoWildcards(args.groupNames, "groupNames");
            if (wildcardError != null)
            {
                return wildcardError;
            }

            return null;
        }

        private static ToolResult ValidateNoWildcards(string[] values, string fieldName)
        {
            if (!HasValues(values))
            {
                return null;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_EMPTY", $"{fieldName}[{i}] must not be empty.");
                }

                if (value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0)
                {
                    return ToolResult.InvalidArgs("AGENTBRIDGE_TEST_FILTER_WILDCARD_UNSUPPORTED", $"{fieldName}[{i}] does not support wildcard characters '*' or '?'.");
                }
            }

            return null;
        }

        private static bool HasValues(string[] values)
        {
            return values != null && values.Length > 0;
        }

        private static string DescribeFilterArgs(UnityTestRunArgs args)
        {
            if (args == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(args.filter))
            {
                parts.Add("filter=" + args.filter);
            }

            AppendFilterPart(parts, "testNames", args.testNames);
            AppendFilterPart(parts, "assemblyNames", args.assemblyNames);
            AppendFilterPart(parts, "categoryNames", args.categoryNames);
            AppendFilterPart(parts, "groupNames", args.groupNames);
            return string.Join("; ", parts);
        }

        private static void AppendFilterPart(List<string> parts, string fieldName, string[] values)
        {
            if (!HasValues(values))
            {
                return;
            }

            parts.Add(fieldName + "=" + string.Join(",", values));
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

            foreach (var path in Directory.GetFiles(processingDirectory, "*.state.json"))
            {
                try
                {
                    var content = File.ReadAllText(path, Utf8NoBom);
                    var state = Newtonsoft.Json.JsonConvert.DeserializeObject<TestRunOperationState>(content);
                    if (state == null || string.IsNullOrWhiteSpace(state.commandId))
                    {
                        continue;
                    }

                    if (!string.Equals(state.tool, "unity.run_editmode_tests", StringComparison.Ordinal) &&
                        !string.Equals(state.tool, "unity.run_playmode_tests", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    state.projectRoot = projectRoot;
                    state.tempRoot = string.IsNullOrWhiteSpace(state.tempRoot) ? "Temp/AgentBridge" : state.tempRoot;
                    OperationsById[state.commandId] = state;
                }
                catch
                {
                }
            }

            if (OperationsById.Count > 0)
            {
                EnsureApi();
            }
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

                CompleteOperation(state, BuildTimeoutResult(state));
            }
        }

        private static void OnRunStarted(ITestAdaptor testsToRun)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var state = MatchState(testsToRun?.TestMode ?? TestMode.EditMode);
            if (state == null)
            {
                return;
            }

            state.status = ToolResultStatus.Running;
            state.leafResults ??= new List<SerializedTestCaseResult>();
            state.leafResults.Clear();
            PersistState(GetStatePath(state.projectRoot, state.tempRoot, state.commandId), state);
        }

        private static void OnTestFinished(ITestResultAdaptor result)
        {
            if (OperationsById.Count == 0 || result == null)
            {
                return;
            }

            if (result.HasChildren && result.Children.Any())
            {
                return;
            }

            var state = MatchState(result.Test?.TestMode ?? TestMode.EditMode);
            if (state == null)
            {
                return;
            }

            state.leafResults ??= new List<SerializedTestCaseResult>();
            MergeLeafResult(state.leafResults, MapLeafResult(result));
        }

        private static void OnRunFinished(ITestResultAdaptor result)
        {
            if (OperationsById.Count == 0)
            {
                return;
            }

            var state = MatchState(result?.Test?.TestMode ?? TestMode.EditMode);
            if (state == null)
            {
                return;
            }

            CompleteOperation(state, BuildRunFinishedResult(state, result));
        }

        private static TestRunOperationState MatchState(TestMode testMode)
        {
            var targetMode = testMode == TestMode.PlayMode ? "PlayMode" : "EditMode";
            return OperationsById.Values.FirstOrDefault(value => value != null && string.Equals(value.testMode, targetMode, StringComparison.Ordinal))
                ?? OperationsById.Values.FirstOrDefault(value => value != null);
        }

        private static ToolResult BuildRunFinishedResult(TestRunOperationState state, ITestResultAdaptor rootResult)
        {
            var cases = state.leafResults != null && state.leafResults.Count > 0
                ? new List<SerializedTestCaseResult>(state.leafResults)
                : new List<SerializedTestCaseResult>();
            if (cases.Count == 0 && rootResult != null)
            {
                CollectLeafResults(rootResult, cases);
            }

            var metricsJson = BuildMetricsJson(cases);
            var hasFailures = cases.Any(item => string.Equals(item.outcome, "Failed", StringComparison.Ordinal));
            var hasTests = cases.Count > 0;
            var summary = hasTests
                ? $"{state.testMode} test run completed with {cases.Count} test(s)."
                : $"{state.testMode} test run completed without matched tests.";

            var result = new ToolResult
            {
                success = hasTests && !hasFailures,
                status = hasTests && !hasFailures ? ToolResultStatus.Success : ToolResultStatus.Failed,
                summary = summary,
                metricsObjectJson = metricsJson
            };

            if (!hasTests)
            {
                result.errors.Add(new ToolError
                {
                    code = "AGENTBRIDGE_TESTS_NOT_FOUND",
                    message = "No tests matched the requested filter."
                });
            }

            return result;
        }

        private static void MergeLeafResult(List<SerializedTestCaseResult> results, SerializedTestCaseResult leafResult)
        {
            if (results == null || leafResult == null || string.IsNullOrWhiteSpace(leafResult.fullName))
            {
                return;
            }

            var existingIndex = results.FindIndex(item => string.Equals(item.fullName, leafResult.fullName, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                results[existingIndex] = leafResult;
                return;
            }

            results.Add(leafResult);
        }

        private static void CollectLeafResults(ITestResultAdaptor node, List<SerializedTestCaseResult> results)
        {
            if (node == null)
            {
                return;
            }

            if (!node.HasChildren || !node.Children.Any())
            {
                results.Add(MapLeafResult(node));
                return;
            }

            foreach (var child in node.Children)
            {
                CollectLeafResults(child, results);
            }
        }

        private static SerializedTestCaseResult MapLeafResult(ITestResultAdaptor result)
        {
            var categories = result.Test?.Categories ?? Array.Empty<string>();
            var testId = categories.FirstOrDefault(category => TestIdRegex.IsMatch(category ?? string.Empty)) ?? string.Empty;
            var category = categories.FirstOrDefault(item => !string.Equals(item, testId, StringComparison.Ordinal)) ?? string.Empty;

            return new SerializedTestCaseResult
            {
                testId = testId,
                fullName = result.FullName ?? string.Empty,
                outcome = result.TestStatus.ToString(),
                durationMs = Math.Max(1L, (long)Math.Round(result.Duration * 1000.0d, MidpointRounding.AwayFromZero)),
                category = category,
                recordPath = ResolveRecordPath(testId)
            };
        }

        private static string ResolveRecordPath(string testId)
        {
            if (string.IsNullOrWhiteSpace(testId))
            {
                return string.Empty;
            }

            var relativePath = "Documentation~/AgentBridge/test_records/" + testId + ".md";
            var candidateRoot = Directory.GetParent(Application.dataPath)?.FullName;
            while (!string.IsNullOrWhiteSpace(candidateRoot))
            {
                var absolutePath = Path.Combine(candidateRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    return relativePath.Replace('\\', '/');
                }

                candidateRoot = Directory.GetParent(candidateRoot)?.FullName;
            }

            return string.Empty;
        }

        private static string BuildMetricsJson(IReadOnlyList<SerializedTestCaseResult> tests)
        {
            var builder = new StringBuilder(512);
            builder.Append("{\"tests\":[");
            for (var index = 0; index < tests.Count; index++)
            {
                var test = tests[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                builder.Append("\"testId\":").Append(ToJsonString(test.testId)).Append(',');
                builder.Append("\"fullName\":").Append(ToJsonString(test.fullName)).Append(',');
                builder.Append("\"outcome\":").Append(ToJsonString(test.outcome)).Append(',');
                builder.Append("\"durationMs\":").Append(test.durationMs.ToString(CultureInfo.InvariantCulture)).Append(',');
                builder.Append("\"category\":").Append(ToJsonString(test.category)).Append(',');
                builder.Append("\"recordPath\":").Append(ToJsonString(test.recordPath));
                builder.Append('}');
            }

            builder.Append("],\"coverage\":{");
            builder.Append("\"enabled\":false,");
            builder.Append("\"lineCoverage\":null,");
            builder.Append("\"threshold\":null,");
            builder.Append("\"passed\":null");
            builder.Append("}}");
            return builder.ToString();
        }

        private static string ToJsonString(string value)
        {
            if (value == null)
            {
                return "null";
            }

            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "\"";
        }

        private static ToolResult BuildTimeoutResult(TestRunOperationState state)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Timeout,
                summary = $"{state.testMode} test run timed out.",
                metricsObjectJson = BuildMetricsJson(Array.Empty<SerializedTestCaseResult>())
            };
        }

        private static ToolResult BuildDeferredResult(TestRunOperationState state, bool timeoutTruncated)
        {
            var result = new ToolResult
            {
                success = false,
                status = string.Equals(state.status, ToolResultStatus.Resuming, StringComparison.Ordinal) ? ToolResultStatus.Resuming : ToolResultStatus.Running,
                summary = $"{state.testMode} test run is in progress."
            };

            if (timeoutTruncated)
            {
                result.warnings.Add(new ToolWarning
                {
                    code = "AGENTBRIDGE_TIMEOUT_TRUNCATED",
                    message = "effectiveTimeoutMs was truncated by settings.maxToolDurationMs."
                });
            }

            return result;
        }

        private static void CompleteOperation(TestRunOperationState state, ToolResult result)
        {
            try
            {
                var settings = ScriptableObject.CreateInstance<AgentBridgeSettings>();
                if (!string.IsNullOrWhiteSpace(state.tempRoot))
                {
                    settings.tempRoot = state.tempRoot;
                }

                result.commandId = state.commandId;
                result.tool = state.tool;
                result.startedAt = state.startedAt;
                result.finishedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (DateTime.TryParse(result.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var startedAtUtc) &&
                    DateTime.TryParse(result.finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var finishedAtUtc))
                {
                    result.durationMs = (long)Math.Max(0, (finishedAtUtc - startedAtUtc).TotalMilliseconds);
                }

                if (state.timeoutTruncated)
                {
                    result.warnings.Add(new ToolWarning
                    {
                        code = "AGENTBRIDGE_TIMEOUT_TRUNCATED",
                        message = "effectiveTimeoutMs was truncated by settings.maxToolDurationMs."
                    });
                }

                var report = new TestRunReport
                {
                    commandId = state.commandId,
                    tool = state.tool,
                    runGuid = state.runGuid,
                    testMode = state.testMode,
                    filter = state.filter
                };
                result.reportPath = AgentBridgeReportWriter.WriteReport(settings, state.commandId, string.Equals(state.testMode, "PlayMode", StringComparison.Ordinal) ? "playmode_tests" : "editmode_tests", report);

                var queue = new AgentCommandQueue(state.projectRoot, state.tempRoot);
                queue.Complete(state.commandId, result);
            }
            finally
            {
                DeleteOperation(state);
            }
        }

        private static void PersistState(string path, TestRunOperationState state)
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

        private static void DeleteOperation(TestRunOperationState state)
        {
            OperationsById.Remove(state.commandId);
            var path = GetStatePath(state.projectRoot, state.tempRoot, state.commandId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetStatePath(string projectRoot, string tempRoot, string commandId)
        {
            var queueRoot = Path.GetFullPath(Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar)));
            return Path.Combine(queueRoot, "processing", commandId + ".state.json");
        }

        private sealed class UnityTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                OnRunStarted(testsToRun);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                OnRunFinished(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                OnTestFinished(result);
            }
        }
    }

    [Serializable]
    internal sealed class TestRunOperationState
    {
        public string commandId;
        public string tool;
        public string projectRoot;
        public string tempRoot;
        public string status;
        public string startedAt;
        public int effectiveTimeoutMs;
        public bool timeoutTruncated;
        public string runGuid;
        public string filter;
        public string testMode;
        public List<SerializedTestCaseResult> leafResults = new List<SerializedTestCaseResult>();
    }

    [Serializable]
    internal sealed class SerializedTestCaseResult
    {
        public string testId;
        public string fullName;
        public string outcome;
        public long durationMs;
        public string category;
        public string recordPath;
    }

    [Serializable]
    internal sealed class TestRunReport
    {
        public string commandId;
        public string tool;
        public string runGuid;
        public string testMode;
        public string filter;
    }
}
