using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using System.Reflection;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpDiagnosticsTests
    {
        private string _tempDirectory;
        private string _originalPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge.P4", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _originalPath = Environment.GetEnvironmentVariable("PATH");
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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_091.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_091")]
        public void Aggregate_ExecutableRuntimeError_ReturnsUnavailable()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Error),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Unavailable));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_092.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_092")]
        public void Aggregate_BridgeDisabled_ReturnsDegraded()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Error),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_093.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_093")]
        public void Aggregate_NormalEnvironment_ReturnsReady()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP002", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP004", McpDiagnosticSeverity.Info),
                CreateCheck("MCP005", McpDiagnosticSeverity.Info),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
                CreateCheck("MCP009", McpDiagnosticSeverity.Info),
                CreateCheck("MCP010", McpDiagnosticSeverity.Info),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Ready));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_094.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_094")]
        public void Aggregate_RuntimeBindingError_ReturnsDegraded()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP004", McpDiagnosticSeverity.Error),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_096.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_096")]
        public void Format_RedactsSensitiveValues()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new[]
            {
                new McpDiagnosticCheck
                {
                    Code = "MCP009",
                    Severity = McpDiagnosticSeverity.Error,
                    Summary = "MCP Tool List",
                    Details = "authorization=Bearer abc123",
                    Remediation = "Repair",
                    Duration = TimeSpan.FromMilliseconds(12),
                },
            }, McpReadiness.Degraded, new McpEditorSettings());

            Assert.That(report, Does.Contain("Readiness: Degraded"));
            Assert.That(report, Does.Contain("UnityVersion:"));
            Assert.That(report, Does.Contain("ProjectPath:"));
            Assert.That(report, Does.Contain("McpServerVersion:"));
            Assert.That(report, Does.Contain("MCP009 [Error]"));
            Assert.That(report, Does.Contain("[redacted]"));
            Assert.That(report, Does.Not.Contain("abc123"));
            Assert.That(report, Does.Not.Contain("Bearer"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_097.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_097")]
        public void RunAsync_ProbeFailure_MarksMcpChecksAsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner());
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Failed,
                            Stderr = "probe failed",
                            Duration = TimeSpan.FromMilliseconds(23),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            });

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP009").Duration, Is.EqualTo(TimeSpan.FromMilliseconds(23)));
            Assert.That(Find(results, "MCP010").Duration, Is.EqualTo(TimeSpan.FromMilliseconds(23)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_098.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_098")]
        public void RunAsync_ProbeSuccess_MarksToolListAndPingAsInfo()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
                DotnetPath = Path.Combine(root, "dotnet.exe"),
            };
            File.WriteAllText(settings.DotnetPath, string.Empty);

            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(34),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP009").Details, Is.EqualTo("17 frozen MCP tools listed"));
            Assert.That(Find(results, "MCP010").Details, Is.EqualTo("status=success"));
        }

        [Test]
        [Category("AGBM_P4")]
        public void RunAsync_PreparedExecutableRuntimeWithoutPreparedPackageJson_MarksMcp004AndMcp006AsInfo()
        {
            var runtimeRoot = Path.Combine(_tempDirectory, "UnityProject", ".unitymcp", "runtime");
            var mcpRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            var cliRoot = Path.Combine(mcpRoot, "cli", "out", "win-x64");
            var roslynRoot = Path.Combine(mcpRoot, "roslyn-execution", "out", "win-x64");
            var launcherRoot = Path.Combine(runtimeRoot, "AgentBridge");
            Directory.CreateDirectory(cliRoot);
            Directory.CreateDirectory(roslynRoot);
            Directory.CreateDirectory(launcherRoot);
            File.WriteAllText(Path.Combine(cliRoot, "unity-agent-bridge.exe"), string.Empty);
            File.WriteAllText(Path.Combine(roslynRoot, "unity-roslyn-compiler.exe"), string.Empty);
            File.WriteAllText(Path.Combine(launcherRoot, "Start-UnityAgentBridge-Mcp.cmd"), string.Empty);

            var resolver = new McpPathResolver(() => Path.Combine(_tempDirectory, "UnityProject"));
            var settings = new McpEditorSettings
            {
                ToolsRoot = Path.Combine(_tempDirectory, "missing-tools"),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(11),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(resolver, new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, resolver, fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP004").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP006").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP011").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP012").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
        }

        [Test]
        [Category("AGBM_P4")]
        public void RunAsync_BundledServerPresent_StillRunsProbe()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            Directory.CreateDirectory(Path.Combine(root, "roslyn-execution", "out", "win-x64"));
            File.WriteAllText(Path.Combine(root, "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe"), string.Empty);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var observedProbeRun = false;
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        observedProbeRun = true;
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            ExitCode = 0,
                            Stdout = HealthyProbeStdout,
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var resolver = new McpPathResolver(() => Path.Combine(_tempDirectory, "UnityProject"));
            var probe = new McpEnvironmentProbe(resolver, new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, resolver, fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(observedProbeRun, Is.True);
            Assert.That(Find(results, "MCP007").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP011").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP012").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
        }

        [Test]
        [Category("AGBM_P4")]
        public void RunAsync_PreparedRuntimeMissingRoslynPayload_MarksMcp012AsError()
        {
            var runtimeRoot = Path.Combine(_tempDirectory, "UnityProject", ".unitymcp", "runtime");
            var mcpRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            var cliRoot = Path.Combine(mcpRoot, "cli", "out", "win-x64");
            var launcherRoot = Path.Combine(runtimeRoot, "AgentBridge");
            Directory.CreateDirectory(cliRoot);
            Directory.CreateDirectory(launcherRoot);
            File.WriteAllText(Path.Combine(cliRoot, "unity-agent-bridge.exe"), string.Empty);
            File.WriteAllText(Path.Combine(launcherRoot, "Start-UnityAgentBridge-Mcp.cmd"), string.Empty);

            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            var toolsRoslynRoot = Path.Combine(toolsRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64");
            Directory.CreateDirectory(toolsRoslynRoot);
            File.WriteAllText(Path.Combine(toolsRoslynRoot, "unity-roslyn-compiler.exe"), string.Empty);

            var resolver = new McpPathResolver(() => Path.Combine(_tempDirectory, "UnityProject"));
            var settings = new McpEditorSettings
            {
                ToolsRoot = toolsRoot,
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(11),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(resolver, new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, resolver, fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP011").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP012").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP012").Details, Does.Contain("unity-roslyn-compiler.exe"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_099.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_099")]
        public void RunAsync_ProbeMalformedJson_MarksToolListAndPingAsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = "{\"listedToolCount\":17}",
                            Duration = TimeSpan.FromMilliseconds(15),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP009").Duration, Is.EqualTo(TimeSpan.FromMilliseconds(15)));
            Assert.That(Find(results, "MCP010").Duration, Is.EqualTo(TimeSpan.FromMilliseconds(15)));
            Assert.That(Find(results, "MCP009").Details, Does.Contain("stdout="));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_100.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_100")]
        public void Aggregate_QueueError_ReturnsDegraded()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP002", McpDiagnosticSeverity.Error),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_101.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_101")]
        public void Aggregate_DotnetError_DoesNotBlockPublishedRuntime()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP005", McpDiagnosticSeverity.Error),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Ready));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_103.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_103")]
        public void Format_IncludesDurationAndRemediation()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new[]
            {
                new McpDiagnosticCheck
                {
                    Code = "MCP006",
                    Severity = McpDiagnosticSeverity.Error,
                    Summary = "Server Files",
                    Details = "Missing",
                    Remediation = "Repair the MCP server root.",
                    Duration = TimeSpan.FromMilliseconds(45),
                },
            }, McpReadiness.Degraded, new McpEditorSettings());

            Assert.That(report, Does.Contain("MCP006 [Error] Server Files (45ms)"));
            Assert.That(report, Does.Contain("Remediation: Repair the MCP server root."));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_104.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_104")]
        public void Constructor_DefaultRunner_WiresEnvironmentProbeProcessRunner()
        {
            var runner = new McpDiagnosticsRunner();
            var environmentProbeField = typeof(McpDiagnosticsRunner).GetField("_environmentProbe", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(environmentProbeField, Is.Not.Null);

            var environmentProbe = environmentProbeField.GetValue(runner);
            Assert.That(environmentProbe, Is.Not.Null);

            var processRunnerField = typeof(McpEnvironmentProbe).GetField("_processRunner", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(processRunnerField, Is.Not.Null);
            Assert.That(processRunnerField.GetValue(environmentProbe), Is.Not.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_105.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_105")]
        public void RunAsync_MissingServerFiles_MarksMcp006AsError()
        {
            var root = Path.Combine(_tempDirectory, "unity-agent-bridge-runtime");
            Directory.CreateDirectory(root);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
            };

            var runner = new McpDiagnosticsRunner(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") }),
                new McpPathResolver(),
                new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") });

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP006").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_106.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_106")]
        public void RunAsync_MissingDependencies_MarksMcp007AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: false, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = Path.Combine(root, "missing-cli.exe"),
            };

            var runner = new McpDiagnosticsRunner(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") }),
                new McpPathResolver(),
                new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") });

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP007").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_107.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_107")]
        public void RunAsync_ExplicitCliPathMissing_MarksMcp008AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = Path.Combine(root, "missing-cli.exe"),
            };

            var runner = new McpDiagnosticsRunner(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") }),
                new McpPathResolver(),
                new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") });

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP008").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_109.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_109")]
        public void RunAsync_DefaultCliSourcePresent_MarksMcp008AsInfo()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(20),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP008").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_111.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_111")]
        public void Format_PreservesPathsWhileRedactingSecrets()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new[]
            {
                new McpDiagnosticCheck
                {
                    Code = "MCP010",
                    Severity = McpDiagnosticSeverity.Error,
                    Summary = "MCP Ping",
                    Details = "C:\\Tools\\codex.cmd",
                    Remediation = "Set token=secret elsewhere",
                    Duration = TimeSpan.FromMilliseconds(5),
                },
            }, McpReadiness.Degraded, new McpEditorSettings());

            Assert.That(report, Does.Contain("C:\\Tools\\codex.cmd"));
            Assert.That(report, Does.Contain("[redacted]"));
            Assert.That(report, Does.Not.Contain("secret"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_112.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_112")]
        public void Aggregate_ToolListError_ReturnsDegraded()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP002", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP004", McpDiagnosticSeverity.Info),
                CreateCheck("MCP005", McpDiagnosticSeverity.Info),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
                CreateCheck("MCP009", McpDiagnosticSeverity.Error),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_113.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_113")]
        public void Aggregate_PingError_ReturnsDegraded()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new[]
            {
                CreateCheck("MCP001", McpDiagnosticSeverity.Info),
                CreateCheck("MCP002", McpDiagnosticSeverity.Info),
                CreateCheck("MCP003", McpDiagnosticSeverity.Info),
                CreateCheck("MCP004", McpDiagnosticSeverity.Info),
                CreateCheck("MCP005", McpDiagnosticSeverity.Info),
                CreateCheck("MCP006", McpDiagnosticSeverity.Info),
                CreateCheck("MCP007", McpDiagnosticSeverity.Info),
                CreateCheck("MCP008", McpDiagnosticSeverity.Info),
                CreateCheck("MCP010", McpDiagnosticSeverity.Error),
            }, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.Degraded));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_114.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_114")]
        public void Aggregate_NoChecks_ReturnsNotChecked()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(new McpDiagnosticCheck[0], new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.NotChecked));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_115.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_115")]
        public void RunAsync_ProbeReturnsPingError_MarksMcp010AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = PingErrorProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(17),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_116.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_116")]
        public void RunAsync_ProbeMissingToolName_MarksMcp009AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = "{\"listedToolCount\":12,\"toolNames\":[\"mcp__unity__ping\",\"mcp__unity__project_get_info\",\"mcp__unity__compile\",\"mcp__unity__get_console\",\"mcp__unity__assetdatabase_search\",\"mcp__unity__get_selection_info\",\"mcp__unity__get_gameobject_component_info\",\"mcp__unity__read_report\",\"mcp__unity__run_static_method\",\"mcp__unity__run_diagnostic\",\"mcp__unity__run_editmode_tests\",\"mcp__unity__agent_bridge_self_test\"],\"pingResult\":{\"isError\":false,\"structuredContent\":{\"status\":\"success\"}}}",
                            Duration = TimeSpan.FromMilliseconds(16),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_117.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_117")]
        public void Format_NullSettings_OmitsPreferredClient()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new McpDiagnosticCheck[0], McpReadiness.NotChecked, null);

            Assert.That(report, Does.Not.Contain("PreferredClient:"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_118.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_118")]
        public void RunAsync_EnvironmentProbeThrows_StillReturnsLaterChecks()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "--version")
                    {
                        throw new InvalidOperationException("version boom");
                    }

                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(20),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP005").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_119.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_119")]
        public void RunAsync_ProbeRunnerThrows_StillReturnsErrorChecks()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        throw new InvalidOperationException("probe boom");
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP009").Details, Is.EqualTo("probe boom"));
            Assert.That(Find(results, "MCP010").Details, Is.EqualTo("probe boom"));
            Assert.That(Find(results, "MCP009").Duration, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Find(results, "MCP010").Duration, Is.EqualTo(TimeSpan.Zero));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_120.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_120")]
        public void Format_EmptyChecks_StillIncludesHeaderFields()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new McpDiagnosticCheck[0], McpReadiness.NotChecked, new McpEditorSettings());

            Assert.That(report, Does.Contain("MCP Diagnostics Report"));
            Assert.That(report, Does.Contain("GeneratedAtUtc:"));
            Assert.That(report, Does.Contain("Readiness: NotChecked"));
            Assert.That(report, Does.Contain("Checks:"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_121.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_121")]
        public void Aggregate_NullChecks_ReturnsNotChecked()
        {
            var aggregator = new McpReadinessAggregator();

            var readiness = aggregator.Aggregate(null, new McpEditorSettings());

            Assert.That(readiness, Is.EqualTo(McpReadiness.NotChecked));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_122.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_122")]
        public void RunAsync_ExecutableRuntimeMissing_MarksMcp003AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: false, includeProbe: false);

            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = Path.Combine(root, "missing-unity-agent-bridge.exe"),
            };

            var runner = new McpDiagnosticsRunner(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") }),
                new McpPathResolver(),
                new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") });

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP003").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_123.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_123")]
        public void RunAsync_RuntimeBindingMissing_MarksMcp004AsError()
        {
            var root = Path.Combine(_tempDirectory, "runtime-binding-missing");
            Directory.CreateDirectory(root);

            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
            };

            var fakeRunner = new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") };
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP004").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_124.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_124")]
        public void RunAsync_DotnetMissing_MarksSourceDiagnosticMcp005AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var toolDirectory = Path.Combine(_tempDirectory, "tools-dotnet-missing");
            Directory.CreateDirectory(toolDirectory);
            Environment.SetEnvironmentVariable("PATH", toolDirectory);

            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
            };

            var fakeRunner = new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") };
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP005").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_126.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_126")]
        public void RunAsync_ProbeFileMissing_MarksMcp009AndMcp010AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: false);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = Path.Combine(root, "missing-cli.exe"),
            };
            var fakeRunner = new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") };
            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_127.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_127")]
        public void RunAsync_ProbeTimedOut_MarksMcp009AndMcp010AsError()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.TimedOut,
                            Stderr = "probe timeout",
                            Duration = TimeSpan.FromMilliseconds(30000),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_128.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_128")]
        public void RunAsync_ProbeUsesConfiguredDiagnosticTimeout()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
                DiagnosticTimeoutMs = 4321,
            };
            ProcessExecutionRequest observedRequest = null;
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        observedRequest = request;
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(12),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(observedRequest, Is.Not.Null);
            Assert.That((int)observedRequest.Timeout.TotalMilliseconds, Is.EqualTo(4321));
            Assert.That(observedRequest.CancellationMode, Is.EqualTo(ProcessCancellationMode.TerminateOnCancel));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_135.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_135")]
        public void RunAsync_ProbeSetsCurrentUnityProjectPathEnvironment()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            ProcessExecutionRequest observedRequest = null;
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        observedRequest = request;
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = HealthyProbeStdout,
                            Duration = TimeSpan.FromMilliseconds(12),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(observedRequest, Is.Not.Null);
            Assert.That(observedRequest.Environment, Is.Not.Null);
            Assert.That(observedRequest.Environment.ContainsKey("UNITY_AGENT_BRIDGE_PROJECT_PATH"), Is.True);
            Assert.That(observedRequest.Environment["UNITY_AGENT_BRIDGE_PROJECT_PATH"], Is.Not.Null.And.Not.Empty);
            Assert.That(Directory.Exists(observedRequest.Environment["UNITY_AGENT_BRIDGE_PROJECT_PATH"]), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_136.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_136")]
        public void RunAsync_ProbeSuccess_WithStructuredContentPayload_MarksToolListAndPingAsInfo()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = "{\"listedToolCount\":17,\"toolNames\":[\"mcp_echo\",\"unity_bridge_health\",\"unity_bridge_submit_only\",\"unity_bridge_wait_result\",\"mcp__unity__ping\",\"mcp__unity__project_get_info\",\"mcp__unity__compile\",\"mcp__unity__get_console\",\"mcp__unity__assetdatabase_search\",\"mcp__unity__get_selection_info\",\"mcp__unity__get_gameobject_component_info\",\"mcp__unity__read_report\",\"mcp__unity__run_static_method\",\"mcp__unity__run_diagnostic\",\"mcp__unity__run_editmode_tests\",\"mcp__unity__run_playmode_tests\",\"mcp__unity__agent_bridge_self_test\"],\"pingResult\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"status\\\":\\\"success\\\"}\"}],\"structuredContent\":{\"status\":\"success\"},\"isError\":false}}",
                            Duration = TimeSpan.FromMilliseconds(17),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(Find(results, "MCP009").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
            Assert.That(Find(results, "MCP010").Severity, Is.EqualTo(McpDiagnosticSeverity.Info));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_180.md
        [Test]
        [Category("AGBM_180")]
        [Category("AGBM_P4")]
        public void RunAsync_ProbePingBlocked_IncludesLayeredHealthDetails()
        {
            var root = CreateMcpServerRoot(includeDependencies: true, includeProbe: true);
            var settings = new McpEditorSettings
            {
                McpServerRoot = root,
                CliExecutablePath = GetCliExecutablePath(root),
            };
            var fakeRunner = new FakeRunner
            {
                Handler = request =>
                {
                    if (request.Arguments.Count > 0 && request.Arguments[0] == "mcp-probe")
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            Stdout = "{\"listedToolCount\":17,\"toolNames\":[\"mcp_echo\",\"unity_bridge_health\",\"unity_bridge_submit_only\",\"unity_bridge_wait_result\",\"mcp__unity__ping\",\"mcp__unity__project_get_info\",\"mcp__unity__compile\",\"mcp__unity__get_console\",\"mcp__unity__assetdatabase_search\",\"mcp__unity__get_selection_info\",\"mcp__unity__get_gameobject_component_info\",\"mcp__unity__read_report\",\"mcp__unity__run_static_method\",\"mcp__unity__run_diagnostic\",\"mcp__unity__run_editmode_tests\",\"mcp__unity__run_playmode_tests\",\"mcp__unity__agent_bridge_self_test\"],\"pingResult\":{\"isError\":true,\"structuredContent\":{\"status\":\"blocked\"}},\"healthResult\":{\"isError\":false,\"structuredContent\":{\"status\":\"success\",\"lifecycleState\":\"degraded\",\"healthReason\":\"ProjectMismatch\",\"recommendedActionCode\":\"UpdateConfig\",\"recommendedAction\":\"Update the configured Unity project binding.\",\"toolExecution\":\"BlockedBeforeDispatch\"}}}",
                            Duration = TimeSpan.FromMilliseconds(19),
                        };
                    }

                    return VersionSuccess("v22.0.0");
                },
            };

            var probe = new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), fakeRunner);
            var runner = new McpDiagnosticsRunner(probe, new McpPathResolver(), fakeRunner);

            var results = runner.RunAsync(settings, CancellationToken.None).GetAwaiter().GetResult();

            var ping = Find(results, "MCP010");
            Assert.That(ping.Severity, Is.EqualTo(McpDiagnosticSeverity.Error));
            Assert.That(ping.Details, Does.Contain("lifecycleState=degraded"));
            Assert.That(ping.Details, Does.Contain("healthReason=ProjectMismatch"));
            Assert.That(ping.Details, Does.Contain("recommendedActionCode=UpdateConfig"));
            Assert.That(ping.Details, Does.Contain("toolExecution=BlockedBeforeDispatch"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_129.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_129")]
        public void Format_RedactsApiKeyInDetails()
        {
            var formatter = new McpReportFormatter();

            var report = formatter.Format(new[]
            {
                new McpDiagnosticCheck
                {
                    Code = "MCP004",
                    Severity = McpDiagnosticSeverity.Error,
                    Summary = "npm",
                    Details = "api_key=abc",
                    Remediation = "repair",
                    Duration = TimeSpan.FromMilliseconds(2),
                },
            }, McpReadiness.Degraded, new McpEditorSettings());

            Assert.That(report, Does.Contain("[redacted]"));
            Assert.That(report, Does.Not.Contain("abc"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_130.md
        [Test]
        [Category("AGBM_P4")]
        [Category("AGBM_130")]
        public void RunAsync_NullSettings_ThrowsArgumentNullException()
        {
            var runner = new McpDiagnosticsRunner(
                new McpEnvironmentProbe(new McpPathResolver(), new ToolVersionParser(), new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") }),
                new McpPathResolver(),
                new FakeRunner { Handler = _ => VersionSuccess("v22.0.0") });

            Assert.Throws<ArgumentNullException>(() => runner.RunAsync(null, CancellationToken.None).GetAwaiter().GetResult());
        }

        private string CreateMcpServerRoot(bool includeDependencies, bool includeProbe)
        {
            var root = Path.Combine(_tempDirectory, "UnityAgentBridge");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "cli"));
            if (includeProbe)
            {
                File.WriteAllText(GetCliExecutablePath(root), string.Empty);
            }

            if (includeDependencies)
            {
                File.WriteAllText(GetCliExecutablePath(root), string.Empty);
            }

            return root;
        }

        private static string GetCliExecutablePath(string root)
        {
            return Path.Combine(root, "unity-agent-bridge.exe");
        }

        private static McpDiagnosticCheck CreateCheck(string code, McpDiagnosticSeverity severity)
        {
            return new McpDiagnosticCheck
            {
                Code = code,
                Severity = severity,
                Summary = code,
                Details = "details",
                Remediation = "remediation",
                Duration = TimeSpan.Zero,
            };
        }

        private static McpDiagnosticCheck Find(IReadOnlyList<McpDiagnosticCheck> checks, string code)
        {
            for (var index = 0; index < checks.Count; index++)
            {
                if (checks[index].Code == code)
                {
                    return checks[index];
                }
            }

            Assert.Fail("Missing check: " + code);
            return null;
        }

        private static ProcessExecutionResult VersionSuccess(string version)
        {
            return new ProcessExecutionResult
            {
                Outcome = ProcessOutcome.Completed,
                Stdout = version,
                Duration = TimeSpan.FromMilliseconds(10),
            };
        }

        private const string HealthyProbeStdout = "{\"listedToolCount\":17,\"toolNames\":[\"mcp_echo\",\"unity_bridge_health\",\"unity_bridge_submit_only\",\"unity_bridge_wait_result\",\"mcp__unity__ping\",\"mcp__unity__project_get_info\",\"mcp__unity__compile\",\"mcp__unity__get_console\",\"mcp__unity__assetdatabase_search\",\"mcp__unity__get_selection_info\",\"mcp__unity__get_gameobject_component_info\",\"mcp__unity__read_report\",\"mcp__unity__run_static_method\",\"mcp__unity__run_diagnostic\",\"mcp__unity__run_editmode_tests\",\"mcp__unity__run_playmode_tests\",\"mcp__unity__agent_bridge_self_test\"],\"pingResult\":{\"isError\":false,\"structuredContent\":{\"status\":\"success\"}}}";
        private const string PingErrorProbeStdout = "{\"listedToolCount\":17,\"toolNames\":[\"mcp_echo\",\"unity_bridge_health\",\"unity_bridge_submit_only\",\"unity_bridge_wait_result\",\"mcp__unity__ping\",\"mcp__unity__project_get_info\",\"mcp__unity__compile\",\"mcp__unity__get_console\",\"mcp__unity__assetdatabase_search\",\"mcp__unity__get_selection_info\",\"mcp__unity__get_gameobject_component_info\",\"mcp__unity__read_report\",\"mcp__unity__run_static_method\",\"mcp__unity__run_diagnostic\",\"mcp__unity__run_editmode_tests\",\"mcp__unity__run_playmode_tests\",\"mcp__unity__agent_bridge_self_test\"],\"pingResult\":{\"isError\":true,\"structuredContent\":{\"status\":\"failed\"}}}";

        private sealed class FakeRunner : IAsyncProcessRunner
        {
            public Func<ProcessExecutionRequest, ProcessExecutionResult> Handler { get; set; }

            public System.Threading.Tasks.Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
            {
                if (Handler == null)
                {
                    return System.Threading.Tasks.Task.FromResult(new ProcessExecutionResult
                    {
                        Outcome = ProcessOutcome.Failed,
                        Stderr = "No handler configured.",
                    });
                }

                return System.Threading.Tasks.Task.FromResult(Handler(request));
            }
        }
    }
}
