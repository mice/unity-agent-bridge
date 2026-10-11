using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentTaskEditor
{
    public sealed class RunPlayModeTestsTask : IAgentTask, ICallbacks
    {
        private readonly TestRunnerApi api;
        private readonly Filter filter;
        private readonly List<EditModeTestCaseEvidence> evidence = new List<EditModeTestCaseEvidence>();
        private IAgentTaskContext context;
        private bool started;
        private bool cleanedUp;

        public RunPlayModeTestsTask(TestRunnerApi api, Filter filter)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            if ((filter.testMode & TestMode.PlayMode) != TestMode.PlayMode)
                throw new ArgumentException("The task requires a PlayMode filter.", nameof(filter));
        }

        public bool CanCancel => false;
        public string RunGuid { get; private set; }

        public void Start(IAgentTaskContext taskContext)
        {
            context = taskContext ?? throw new ArgumentNullException(nameof(taskContext));
            api.RegisterCallbacks(this);
            started = true;
            try { RunGuid = api.Execute(new ExecutionSettings(filter) { runSynchronously = false }); }
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
            if (!started) throw new InvalidOperationException("PlayMode test task has not started.");
        }

        public void RequestCancellation(IAgentTaskContext taskContext) => throw new NotSupportedException("Unity Test Runner cancellation is not publicly supported.");
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result == null || result.HasChildren) return;
            var fullName = result.FullName ?? string.Empty;
            if (evidence.Any(item => item.fullName == fullName)) return;
            var categories = result.Test?.Categories ?? new string[0];
            evidence.Add(new EditModeTestCaseEvidence
            {
                testId = categories.FirstOrDefault(category => category != null && System.Text.RegularExpressions.Regex.IsMatch(category, "^AGB(?:M)?_[0-9]{3}$")) ?? string.Empty,
                fullName = fullName,
                outcome = result.TestStatus.ToString(),
                durationMs = Math.Max(1L, (long)Math.Round(result.Duration * 1000.0d, MidpointRounding.AwayFromZero)),
                category = categories.FirstOrDefault(category => category != null) ?? string.Empty
            });
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (context == null) return;
            CleanupRunner();
            var hasFailures = result != null && result.FailCount > 0;
            var hasTests = result != null && result.PassCount + result.FailCount + result.InconclusiveCount + result.SkipCount > 0;
            var payload = new EditModeTestTaskPayload { tests = evidence.ToArray() };
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
            UnityEngine.Object.DestroyImmediate(api);
        }
    }
}
