using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class PiBudgetProcessTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                Assert.Ignore("The Node launcher fixture exercises the Windows npm/JavaScript resolver.");
            directory = Path.Combine(Path.GetTempPath(), "PiBudgetFixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_275.md
        [Test, Category("AGB_275"), Timeout(30000)]
        public void RealProcessFixtureCompletesAtRequestCeilingAndDrainsPipes()
        {
            using (var process = (IDisposable)CreateFixture("success", 2))
            {
                var bounded = (IPiBudgetProcess)process;
                bounded.ConfigureBudget("inspect \"quoted\"\nrequest", 2, 1024);
                bounded.Start();
                Assert.That(SpinWait.SpinUntil(() => bounded.HasExited, 10000), Is.True);
                Assert.That(bounded.FailureCode, Is.Null, bounded.FailureMessage);
                Assert.That(bounded.StandardOutput, Is.EqualTo("final"));
                Assert.That(bounded.ExitCode, Is.EqualTo(0));
                Assert.That(File.ReadAllLines(Path.Combine(directory, "dispatches.txt")).Length, Is.EqualTo(2));
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_276.md
        [Test, Category("AGB_276"), Timeout(30000)]
        public void RealProcessGuardPreventsRequestThreeAndDiscardsPartialText()
        {
            using (var process = (IDisposable)CreateFixture("success", 3))
            {
                var bounded = (IPiBudgetProcess)process;
                bounded.ConfigureBudget("inspect", 2, 1024);
                bounded.Start();
                Assert.That(SpinWait.SpinUntil(() => bounded.HasExited, 10000), Is.True);
                Assert.That(bounded.FailureCode, Is.EqualTo("PI_TURN_LIMIT_EXCEEDED"));
                Assert.That(bounded.StandardOutput, Is.Empty);
                Assert.That(File.ReadAllLines(Path.Combine(directory, "dispatches.txt")).Length, Is.EqualTo(2));
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_277.md
        [Test, Category("AGB_277"), Timeout(30000)]
        public void UnsupportedVersionNeverStartsPromptProcess()
        {
            using (var process = (IDisposable)CreateFixture("unsupported", 1))
            {
                var bounded = (IPiBudgetProcess)process;
                bounded.ConfigureBudget("inspect", 2, 1024);
                var error = Assert.Throws<PiProcessException>(() => bounded.Start());
                Assert.That(error.Code, Is.EqualTo("PI_TURN_LIMIT_UNSUPPORTED"));
                Assert.That(File.Exists(Path.Combine(directory, "dispatches.txt")), Is.False);
            }
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_301.md
        [Test, Category("AGB_301"), Timeout(30000)]
        public void RealProcessRejectsForbiddenPathWithoutReturningContents()
        {
            using (var process = (IDisposable)CreateFixture("denied", 1))
            {
                var bounded = (IPiBudgetProcess)process;
                bounded.ConfigureBudget("inspect", 2, 1024);
                bounded.Start();
                Assert.That(SpinWait.SpinUntil(() => bounded.HasExited, 10000), Is.True);
                Assert.That(bounded.FailureCode, Is.EqualTo("PI_PATH_ACCESS_DENIED"));
                Assert.That(bounded.StandardOutput, Is.Empty);
                Assert.That(File.Exists(Path.Combine(directory, "dispatches.txt")), Is.False);
            }
        }

        private IPiProcess CreateFixture(string mode, int requests)
        {
            var path = Path.Combine(directory, "fixture.mjs");
            var source = @"
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { createInterface } from 'node:readline';
if (process.argv.includes('--version')) { console.log(VERSION); process.exit(0); }
const guard = process.argv[process.argv.indexOf('--extension') + 1];
const handlers = new Map();
(await import(pathToFileURL(guard).href)).default({on(name, handler) { handlers.set(name, handler); }, registerTool() {}, setActiveTools() {}});
handlers.get('session_start')();
const input = createInterface({input: process.stdin});
input.once('line', async line => {
    const command = JSON.parse(line);
    if (command.type !== 'prompt') process.exit(4);
    if (DENIED) { handlers.get('tool_call')({toolName:'read',input:{path:'Library/hidden.txt'}}); console.log('UNEXPECTED_READ'); }
    for (let i = 0; i < REQUESTS; i++) {
        try { await handlers.get('before_provider_request')({}); } catch { }
        appendFileSync('dispatches.txt', 'request\n');
        console.log(JSON.stringify({type: 'message_update', delta: 'partial'}));
    }
    console.log(JSON.stringify({type: 'agent_end', willRetry: false, messages: [
        {role: 'assistant', stopReason: 'stop', content: [{type: 'text', text: 'final'}]}]}));
});
input.on('close', () => process.exit(0));
";
            File.WriteAllText(path, source.Replace("VERSION", mode == "unsupported" ? "'0.0.0'" : "'0.85.1'")
                .Replace("REQUESTS", requests.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("DENIED", mode == "denied" ? "true" : "false"));
            var process = new PiProcessFactory().Create(path);
            process.WorkingDirectory = directory;
            ((IPiReadScopeProcess)process).ConfigureReadPaths(directory, PiReadPathScope.DefaultReadFolders, PiReadPathScope.DefaultDenyFolders, PiAgentRunnerSettings.DefaultTools);
            process.Arguments = "--tools read --no-extensions --no-skills --no-prompt-templates --no-session --mode rpc";
            return process;
        }
    }
}
