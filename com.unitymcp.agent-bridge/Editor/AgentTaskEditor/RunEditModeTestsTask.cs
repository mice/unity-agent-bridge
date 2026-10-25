using System;
using System.IO;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskCore;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.AgentTaskEditor
{
    public sealed class RunEditModeTestsTask : IAgentTask, ICallbacks
    {
        private readonly TestRunnerApi api;
        private readonly Filter filter;
        private readonly Func<string, object, string> reportWriter;
        private IAgentTaskContext context;
        private bool started;
        private readonly List<EditModeTestCaseEvidence> evidence = new List<EditModeTestCaseEvidence>();
        private bool cleanedUp;
        private readonly Action<string> runStarted;

        public RunEditModeTestsTask(TestRunnerApi api, Filter filter, Func<string, object, string> reportWriter = null, Action<string> runStarted = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            this.reportWriter = reportWriter;
            this.runStarted = runStarted;
            if ((filter.testMode & TestMode.EditMode) != TestMode.EditMode)
                throw new ArgumentException("The task requires an EditMode filter.", nameof(filter));
        }

        public bool CanCancel => false;
        public string RunGuid { get; private set; }
        public bool ExecutionEnded { get; private set; }

        public void Start(IAgentTaskContext taskContext)
        {
            context = taskContext ?? throw new ArgumentNullException(nameof(taskContext));
            api.RegisterCallbacks(this);
            started = true;
            try
            {
                RunGuid = api.Execute(new ExecutionSettings(filter) { runSynchronously = false });
            }
            catch (Exception exception)
            {
                ExecutionEnded = true;
                CleanupRunner();
                context.Fail(new AgentTaskError("AGENT_TASK_TEST_RUNNER_START", exception.Message, exception));
            }
        }

        public void Reattach(IAgentTaskContext taskContext, string runGuid)
        {
            context = taskContext ?? throw new ArgumentNullException(nameof(taskContext));
            RunGuid = runGuid;
            api.RegisterCallbacks(this);
            started = true;
        }

        public void Advance(IAgentTaskContext taskContext)
        {
            if (!started) throw new InvalidOperationException("Test task has not started.");
        }

        public void RequestCancellation(IAgentTaskContext taskContext)
        {
            throw new NotSupportedException("Unity Test Runner cancellation is not publicly supported.");
        }

        public void RunStarted(ITestAdaptor testsToRun) { runStarted?.Invoke(RunGuid); }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (result == null || result.HasChildren || result.Test?.IsSuite == true) return;
            var fullName = result.FullName ?? string.Empty;
            var existing = evidence.FindIndex(item => item.fullName == fullName);
            var categories = result.Test?.Categories ?? new string[0];
            var testId = categories.FirstOrDefault(category => category != null && System.Text.RegularExpressions.Regex.IsMatch(category, "^AGB(?:M|MP)?_[0-9]{3}$")) ?? string.Empty;
            var capturedCase = new EditModeTestCaseEvidence
            {
                testId = testId,
                fullName = fullName,
                outcome = result.TestStatus.ToString(),
                durationMs = Math.Max(1L, (long)Math.Round(result.Duration * 1000.0d, MidpointRounding.AwayFromZero)),
                category = categories.FirstOrDefault(category => category != testId) ?? string.Empty,
                message = result.Message ?? string.Empty,
                stackTrace = result.StackTrace ?? string.Empty,
                output = result.Output ?? string.Empty,
                resultState = result.ResultState ?? string.Empty
            };
            if (existing >= 0) evidence[existing] = capturedCase;
            else evidence.Add(capturedCase);
        }

        private void CollectFinalResults(ITestResultAdaptor result)
        {
            if (result == null) return;
            if (!result.HasChildren) { TestFinished(result); return; }
            if (result.Children != null)
                foreach (var child in result.Children) CollectFinalResults(child);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            ExecutionEnded = true;
            if ((context as IAgentTaskTerminalContext)?.IsTerminal == true) { CleanupRunner(); return; }
            if (context == null) return;
            // The final tree also contains leaves whose callbacks preceded Domain Reload.
            CollectFinalResults(result);
            CleanupRunner();
            if (result == null)
            {
                context.Fail(new AgentTaskError("AGENT_TASK_TEST_RUNNER_RESULT_MISSING", "Unity Test Runner returned no final result."));
                return;
            }
            var payload = new EditModeTestTaskPayload
            {
                tests = evidence.ToArray(),
                errors = evidence.Where(item => item.outcome == "Failed").Select(item => new EditModeTestDiagnostic
                {
                    code = "UNITY_TEST_FAILURE",
                    message = item.fullName + ": " + item.message + (string.IsNullOrWhiteSpace(item.stackTrace) ? string.Empty : "\n" + item.stackTrace),
                    stackTrace = item.stackTrace,
                    testName = item.fullName
                }).ToArray()
            };
            if (reportWriter != null)
            {
                try
                {
                    payload.reportPath = reportWriter(context.Id.Value, payload);
                    if (string.IsNullOrWhiteSpace(payload.reportPath)) throw new IOException("Report writer returned no report path.");
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    payload.reportError = exception.Message;
                    payload.warnings = new[] { new EditModeTestDiagnostic { code = "AGENT_TASK_REPORT_UNAVAILABLE", message = exception.Message } };
                }
            }
            var hasFailures = result.FailCount > 0;
            var hasTests = result.PassCount + result.FailCount + result.InconclusiveCount + result.SkipCount > 0;
            context.Complete(!hasTests ? AgentTaskResult.DomainFailure("NoTestsMatched", payload) : hasFailures
                ? AgentTaskResult.DomainFailure("TestAssertionsFailed", payload)
                : AgentTaskResult.Succeeded(payload));
        }

        private void CleanupRunner()
        {
            if (cleanedUp) return;
            cleanedUp = true;
            try { api.UnregisterCallbacks(this); } catch { }
            started = false;
            if (api != null) UnityEngine.Object.DestroyImmediate(api);
        }
    }

    [Serializable]
    public sealed class EditModeTestTaskPayload
    {
        public string schemaVersion = "1.0";
        public string payloadVersion = "editmode-tests.v1";
        public string generatedAt = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        public EditModeTestCaseEvidence[] tests = new EditModeTestCaseEvidence[0];
        public EditModeTestDiagnostic[] errors = new EditModeTestDiagnostic[0];
        public EditModeTestDiagnostic[] warnings = new EditModeTestDiagnostic[0];
        public string reportPath;
        public string reportError;
    }

    [Serializable]
    public sealed class EditModeTestCaseEvidence
    {
        public string testId;
        public string fullName;
        public string outcome;
        public long durationMs;
        public string category;
        public string message;
        public string stackTrace;
        public string output;
        public string resultState;
    }

    [Serializable]
    public sealed class EditModeTestDiagnostic
    {
        public string code;
        public string message;
        public string stackTrace;
        public string testName;
    }
}
