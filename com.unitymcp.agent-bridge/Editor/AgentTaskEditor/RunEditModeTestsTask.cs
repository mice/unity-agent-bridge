using System;
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
        private IAgentTaskContext context;
        private bool started;
        private readonly List<EditModeTestCaseEvidence> evidence = new List<EditModeTestCaseEvidence>();
        private bool cleanedUp;

        public RunEditModeTestsTask(TestRunnerApi api, Filter filter)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            if ((filter.testMode & TestMode.EditMode) != TestMode.EditMode)
                throw new ArgumentException("The task requires an EditMode filter.", nameof(filter));
        }

        public bool CanCancel => false;
        public string RunGuid { get; private set; }

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

        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (result == null || result.HasChildren) return;
            var fullName = result.FullName ?? string.Empty;
            if (evidence.Any(item => item.fullName == fullName)) return;
            var categories = result.Test?.Categories ?? new string[0];
            var testId = categories.FirstOrDefault(category => category != null && System.Text.RegularExpressions.Regex.IsMatch(category, "^AGB(?:M)?_[0-9]{3}$")) ?? string.Empty;
            evidence.Add(new EditModeTestCaseEvidence
            {
                testId = testId,
                fullName = fullName,
                outcome = result.TestStatus.ToString(),
                durationMs = Math.Max(1L, (long)Math.Round(result.Duration * 1000.0d, MidpointRounding.AwayFromZero)),
                category = categories.FirstOrDefault(category => category != testId) ?? string.Empty
            });
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (context == null) return;
            CleanupRunner();
            var hasFailures = result != null && result.FailCount > 0;
            var hasTests = result != null && result.PassCount + result.FailCount + result.InconclusiveCount + result.SkipCount > 0;
            if (!hasTests)
            {
                context.Complete(AgentTaskResult.DomainFailure("NoTestsMatched", new EditModeTestTaskPayload { tests = evidence.ToArray() }));
                return;
            }
            context.Complete(hasFailures
                ? AgentTaskResult.DomainFailure("TestAssertionsFailed", new EditModeTestTaskPayload { tests = evidence.ToArray() })
                : AgentTaskResult.Succeeded(new EditModeTestTaskPayload { tests = evidence.ToArray() }));
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
        public EditModeTestCaseEvidence[] tests = new EditModeTestCaseEvidence[0];
    }

    [Serializable]
    public sealed class EditModeTestCaseEvidence
    {
        public string testId;
        public string fullName;
        public string outcome;
        public long durationMs;
        public string category;
    }
}
