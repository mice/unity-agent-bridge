using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpRuntimeBuilderTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge.RuntimeBuild", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        [Category("AGBM_P4")]
        public void DotnetSdkProbe_Parse_FindsNet8SdkFromListSdks()
        {
            var result = DotnetSdkProbeResult.Parse(
                "6.0.416 [C:\\Program Files\\dotnet\\sdk]\n8.0.100 [C:\\Program Files\\dotnet\\sdk]\n10.0.301 [C:\\Program Files\\dotnet\\sdk]",
                string.Empty);

            Assert.That(result.HasNet8Sdk, Is.True);
            Assert.That(result.Net8SdkVersion, Is.EqualTo("8.0.100"));
        }

        [Test]
        [Category("AGBM_P4")]
        public void DotnetSdkProbe_Parse_RejectsMissingNet8Sdk()
        {
            var result = DotnetSdkProbeResult.Parse(
                "6.0.416 [C:\\Program Files\\dotnet\\sdk]\n7.0.305 [C:\\Program Files\\dotnet\\sdk]\n10.0.301 [C:\\Program Files\\dotnet\\sdk]",
                string.Empty);

            Assert.That(result.HasNet8Sdk, Is.False);
        }

        [Test]
        [Category("AGBM_P4")]
        public void BuildAsync_MissingNet8Sdk_FailsBeforeBuildScript()
        {
            var projectRoot = CreateProjectRoot();
            var toolsRoot = CreateToolsRoot(includeBuildInputs: true);
            var runner = new FakeProcessRunner
            {
                Handler = request => new ProcessExecutionResult
                {
                    Outcome = ProcessOutcome.Completed,
                    ExitCode = 0,
                    Stdout = "10.0.301 [C:\\Program Files\\dotnet\\sdk]",
                },
            };
            var builder = new McpRuntimeBuilder(runner, new McpPathResolver(() => projectRoot), TimeSpan.FromSeconds(5));

            var result = builder.BuildAsync(new McpEditorSettings { ToolsRoot = toolsRoot }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("dotnet_sdk_missing"));
            Assert.That(runner.Requests.Count, Is.EqualTo(1));
            Assert.That(runner.Requests[0].FilePath, Is.EqualTo("dotnet"));
            Assert.That(runner.Requests[0].Arguments, Is.EquivalentTo(new[] { "--list-sdks" }));
        }

        [Test]
        [Category("AGBM_P4")]
        public void BuildAsync_MissingBuildInput_FailsBeforePublish()
        {
            var projectRoot = CreateProjectRoot();
            var toolsRoot = CreateToolsRoot(includeBuildInputs: false);
            var runner = new FakeProcessRunner
            {
                Handler = request => new ProcessExecutionResult
                {
                    Outcome = ProcessOutcome.Completed,
                    ExitCode = 0,
                    Stdout = "8.0.100 [C:\\Program Files\\dotnet\\sdk]",
                },
            };
            var builder = new McpRuntimeBuilder(runner, new McpPathResolver(() => projectRoot), TimeSpan.FromSeconds(5));

            var result = builder.BuildAsync(new McpEditorSettings { ToolsRoot = toolsRoot }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("cli_build_input_missing"));
            Assert.That(runner.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        [Category("AGBM_P4")]
        public void BuildAsync_PublishFailure_ReportsExitCodeAndLogPath()
        {
            var projectRoot = CreateProjectRoot();
            var toolsRoot = CreateToolsRoot(includeBuildInputs: true);
            var runner = new FakeProcessRunner
            {
                Handler = request =>
                {
                    if (IsSdkProbe(request))
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            ExitCode = 0,
                            Stdout = "8.0.100 [C:\\Program Files\\dotnet\\sdk]",
                        };
                    }

                    return new ProcessExecutionResult
                    {
                        Outcome = ProcessOutcome.Completed,
                        ExitCode = 42,
                        Stderr = "publish failed",
                    };
                },
            };
            var builder = new McpRuntimeBuilder(runner, new McpPathResolver(() => projectRoot), TimeSpan.FromSeconds(5));

            var result = builder.BuildAsync(new McpEditorSettings { ToolsRoot = toolsRoot }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("runtime_build_failed"));
            Assert.That(result.ExitCode, Is.EqualTo(42));
            Assert.That(File.Exists(result.LogPath), Is.True);
            Assert.That(File.ReadAllText(result.LogPath), Does.Contain("publish failed"));
        }

        [Test]
        [Category("AGBM_P4")]
        public void BuildAsync_Success_WritesProjectLocalRuntimeExecutables()
        {
            var projectRoot = CreateProjectRoot();
            var toolsRoot = CreateToolsRoot(includeBuildInputs: true);
            var runner = new FakeProcessRunner
            {
                Handler = request =>
                {
                    if (IsSdkProbe(request))
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            ExitCode = 0,
                            Stdout = "8.0.100 [C:\\Program Files\\dotnet\\sdk]",
                        };
                    }

                    var outputRoot = GetArgumentValue(request.Arguments, "-OutputRoot");
                    CreateGeneratedRuntimeExecutables(outputRoot);
                    return new ProcessExecutionResult
                    {
                        Outcome = ProcessOutcome.Completed,
                        ExitCode = 0,
                        Stdout = "built",
                    };
                },
            };
            var builder = new McpRuntimeBuilder(runner, new McpPathResolver(() => projectRoot), TimeSpan.FromSeconds(5));

            var result = builder.BuildAsync(new McpEditorSettings { ToolsRoot = toolsRoot }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.DotnetSdkVersion, Is.EqualTo("8.0.100"));
            Assert.That(result.RuntimeRoot, Is.EqualTo(Path.Combine(projectRoot, ".unitymcp", "runtime")));
            Assert.That(File.Exists(result.CliExecutablePath), Is.True);
            Assert.That(File.Exists(result.RoslynExecutablePath), Is.True);
            Assert.That(result.CliExecutablePath, Does.Contain(Path.Combine(".unitymcp", "runtime")));
            Assert.That(result.CliExecutablePath, Does.Not.Contain("PackageCache"));
            Assert.That(runner.Requests[1].FilePath, Is.EqualTo("pwsh"));
            Assert.That(runner.Requests[1].Arguments, Does.Contain("-OutputRoot"));
        }

        [Test]
        [Category("AGBM_P4")]
        public void BuildAsync_LegacyRepositoryToolsRoot_UsesResolvedPackageToolsRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "UnityProject");
            var legacyToolsRoot = Path.Combine(workspaceRoot, "Tools");
            var expectedToolsRoot = McpPathResolver.TryResolvePackageToolsRoot();
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(legacyToolsRoot);
            Assert.That(expectedToolsRoot, Is.Not.Empty);
            var runner = new FakeProcessRunner
            {
                Handler = request =>
                {
                    if (IsSdkProbe(request))
                    {
                        return new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            ExitCode = 0,
                            Stdout = "8.0.100 [C:\\Program Files\\dotnet\\sdk]",
                        };
                    }

                    var outputRoot = GetArgumentValue(request.Arguments, "-OutputRoot");
                    CreateGeneratedRuntimeExecutables(outputRoot);
                    return new ProcessExecutionResult
                    {
                        Outcome = ProcessOutcome.Completed,
                        ExitCode = 0,
                    };
                },
            };
            var builder = new McpRuntimeBuilder(runner, new McpPathResolver(() => projectRoot), TimeSpan.FromSeconds(5));

            var result = builder.BuildAsync(new McpEditorSettings { ToolsRoot = legacyToolsRoot }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(runner.Requests[1].Arguments, Does.Contain(Path.Combine(expectedToolsRoot, "UnityAgentBridge", "runtime-build", "Build-LocalRuntime.ps1")));
            Assert.That(runner.Requests[1].WorkingDirectory, Is.EqualTo(Path.Combine(expectedToolsRoot, "UnityAgentBridge", "runtime-build")));
            Assert.That(runner.Requests[1].Arguments, Does.Not.Contain(Path.Combine(legacyToolsRoot, "UnityAgentBridge", "runtime-build", "Build-LocalRuntime.ps1")));
        }

        private string CreateProjectRoot()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        private string CreateToolsRoot(bool includeBuildInputs)
        {
            var toolsRoot = Path.Combine(_tempDirectory, "Tools~");
            CreateToolsRootAt(toolsRoot, includeBuildInputs);
            return toolsRoot;
        }

        private static void CreateToolsRootAt(string toolsRoot, bool includeBuildInputs)
        {
            var unityAgentBridgeRoot = Path.Combine(toolsRoot, "UnityAgentBridge");
            Directory.CreateDirectory(Path.Combine(unityAgentBridgeRoot, "runtime-build"));
            Directory.CreateDirectory(Path.Combine(toolsRoot, "AgentBridge"));
            File.WriteAllText(Path.Combine(unityAgentBridgeRoot, "runtime-build", "Build-LocalRuntime.ps1"), "param()");
            File.WriteAllText(Path.Combine(toolsRoot, "AgentBridge", "Start-UnityAgentBridge-Mcp.cmd"), "@echo off");
            if (includeBuildInputs)
            {
                Directory.CreateDirectory(Path.Combine(unityAgentBridgeRoot, "src", "UnityAgentBridge.Cli"));
                Directory.CreateDirectory(Path.Combine(unityAgentBridgeRoot, "src", "UnityAgentBridge.RoslynCompiler"));
                File.WriteAllText(Path.Combine(unityAgentBridgeRoot, "src", "UnityAgentBridge.Cli", "UnityAgentBridge.Cli.csproj"), "<Project />");
                File.WriteAllText(Path.Combine(unityAgentBridgeRoot, "src", "UnityAgentBridge.RoslynCompiler", "UnityAgentBridge.RoslynCompiler.csproj"), "<Project />");
            }
        }

        private static bool IsSdkProbe(ProcessExecutionRequest request)
        {
            return request.FilePath == "dotnet" && request.Arguments.Count == 1 && request.Arguments[0] == "--list-sdks";
        }

        private static string GetArgumentValue(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }

            return string.Empty;
        }

        private static void CreateGeneratedRuntimeExecutables(string outputRoot)
        {
            var cliPath = Path.Combine(outputRoot, "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe");
            var roslynPath = Path.Combine(outputRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(cliPath));
            Directory.CreateDirectory(Path.GetDirectoryName(roslynPath));
            File.WriteAllText(cliPath, "cli");
            File.WriteAllText(roslynPath, "roslyn");
        }

        private sealed class FakeProcessRunner : IAsyncProcessRunner
        {
            public List<ProcessExecutionRequest> Requests { get; } = new List<ProcessExecutionRequest>();
            public Func<ProcessExecutionRequest, ProcessExecutionResult> Handler { get; set; }

            public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(Handler(request));
            }
        }
    }
}
