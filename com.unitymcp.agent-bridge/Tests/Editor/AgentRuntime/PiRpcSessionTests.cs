using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PiRpcSessionTests
    {
        private const string PolicyHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Nonce = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private static string Marker(string type, int requests = 0, string nonce = Nonce, int maxTurns = 2)
            => PiRpcSession.MarkerPrefix + new JObject { ["type"] = type, ["nonce"] = nonce,
                ["protocolVersion"] = 2, ["policyHash"] = PolicyHash, ["maxTurns"] = maxTurns, ["requests"] = requests }.ToString(Newtonsoft.Json.Formatting.None);
        private static string Terminal(string text, string stopReason = "stop", bool willRetry = false)
            => new JObject { ["type"] = "agent_end", ["willRetry"] = willRetry, ["messages"] = new JArray(
                new JObject { ["role"] = "assistant", ["stopReason"] = stopReason, ["content"] = new JArray(
                    new JObject { ["type"] = "thinking", ["thinking"] = "private" },
                    new JObject { ["type"] = "text", ["text"] = text }) }) }.ToString(Newtonsoft.Json.Formatting.None);

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_267.md
        [Test, Category("AGB_267")]
        public void ReadinessSubmitsExactlyOneJsonPrompt()
        {
            const string task = "inspect \"quotes\"\n$literal";
            var session = new PiRpcSession(Nonce, 2, 1024, task, PolicyHash);
            Assert.That(session.PromptSent, Is.False);
            Assert.That(session.AcceptGuardLine("ordinary diagnostic"), Is.Null);
            var prompt = JObject.Parse(session.AcceptGuardLine(Marker("ready")));
            Assert.That(prompt.Value<string>("type"), Is.EqualTo("prompt"));
            Assert.That(prompt.Value<string>("message"), Is.EqualTo(task));
            Assert.That(session.AcceptGuardLine(Marker("ready")), Is.Null);
            Assert.That(session.FailureCode, Is.EqualTo("PI_GUARD_INVALID"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_268.md
        [Test, Category("AGB_268")]
        public void WrongNonceOrBudgetNeverSubmitsPrompt()
        {
            foreach (var marker in new[] { Marker("ready", nonce: "other"), Marker("ready", maxTurns: 3) })
            {
                var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
                Assert.That(session.AcceptGuardLine(marker), Is.Null);
                Assert.That(session.PromptSent, Is.False);
                Assert.That(session.FailureCode, Is.EqualTo("PI_GUARD_INVALID"));
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_269.md
        [Test, Category("AGB_269")]
        public void TerminalProjectionExcludesPartialTextAndThinking()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine("{\"type\":\"message_update\",\"delta\":\"partial\"}");
            session.AcceptEventLine(Terminal("final"));
            session.EndOfStream(0);
            Assert.That(session.Completed, Is.True);
            Assert.That(session.FailureCode, Is.Null);
            Assert.That(session.Output, Is.EqualTo("final"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_270.md
        [Test, Category("AGB_270")]
        public void LimitMarkerClearsPartialOrTerminalOutput()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine(Terminal("partial"));
            session.AcceptGuardLine(Marker("limit", requests: 2));
            Assert.That(session.FailureCode, Is.EqualTo("PI_TURN_LIMIT_EXCEEDED"));
            Assert.That(session.Output, Is.Empty);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_271.md
        [Test, Category("AGB_271")]
        public void MalformedOrOversizedFramesFailClosed()
        {
            foreach (var line in new[] { "not json", new string('x', PiRpcSession.MaxFrameCharacters + 1) })
            {
                var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
                session.AcceptEventLine(line);
                Assert.That(session.FailureCode, Is.EqualTo("PI_RPC_PROTOCOL_INVALID"));
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_272.md
        [Test, Category("AGB_272")]
        public void RetryEventsDoNotCompleteAndModelErrorsFail()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine(Terminal("retry", "error", true));
            Assert.That(session.Completed, Is.False);
            session.AcceptEventLine(Terminal("error", "error"));
            Assert.That(session.FailureCode, Is.EqualTo("PI_MODEL_FAILED"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_292.md
        [Test, Category("AGB_292")]
        public void ProviderErrorDiagnosticIsBoundedRedactedAndExcludesAssistantContent()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            var terminal = JObject.Parse(Terminal("unpublished body", "error"));
            terminal["messages"][0]["errorMessage"] = "Provider unavailable Bearer secret-credential sk-example-secret eyJabc.abc.abc refresh_token=hidden " + new string('x', 2000);
            session.AcceptEventLine(terminal.ToString(Newtonsoft.Json.Formatting.None));
            Assert.That(session.FailureCode, Is.EqualTo("PI_MODEL_FAILED"));
            Assert.That(session.FailureMessage, Does.StartWith("Pi model failed: Provider unavailable"));
            Assert.That(session.FailureMessage.Length, Is.EqualTo(1024));
            Assert.That(session.FailureMessage, Does.Not.Contain("secret-credential"));
            Assert.That(session.FailureMessage, Does.Not.Contain("sk-example-secret"));
            Assert.That(session.FailureMessage, Does.Not.Contain("eyJabc.abc.abc"));
            Assert.That(session.FailureMessage, Does.Not.Contain("hidden"));
            Assert.That(session.FailureMessage, Does.Not.Contain("unpublished body"));
            Assert.That(session.Output, Is.Empty);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_273.md
        [Test, Category("AGB_273")]
        public void OutputCeilingPreservesUtf8CharacterBoundary()
        {
            var session = new PiRpcSession(Nonce, 2, 5, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine(Terminal("a😀b"));
            Assert.That(session.Output, Is.EqualTo("a😀"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_274.md
        [Test, Category("AGB_274")]
        public void ZeroExitWithoutReadinessOrTerminalResultFails()
        {
            var absent = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            absent.EndOfStream(0);
            Assert.That(absent.FailureCode, Is.EqualTo("PI_GUARD_NOT_READY"));
            var incomplete = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            incomplete.AcceptGuardLine(Marker("ready"));
            incomplete.EndOfStream(0);
            Assert.That(incomplete.FailureCode, Is.EqualTo("PI_RPC_PROTOCOL_INVALID"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_299.md
        [Test, Category("AGB_299")]
        public void WrongScopeHashOrLegacyMarkerNeverSubmitsPrompt()
        {
            foreach (var variant in new[] { "missing", "wrong", "legacy" })
            {
                var marker = JObject.Parse(Marker("ready").Substring(PiRpcSession.MarkerPrefix.Length));
                if (variant == "missing") marker.Remove("policyHash");
                if (variant == "wrong") marker["policyHash"] = new string('c', 64);
                if (variant == "legacy") marker["protocolVersion"] = 1;
                var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
                Assert.That(session.AcceptGuardLine(PiRpcSession.MarkerPrefix + marker.ToString()), Is.Null);
                Assert.That(session.FailureCode, Is.EqualTo("PI_GUARD_INVALID"));
                Assert.That(session.PromptSent, Is.False);
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_300.md
        [Test, Category("AGB_300")]
        public void PermissionMarkerDiscardsPreviouslyCompletedText()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine(Terminal("partial result"));
            session.AcceptGuardLine(Marker("path_denied", requests: 1));
            session.EndOfStream(80);
            Assert.That(session.FailureCode, Is.EqualTo("PI_PATH_ACCESS_DENIED"));
            Assert.That(session.Output, Is.Empty);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_278.md
        [Test, Category("AGB_278")]
        public void GuardExtensionErrorsCannotBecomeSuccessfulResults()
        {
            var session = new PiRpcSession(Nonce, 2, 1024, "inspect", PolicyHash);
            session.AcceptGuardLine(Marker("ready"));
            session.AcceptEventLine("{\"type\":\"extension_error\",\"event\":\"before_provider_request\"}");
            session.AcceptEventLine(Terminal("partial"));
            Assert.That(session.FailureCode, Is.EqualTo("PI_GUARD_INVALID"));
            Assert.That(session.Output, Is.Empty);
        }
    }
}
