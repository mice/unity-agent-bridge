using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpEnvironmentProbeTests
    {
        private string _tempDirectory;
        private string _originalPath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            AgentBridgeBootstrap.SetSuppressStartForTests(true);
            AgentBridgeBootstrap.Reconfigure();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            AgentBridgeBootstrap.SetSuppressStartForTests(false);
        }

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_029.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_029")]
        public void SnapshotAsync_ResolvesDotnetOnly()
        {
            CreateExecutable("dotnet.exe");

            var runner = new FakeAsyncProcessRunner(new Dictionary<string, string>
            {
                { "dotnet.exe", "10.0.100" },
            });
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), runner);

            var snapshot = probe.SnapshotAsync(new McpEditorSettings(), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(snapshot.Dotnet.IsAvailable, Is.True);
            Assert.That(runner.Requests.Count, Is.EqualTo(1));
            Assert.That(runner.Requests[0].Arguments, Is.EqualTo(new[] { "--version" }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_030.md
        [Test]
        [Timeout(5000)]
        [Category("AGBM_Discovery")]
        [Category("AGBM_030")]
        public void SnapshotAsync_UnknownVersion_MarksToolUnavailable()
        {
            CreateExecutable("dotnet.exe");
            var runner = new FakeAsyncProcessRunner(new Dictionary<string, string>
            {
                { "dotnet.exe", "unknown" },
            });
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), runner);

            var snapshot = probe.SnapshotAsync(new McpEditorSettings(), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(snapshot.Dotnet.ResolvedPath, Is.Not.Empty);
            Assert.That(snapshot.Dotnet.VersionText, Is.EqualTo("unknown"));
            Assert.That(snapshot.Dotnet.IsAvailable, Is.False);
        }

        private void CreateExecutable(string fileName)
        {
            File.WriteAllText(Path.Combine(_tempDirectory, fileName), "stub");
        }

        private sealed class FakeAsyncProcessRunner : IAsyncProcessRunner
        {
            private readonly Dictionary<string, string> _responses;

            public FakeAsyncProcessRunner(Dictionary<string, string> responses)
            {
                _responses = responses;
            }

            public List<ProcessExecutionRequest> Requests { get; } = new List<ProcessExecutionRequest>();

            public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                var fileName = Path.GetFileName(request.FilePath);
                _responses.TryGetValue(fileName, out var versionText);

                return Task.FromResult(new ProcessExecutionResult
                {
                    Outcome = ProcessOutcome.Completed,
                    ExitCode = 0,
                    Stdout = versionText ?? string.Empty,
                    Stderr = string.Empty,
                });
            }
        }
    }
}
