using System;
using NUnit.Framework;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PiAgentRunnerTests
    {
        [Test, Category("AGB_230")]
        public void SuccessfulProcessCompletesWithOutputAndReadOnlyArguments()
        {
            var process = new FakeProcess { HasExited = true, ExitCode = 0, StandardOutput = "inspection" };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions { ExecutablePath = "pi", Model = "deepseek/deepseek-flash" }, new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect files", "C:/repo"));
            runner.Start(context.Request, context);
            runner.Advance(context);
            Assert.IsTrue(context.Result.Success);
            StringAssert.Contains("--tools read,grep,find,ls", process.Arguments);
            StringAssert.Contains("--model", process.Arguments);
            StringAssert.Contains("deepseek/deepseek-flash", process.Arguments);
            StringAssert.Contains("-p", process.Arguments);
            Assert.AreEqual("C:/repo", process.WorkingDirectory);
        }

        [Test, Category("AGB_231")]
        public void NonZeroExitBecomesRunnerFailure()
        {
            var process = new FakeProcess { HasExited = true, ExitCode = 2, StandardError = "denied" };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            runner.Advance(context);
            Assert.IsFalse(context.Result.Success);
            Assert.AreEqual("PI_EXIT_NONZERO", context.Error.Code);
            StringAssert.Contains("denied", context.Error.Message);
        }

        [Test, Category("AGB_232")]
        public void CancellationKillsProcessAndCancelsContext()
        {
            var process = new FakeProcess { HasExited = false };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect")) { CancellationRequested = true };
            runner.Start(context.Request, context);
            runner.Advance(context);
            Assert.IsTrue(process.Killed);
            Assert.IsTrue(context.Cancelled);
        }

        [Test, Category("AGB_233")]
        public void StartupFailureIsContainedAsRunnerFailure()
        {
            var process = new FakeProcess { StartException = new InvalidOperationException("missing pi") };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            Assert.AreEqual("PI_START_FAILED", context.Error.Code);
        }

        private sealed class FakeFactory : IPiProcessFactory
        {
            private readonly FakeProcess process;
            public FakeFactory(FakeProcess process) { this.process = process; }
            public IPiProcess Create(string executablePath) => process;
        }

        private sealed class FakeProcess : IPiProcess
        {
            public string WorkingDirectory { get; set; }
            public string Arguments { get; set; }
            public bool HasExited { get; set; }
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; } = string.Empty;
            public string StandardError { get; set; } = string.Empty;
            public bool Killed { get; private set; }
            public Exception StartException { get; set; }
            public void Start() { if (StartException != null) throw StartException; }
            public void Kill() { Killed = true; HasExited = true; }
        }

        private sealed class FakeContext : IAgentRunnerContext
        {
            public FakeContext(AgentRunRequest request) { Request = request; }
            public AgentRunRequest Request { get; }
            public bool CancellationRequested { get; set; }
            public AgentRunResult Result { get; private set; }
            public AgentRunError Error { get; private set; }
            public bool Cancelled { get; private set; }
            public void Complete(AgentRunResult result) { Result = result; }
            public void Fail(AgentRunError error) { Error = error; Result = AgentRunResult.Failed(error); }
            public void Cancel(AgentRunResult result = null) { Cancelled = true; Result = result; }
        }
    }
}
