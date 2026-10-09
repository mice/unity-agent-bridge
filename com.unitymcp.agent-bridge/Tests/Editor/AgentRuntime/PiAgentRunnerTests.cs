using System;
using System.IO;
using System.Collections.Generic;
using UnityMcp.AgentIntelligentAnalysis;
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
            StringAssert.Contains("--mode rpc", process.Arguments);
            Assert.That(process.ConfiguredTask, Is.EqualTo("inspect files"));
            StringAssert.DoesNotContain("inspect files", process.Arguments);
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

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_261.md
        [Test, Category("AGB_261")]
        public void InvalidPolicyFailsBeforeProcessStarts()
        {
            var process = new FakeProcess { HasExited = true, ExitCode = 0 };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions { Tools = new[] { "bash" } }, new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect"));

            runner.Start(context.Request, context);

            Assert.AreEqual("PI_CONFIGURATION_INVALID", context.Error.Code);
            Assert.IsFalse(process.Started);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_256.md
        [Test, Category("AGB_256")]
        public void ProjectSettings_MissingFieldsUseDefaultsButExplicitInvalidValuesFail()
        {
            var legacy = PiAgentRunnerProjectSettings.Parse("{\"workspaceRoot\":\"D:/Repo\"}");
            Assert.That(PiAgentRunnerProjectSettings.Resolve(legacy, useEnvironment: false).TimeoutMilliseconds, Is.EqualTo(120000));
            var invalid = PiAgentRunnerProjectSettings.Parse("{\"piTimeoutMilliseconds\":0,\"piTools\":[\"bash\"]}");
            Assert.Throws<InvalidOperationException>(() => PiAgentRunnerProjectSettings.Resolve(invalid, useEnvironment: false));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_257.md
        [Test, Category("AGB_257")]
        public void ProjectSettings_RejectsMalformedSchemaTypesAndForbiddenFields()
        {
            foreach (var json in new[] { "{ bad json", "{\"piSchemaVersion\":\"9.0\"}",
                "{\"piArguments\":\"--tools bash\"}", "{\"prompt\":\"run shell\"}", "{\"writePath\":\"Assets/test.cs\"}",
                "{\"piMaxTurns\":\"8\"}", "{\"piTools\":[null]}" })
                Assert.Catch(() => PiAgentRunnerProjectSettings.Parse(json), json);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_258.md
        [Test, Category("AGB_258")]
        public void ResolvedTemplatePolicyBoundsBudgetsAndPreservesShortRequestTimeout()
        {
            var registry = new AnalysisTemplateRegistry();
            UnityProgrammerSpecialistFactory.Register(registry);
            var configured = new PiAgentRunnerSettings { Tools = new List<string> { "read" },
                MaxTurns = 32, MaxOutputBytes = 1048576, TimeoutMilliseconds = 900000 };
            var options = PiAgentRunnerProjectSettings.Resolve(configured,
                registry.Get(UnityProgrammerSpecialistFactory.TemplateId), 1, false);
            Assert.That(options.MaxTurns, Is.EqualTo(6));
            Assert.That(options.MaxOutputBytes, Is.EqualTo(32768));
            Assert.That(options.TimeoutMilliseconds, Is.EqualTo(1));
            Assert.That(options.Tools, Is.EqualTo(new[] { "read" }));
            var process = new FakeProcess { HasExited = true };
            var context = new FakeContext(new AgentRunRequest("inspect"));
            new PiAgentRunner(options, new FakeFactory(process)).Start(context.Request, context);
            Assert.That(process.Started, Is.True);
            StringAssert.Contains("--tools read ", process.Arguments);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_259.md
        [Test, Category("AGB_259")]
        public void ToOptionsRejectsUnknownEmptyOrNullToolsInsteadOfNormalizing()
        {
            foreach (var tools in new[] { new List<string> { "bash" }, new List<string>(), new List<string> { null }, new List<string> { "" } })
            {
                var settings = new PiAgentRunnerSettings { Tools = tools };
                Assert.Throws<InvalidOperationException>(() => settings.ToOptions());
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_260.md
        [Test, Category("AGB_260")]
        public void ProjectSettings_UnreadableOrMalformedFileDoesNotFallBack()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{ bad json");
                Assert.Catch(() => PiAgentRunnerProjectSettings.Read(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_263.md
        [Test, Category("AGB_263")]
        public void BudgetFailureAfterPartialTextIsNeverSuccessful()
        {
            var process = new FakeProcess { StandardOutput = "partial", FailureCode = "PI_TURN_LIMIT_EXCEEDED", FailureMessage = "ceiling" };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions { MaxTurns = 2 }, new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            runner.Advance(context);
            Assert.That(process.ConfiguredMaxTurns, Is.EqualTo(2));
            Assert.That(process.Killed, Is.True);
            Assert.That(context.Error.Code, Is.EqualTo("PI_TURN_LIMIT_EXCEEDED"));
            Assert.That(context.Result.Success, Is.False);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_264.md
        [Test, Category("AGB_264")]
        public void MissingGuardTimesOutWithoutFallingBack()
        {
            var now = new DateTime(2026, 1, 1);
            var process = new FakeProcess { GuardReady = false };
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process), () => now);
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            now = now.AddSeconds(5);
            runner.Advance(context);
            Assert.That(context.Error.Code, Is.EqualTo("PI_GUARD_NOT_READY"));
            Assert.That(process.Killed, Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_265.md
        [Test, Category("AGB_265")]
        public void ReadyGuardStillRespectsTaskTimeout()
        {
            var now = new DateTime(2026, 1, 1);
            var process = new FakeProcess();
            var runner = new PiAgentRunner(new PiAgentRunnerOptions { TimeoutMilliseconds = 1000 }, new FakeFactory(process), () => now);
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            now = now.AddSeconds(2);
            runner.Advance(context);
            Assert.That(context.Error.Code, Is.EqualTo("PI_TIMEOUT"));
            Assert.That(process.Killed, Is.True);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_266.md
        [Test, Category("AGB_266")]
        public void ProcessWithoutBudgetSeamFailsBeforeStart()
        {
            var process = new LegacyProcess();
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process));
            var context = new FakeContext(new AgentRunRequest("inspect"));
            runner.Start(context.Request, context);
            Assert.That(process.Started, Is.False);
            Assert.That(context.Error.Code, Is.EqualTo("PI_TURN_LIMIT_UNSUPPORTED"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_288.md
        [Test, Category("AGB_288")]
        public void KnownBudgetFailureSurvivesThrowingProcessCleanup()
        {
            var process = new FakeProcess { FailureCode = "PI_TURN_LIMIT_EXCEEDED", FailureMessage = "two requests",
                KillException = new InvalidOperationException("native process is gone"), FailReadAfterDispose = true };
            var context = new FakeContext(new AgentRunRequest("inspect"));
            var runner = new PiAgentRunner(new PiAgentRunnerOptions(), new FakeFactory(process));
            runner.Start(context.Request, context);
            runner.Advance(context);
            Assert.That(process.Disposed, Is.True);
            Assert.That(context.Error.Code, Is.EqualTo("PI_TURN_LIMIT_EXCEEDED"));
            Assert.That(context.Error.Message, Is.EqualTo("two requests"));
            Assert.That(context.Result.Success, Is.False);
        }

        private sealed class LegacyProcess : IPiProcess
        {
            public string WorkingDirectory { get; set; }
            public string Arguments { get; set; }
            public bool HasExited => false;
            public int ExitCode => 0;
            public string StandardOutput => "";
            public string StandardError => "";
            public bool Started { get; private set; }
            public void Start() { Started = true; }
            public void Kill() { }
        }

        private sealed class FakeFactory : IPiProcessFactory
        {
            private readonly IPiProcess process;
            public FakeFactory(IPiProcess process) { this.process = process; }
            public IPiProcess Create(string executablePath) => process;
        }

        private sealed class FakeProcess : IPiBudgetProcess, IDisposable
        {
            public bool GuardReady { get; set; } = true;
            private string failureCode;
            public string FailureCode
            {
                get { if (Disposed && FailReadAfterDispose) throw new ObjectDisposedException("process"); return failureCode; }
                set => failureCode = value;
            }
            public bool Disposed { get; private set; }
            public bool FailReadAfterDispose { get; set; }
            public Exception KillException { get; set; }
            public string FailureMessage { get; set; }
            public string ConfiguredTask { get; private set; }
            public int ConfiguredMaxTurns { get; private set; }
            public void ConfigureBudget(string task, int maxTurns, int maxOutputBytes)
            { ConfiguredTask = task; ConfiguredMaxTurns = maxTurns; }
            public string WorkingDirectory { get; set; }
            public string Arguments { get; set; }
            public bool HasExited { get; set; }
            public int ExitCode { get; set; }
            public string StandardOutput { get; set; } = string.Empty;
            public string StandardError { get; set; } = string.Empty;
            public bool Killed { get; private set; }
            public bool Started { get; private set; }
            public Exception StartException { get; set; }
            public void Start() { Started = true; if (StartException != null) throw StartException; }
            public void Kill() { Killed = true; HasExited = true; if (KillException != null) throw KillException; }
            public void Dispose() { Disposed = true; Kill(); }
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
