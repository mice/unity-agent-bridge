using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpProcessAndConfigTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge.P3", Guid.NewGuid().ToString("N"));
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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_031.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_031")]
        public void RunAsync_WithUnspecifiedCancellationMode_Throws()
        {
            var runner = new AsyncProcessRunner();

            Assert.Throws<ArgumentException>(() =>
                runner.RunAsync(new ProcessExecutionRequest
                {
                    FilePath = "cmd.exe",
                    Arguments = new[] { "/c", "echo", "hi" },
                    CancellationMode = ProcessCancellationMode.Unspecified,
                }, CancellationToken.None).GetAwaiter().GetResult());
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_134.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_134")]
        public void RunAsync_CompletedProcess_CapturesStdoutAfterExit()
        {
            var runner = new AsyncProcessRunner();

            var result = runner.RunAsync(new ProcessExecutionRequest
            {
                FilePath = "cmd.exe",
                Arguments = new[] { "/c", "echo", "{\"status\":\"success\",\"toolNames\":[\"mcp__unity__ping\"]}" },
                WorkingDirectory = _tempDirectory,
                Timeout = TimeSpan.FromSeconds(5),
                CancellationMode = ProcessCancellationMode.TerminateOnCancel,
                TerminateGracePeriod = TimeSpan.FromMilliseconds(500),
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Outcome, Is.EqualTo(ProcessOutcome.Completed));
            var normalized = result.Stdout.Replace("\\\"", "\"");
            Assert.That(normalized, Does.Contain("\"status\":\"success\""));
            Assert.That(normalized, Does.Contain("\"toolNames\":[\"mcp__unity__ping\"]"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_032.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_032")]
        public void ManagedBlockTextEditor_Apply_WrapsManagedBlock()
        {
            var editor = new ManagedBlockTextEditor();

            var result = editor.Apply(string.Empty, "[mcp_servers.unity_agent_bridge]\ncommand = \"cmd\"");

            Assert.That(result, Does.Contain(ManagedBlockTextEditor.BeginMarker));
            Assert.That(result, Does.Contain(ManagedBlockTextEditor.EndMarker));
            Assert.That(result, Does.Contain("[mcp_servers.unity_agent_bridge]"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_033.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_033")]
        public void ManagedBlockTextEditor_Remove_DeletesManagedBlockOnly()
        {
            var editor = new ManagedBlockTextEditor();
            var original = "prefix\n\n" + ManagedBlockTextEditor.BeginMarker + "\nbody\n" + ManagedBlockTextEditor.EndMarker + "\n\nsuffix";

            var result = editor.Remove(original);

            Assert.That(result, Does.Not.Contain(ManagedBlockTextEditor.BeginMarker));
            Assert.That(result, Does.Contain("prefix"));
            Assert.That(result, Does.Contain("suffix"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_034.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_034")]
        public void CodexProjectConfigWriter_Preview_UsesManagedBlockAndExecutable()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var executablePath = CreatePreparedExecutable(projectRoot);
            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain(ManagedBlockTextEditor.BeginMarker));
            Assert.That(preview, Does.Contain(executablePath.Replace("\\", "\\\\")));
            Assert.That(preview, Does.Contain("args = [\"mcp-server\"]"));
            Assert.That(preview, Does.Contain("[mcp_servers.unity_agent_bridge]"));
            AssertHasCodexProjectPathEnv(preview, projectRoot);
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Preview_UsesPreparedProjectRuntimeExecutableWhenAvailable()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var executablePath = CreatePreparedExecutable(projectRoot);

            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));
            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain(executablePath.Replace("\\", "\\\\")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Apply_ReturnsCliExecutableMissingWhenResolvedPathDoesNotExist()
        {
            var missingToolsRoot = Path.Combine(_tempDirectory, "missing-tools");
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));

            var result = writer.Apply(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
                ToolsRoot = missingToolsRoot,
            });

            Assert.That(result.Applied, Is.False);
            Assert.That(result.Reason, Is.EqualTo("cli_executable_missing"));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Preview_ReturnsCliExecutableMissingCommentWhenResolvedPathDoesNotExist()
        {
            var missingToolsRoot = Path.Combine(_tempDirectory, "missing-tools");
            var projectRoot = Path.Combine(_tempDirectory, "workspace", "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings
            {
                ToolsRoot = missingToolsRoot,
            });

            Assert.That(preview, Does.Contain("cli_executable_missing"));
            Assert.That(preview, Does.Contain("Resolved unity_agent_bridge executable path does not exist."));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_BuildExecutableCommand_DoesNotFallbackWhenExplicitToolsRootIsMissing()
        {
            var missingToolsRoot = Path.Combine(_tempDirectory, "missing-tools");
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");

            var executableCommand = CodexProjectConfigWriter.BuildExecutableCommand(
                new McpEditorSettings
                {
                    ToolsRoot = missingToolsRoot,
                },
                new McpPathResolver(() => projectRoot));

            Assert.That(executableCommand, Is.EqualTo(Path.GetFullPath(Path.Combine(
                projectRoot,
                ".unitymcp",
                "runtime",
                "UnityAgentBridge",
                "cli",
                "out",
                McpRuntimeInitializer.GetCurrentRid(),
                McpRuntimeInitializer.GetProductExecutableName()))));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_GetTargetPath_UsesWorkspaceRootInsteadOfUnityProjectRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".codex"));
            Directory.CreateDirectory(projectRoot);

            var targetPath = CodexProjectConfigWriter.GetTargetPath(McpPathResolver.ResolveWorkspaceRoot(projectRoot));

            Assert.That(targetPath, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), ".codex", "config.toml")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void ClaudeCodeProjectConfigWriter_GetTargetPath_UsesWorkspaceRootInsteadOfUnityProjectRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".codex"));
            Directory.CreateDirectory(projectRoot);

            var targetPath = ClaudeCodeProjectConfigWriter.GetTargetPath(McpPathResolver.ResolveWorkspaceRoot(projectRoot));

            Assert.That(targetPath, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), ".mcp.json")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_IsPathUnderRoot_RejectsRelativePathOutsideWorkspaceRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".codex"));
            Directory.CreateDirectory(projectRoot);

            var candidatePath = Path.GetFullPath(Path.Combine(workspaceRoot, @"..\outside\Start-UnityAgentBridge-Mcp.cmd"));
            var success = CodexProjectConfigWriter.IsPathUnderRoot(candidatePath, workspaceRoot);

            Assert.That(success, Is.False);
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Apply_ReplacesStandaloneUnityAgentBridgeSectionAndPreservesOtherContent()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var configDirectory = Path.Combine(workspaceRoot, ".codex");
            Directory.CreateDirectory(configDirectory);
            var targetPath = Path.Combine(configDirectory, "config.toml");
            File.WriteAllText(targetPath,
                "[mcp_servers.unity_agent_bridge]\ncommand = \"custom\"\n\n[other]\nvalue = 1\n");

            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            var executablePath = CreatePreparedExecutable(projectRoot);

            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));
            var result = writer.Apply(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
            });

            var content = File.ReadAllText(targetPath);
            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Contain(ManagedBlockTextEditor.BeginMarker));
            Assert.That(content, Does.Not.Contain("command = \"custom\""));
            Assert.That(content, Does.Contain("command = \"" + executablePath.Replace("\\", "\\\\") + "\""));
            AssertHasMcpServerArgs(content);
            AssertHasCodexProjectPathEnv(content, projectRoot);
            Assert.That(content, Does.Contain("[other]"));
            Assert.That(content, Does.Contain("value = 1"));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Apply_PreservesUnityAgentBridgeChildTables()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var configDirectory = Path.Combine(workspaceRoot, ".codex");
            Directory.CreateDirectory(configDirectory);
            var targetPath = Path.Combine(configDirectory, "config.toml");
            File.WriteAllText(targetPath,
                "[mcp_servers.unity_agent_bridge]\ncommand = \"custom\"\nargs = [\"old\"]\n\n" +
                "[mcp_servers.unity_agent_bridge.tools.mcp__unity__ping]\napproval_mode = \"approve\"\n\n" +
                "[mcp_servers.unity_agent_bridge.tools.mcp__unity__get_console_errors]\napproval_mode = \"approve\"\n");

            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            CreatePreparedExecutable(projectRoot);

            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));
            var result = writer.Apply(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
            });

            var content = File.ReadAllText(targetPath);
            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Contain(ManagedBlockTextEditor.BeginMarker));
            Assert.That(content, Does.Contain("[mcp_servers.unity_agent_bridge.tools.mcp__unity__ping]"));
            Assert.That(content, Does.Contain("[mcp_servers.unity_agent_bridge.tools.mcp__unity__get_console_errors]"));
            Assert.That(content, Does.Contain("approval_mode = \"approve\""));
            Assert.That(content, Does.Not.Contain("args = [\"old\"]"));
            AssertHasMcpServerArgs(content);
            AssertHasCodexProjectPathEnv(content, projectRoot);
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_Apply_PreservesNonUnityAgentBridgeSectionsWhenUsingTomlMerge()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var configDirectory = Path.Combine(workspaceRoot, ".codex");
            Directory.CreateDirectory(configDirectory);
            var targetPath = Path.Combine(configDirectory, "config.toml");
            File.WriteAllText(targetPath,
                "[mcp_servers.unity_agent_bridge]\ncommand = \"custom\"\n\n" +
                "[mcp_servers.other]\ncommand = \"keep\"\n\n" +
                "[other]\nvalue = 1\n");

            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            CreatePreparedExecutable(projectRoot);

            var writer = new CodexProjectConfigWriter(new CodexTomlConfigEditor(), new McpPathResolver(() => projectRoot));
            var result = writer.Apply(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
            });

            var content = File.ReadAllText(targetPath);
            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Contain("[mcp_servers.unity_agent_bridge]"));
            Assert.That(content, Does.Contain("[mcp_servers.other]"));
            Assert.That(content, Does.Contain("command = \"keep\""));
            Assert.That(content, Does.Contain("[other]"));
            Assert.That(content, Does.Contain("value = 1"));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CodexProjectConfigWriter_ValidateManagedTomlResult_RejectsResidualStandaloneUnityAgentBridgeSection()
        {
            var invalid = "[mcp_servers.unity_agent_bridge]\ncommand = \"custom\"\n\n" +
                          ManagedBlockTextEditor.BeginMarker + "\n" +
                          "[mcp_servers.unity_agent_bridge]\ncommand = \"cmd\"\n" +
                          ManagedBlockTextEditor.EndMarker + "\n";

            var valid = CodexProjectConfigWriter.ValidateManagedTomlResult(invalid);

            Assert.That(valid, Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_035.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_035")]
        public void ManagedJsonMerger_Apply_CreatesMcpServersEntry()
        {
            var targetPath = Path.Combine(_tempDirectory, ".mcp.json");
            var merger = new ManagedJsonMerger();

            var result = merger.Apply(targetPath, "{ \"command\": \"cmd\", \"args\": [\"/d\"], \"cwd\": \".\" }");
            var content = File.ReadAllText(targetPath);

            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Contain("\"mcpServers\""));
            Assert.That(content, Does.Contain("\"unity_agent_bridge\""));
        }

        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_177")]
        public void ManagedJsonMerger_Apply_CreatesCopilotServersEntry()
        {
            var targetPath = Path.Combine(_tempDirectory, "mcp.json");
            var merger = new ManagedJsonMerger();

            var result = merger.Apply(targetPath, "{ \"command\": \"cmd\", \"args\": [\"/d\"] }", "servers");
            var content = File.ReadAllText(targetPath);

            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Contain("\"servers\""));
            Assert.That(content, Does.Contain("\"unity_agent_bridge\""));
            Assert.That(content, Does.Not.Contain("\"mcpServers\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_036.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_036")]
        public void ManagedJsonMerger_Remove_RemovesManagedEntryOnly()
        {
            var targetPath = Path.Combine(_tempDirectory, ".mcp.json");
            File.WriteAllText(targetPath, "{\n  \"mcpServers\": {\n    \"unity_agent_bridge\": { \"command\": \"cmd\" },\n    \"other\": { \"command\": \"other\" }\n  }\n}");

            var merger = new ManagedJsonMerger();

            var result = merger.Remove(targetPath);
            var content = File.ReadAllText(targetPath);

            Assert.That(result.Applied, Is.True);
            Assert.That(content, Does.Not.Contain("\"unity_agent_bridge\""));
            Assert.That(content, Does.Contain("\"other\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_037.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_037")]
        public void ManagedJsonMerger_BrokenJson_CreatesBackupAndReturnsParseFailed()
        {
            var targetPath = Path.Combine(_tempDirectory, ".mcp.json");
            var originalExists = File.Exists(targetPath);
            var originalContent = originalExists ? File.ReadAllText(targetPath) : null;

            try
            {
                File.WriteAllText(targetPath, "{ broken");
                var merger = new ManagedJsonMerger();

                var result = merger.Apply(targetPath, "{ \"command\": \"cmd\" }");

                Assert.That(result.Applied, Is.False);
                Assert.That(result.Reason, Is.EqualTo("parse_failed").Or.EqualTo("backup_failed"));
                if (result.Reason == "parse_failed")
                {
                    Assert.That(result.BackupPath, Is.Not.Empty);
                    Assert.That(File.Exists(result.BackupPath), Is.True);
                }
            }
            finally
            {
                if (originalExists)
                {
                    File.WriteAllText(targetPath, originalContent);
                }
                else if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_038.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_038")]
        public void ClaudeCodeProjectConfigWriter_Preview_ContainsUnityAgentBridgeEntry()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var executablePath = CreatePreparedExecutable(projectRoot);
            var writer = new ClaudeCodeProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain("\"mcpServers\""));
            Assert.That(preview, Does.Contain("\"unity_agent_bridge\""));
            Assert.That(preview, Does.Contain(executablePath.Replace("\\", "\\\\")));
            Assert.That(preview, Does.Contain("\"args\": [\"mcp-server\"]"));
        }

        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_178")]
        public void CursorProjectConfigWriter_Preview_ContainsMcpServersEntry()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var launcherPath = CreatePreparedLauncher(projectRoot);
            var writer = new CursorProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain("\"mcpServers\""));
            Assert.That(preview, Does.Contain("\"unity_agent_bridge\""));
            Assert.That(preview, Does.Contain(launcherPath.Replace("\\", "\\\\")));
            Assert.That(preview, Does.Contain("\"UNITY_AGENT_BRIDGE_PROJECT_PATH\""));
            Assert.That(preview, Does.Contain(projectRoot.Replace("\\", "\\\\")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void GitHubCopilotProjectConfigWriter_Preview_ContainsServersEntry()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var launcherPath = CreatePreparedLauncher(projectRoot);
            var writer = new GitHubCopilotProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain("\"servers\""));
            Assert.That(preview, Does.Contain("\"unity_agent_bridge\""));
            Assert.That(preview, Does.Contain(launcherPath.Replace("\\", "\\\\")));
            Assert.That(preview, Does.Contain("\"UNITY_AGENT_BRIDGE_PROJECT_PATH\""));
            Assert.That(preview, Does.Contain(projectRoot.Replace("\\", "\\\\")));
            Assert.That(preview, Does.Not.Contain("\"mcpServers\""));
        }

        [Test]
        [Category("AGBM_P3")]
        public void CursorAndCopilotConfigWriters_TargetWorkspaceClientFiles()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");

            Assert.That(
                CursorProjectConfigWriter.GetTargetPath(workspaceRoot),
                Is.EqualTo(Path.Combine(workspaceRoot, ".cursor", "mcp.json")));
            Assert.That(
                GitHubCopilotProjectConfigWriter.GetTargetPath(workspaceRoot),
                Is.EqualTo(Path.Combine(workspaceRoot, ".vscode", "mcp.json")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void ClaudeCodeProjectConfigWriter_Preview_UsesPreparedProjectRuntimeExecutableWhenAvailable()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var executablePath = CreatePreparedExecutable(projectRoot);

            var writer = new ClaudeCodeProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));
            var preview = writer.Preview(new McpEditorSettings());

            Assert.That(preview, Does.Contain(executablePath.Replace("\\", "\\\\")));
        }

        [Test]
        [Category("AGBM_P3")]
        public void ClaudeCodeProjectConfigWriter_Apply_ReturnsCliExecutableMissingWhenResolvedPathDoesNotExist()
        {
            var missingToolsRoot = Path.Combine(_tempDirectory, "missing-tools");
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            var writer = new ClaudeCodeProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));

            var result = writer.Apply(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
                ToolsRoot = missingToolsRoot,
            });

            Assert.That(result.Applied, Is.False);
            Assert.That(result.Reason, Is.EqualTo("cli_executable_missing"));
        }

        [Test]
        [Category("AGBM_P3")]
        public void ClaudeCodeProjectConfigWriter_Preview_ReturnsCliExecutableMissingPayloadWhenResolvedPathDoesNotExist()
        {
            var missingToolsRoot = Path.Combine(_tempDirectory, "missing-tools");
            var projectRoot = Path.Combine(_tempDirectory, "workspace", "nested", "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);
            var writer = new ClaudeCodeProjectConfigWriter(new ManagedJsonMerger(), new McpPathResolver(() => projectRoot));

            var preview = writer.Preview(new McpEditorSettings
            {
                ToolsRoot = missingToolsRoot,
            });

            Assert.That(preview, Does.Contain("\"error\": \"cli_executable_missing\""));
            Assert.That(preview, Does.Contain("Resolved unity_agent_bridge executable path does not exist."));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_174.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_174")]
        public void McpRuntimeInitializer_InitializeRuntimeAsync_MissingMcpRoot_ReturnsMcpRootMissing()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var toolsRoot = Path.Combine(_tempDirectory, "empty-tools-root");
            Directory.CreateDirectory(toolsRoot);
            var resolver = new McpPathResolver(() => projectRoot);
            var initializer = new McpRuntimeInitializer(new FakeProcessRunner(), resolver);

            var result = initializer.InitializeRuntimeAsync(new McpEditorSettings
            {
                WorkspaceRoot = Path.Combine(_tempDirectory, "workspace"),
                ToolsRoot = toolsRoot,
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Applied, Is.False);
            Assert.That(result.Reason, Is.EqualTo("mcp_root_missing"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_175.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_175")]
        public void McpRuntimeInitializer_InitializeRuntimeAsync_MissingCliExecutable_ReturnsCliExecutableMissing()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            var payloadMcpRoot = Path.Combine(toolsRoot, "UnityAgentBridge");
            var payloadCliRoot = Path.Combine(payloadMcpRoot, "cli");
            Directory.CreateDirectory(payloadMcpRoot);
            Directory.CreateDirectory(payloadCliRoot);
            File.WriteAllText(Path.Combine(_tempDirectory, "package.json"), "{ }");
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            Directory.CreateDirectory(projectRoot);
            var resolver = new McpPathResolver(() => projectRoot);
            var initializer = new McpRuntimeInitializer(new FakeProcessRunner(), resolver);
            var result = initializer.InitializeRuntimeAsync(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
                ToolsRoot = toolsRoot,
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Applied, Is.False);
            Assert.That(result.Reason, Is.EqualTo("cli_executable_missing"));
            Assert.That(result.TargetPath, Is.EqualTo(Path.GetFullPath(Path.Combine(projectRoot, ".unitymcp", "runtime", "UnityAgentBridge", "cli"))));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_176.md
        [Test]
        [Category("AGBM_P3")]
        [Category("AGBM_176")]
        public void McpRuntimeInitializer_InitializeRuntime_MaterializesPayloadWithoutProcessExecution()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            var payloadMcpRoot = Path.Combine(toolsRoot, "UnityAgentBridge");
            var runtimeRid = McpRuntimeInitializer.GetCurrentRid();
            var executableName = McpRuntimeInitializer.GetProductExecutableName();
            var payloadCliRoot = Path.Combine(payloadMcpRoot, "cli", "out", runtimeRid);
            var payloadRoslynRoot = Path.Combine(payloadMcpRoot, "roslyn-execution", "out", "win-x64");
            Directory.CreateDirectory(payloadMcpRoot);
            Directory.CreateDirectory(payloadCliRoot);
            Directory.CreateDirectory(payloadRoslynRoot);
            File.WriteAllText(Path.Combine(_tempDirectory, "package.json"), "{ }");
            File.WriteAllText(Path.Combine(payloadCliRoot, executableName), "stub");
            File.WriteAllText(Path.Combine(payloadRoslynRoot, "unity-roslyn-compiler.exe"), "stub");
            var fakeRunner = new FakeProcessRunner
            {
                Result = new ProcessExecutionResult
                {
                    Outcome = ProcessOutcome.Completed,
                    ExitCode = 0,
                }
            };
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            Directory.CreateDirectory(projectRoot);
            var resolver = new McpPathResolver(() => projectRoot);
            var initializer = new McpRuntimeInitializer(fakeRunner, resolver);
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");

            var result = initializer.InitializeRuntimeAsync(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
                ToolsRoot = toolsRoot,
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Applied, Is.True);
            var runtimeRoot = Path.GetFullPath(Path.Combine(projectRoot, ".unitymcp", "runtime"));
            var runtimeMcpRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            var runtimeCliPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", runtimeRid, executableName);
            var runtimeRoslynPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe");
            Assert.That(result.TargetPath, Is.EqualTo(runtimeRoot));
            Assert.That(fakeRunner.LastRequest, Is.Null);
            Assert.That(Directory.Exists(runtimeMcpRoot), Is.True);
            Assert.That(File.Exists(runtimeCliPath), Is.True);
            Assert.That(File.Exists(runtimeRoslynPath), Is.True);
        }

        [Test]
        [Category("AGBM_P3")]
        public void McpRuntimeInitializer_MaterializeRuntimePayload_ExcludesNodeRuntimeSources()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            var payloadMcpRoot = Path.Combine(toolsRoot, "UnityAgentBridge");
            var nodeModulesRoot = Path.Combine(payloadMcpRoot, "node_modules", "tsx");
            var nodeToolsRoot = Path.Combine(payloadMcpRoot, "tools");
            Directory.CreateDirectory(nodeModulesRoot);
            Directory.CreateDirectory(nodeToolsRoot);
            File.WriteAllText(Path.Combine(payloadMcpRoot, "placeholder.txt"), "payload");
            File.WriteAllText(Path.Combine(payloadMcpRoot, "server.ts"), "export {};");
            File.WriteAllText(Path.Combine(payloadMcpRoot, "probe.ts"), "export {};");
            File.WriteAllText(Path.Combine(payloadMcpRoot, "package-lock.json"), "{}");
            File.WriteAllText(Path.Combine(nodeModulesRoot, "index.js"), "module.exports = {};");
            File.WriteAllText(Path.Combine(nodeToolsRoot, "ping.ts"), "export {};");
            var runtimeRoot = Path.Combine(_tempDirectory, "runtime");

            McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);

            var runtimeMcpRoot = Path.Combine(runtimeRoot, "UnityAgentBridge");
            Assert.That(File.Exists(Path.Combine(runtimeMcpRoot, "placeholder.txt")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(runtimeMcpRoot, "node_modules")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(runtimeMcpRoot, "tools")), Is.False);
            Assert.That(File.Exists(Path.Combine(runtimeMcpRoot, "server.ts")), Is.False);
            Assert.That(File.Exists(Path.Combine(runtimeMcpRoot, "probe.ts")), Is.False);
            Assert.That(File.Exists(Path.Combine(runtimeMcpRoot, "package-lock.json")), Is.False);
        }

        [Test]
        [Category("AGBM_P3")]
        public void McpRuntimeInitializer_MaterializeRuntimePayload_PreservesLockedRuntimeExecutableWhileRefreshingRoslynPayload()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            var payloadCliRoot = Path.Combine(toolsRoot, "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid());
            var payloadRoslynRoot = Path.Combine(toolsRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64");
            Directory.CreateDirectory(payloadCliRoot);
            Directory.CreateDirectory(payloadRoslynRoot);
            File.WriteAllText(Path.Combine(payloadCliRoot, McpRuntimeInitializer.GetProductExecutableName()), "new-cli");
            File.WriteAllText(Path.Combine(payloadRoslynRoot, "unity-roslyn-compiler.exe"), "new-roslyn");

            var runtimeRoot = Path.Combine(_tempDirectory, "runtime");
            var runtimeCliRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid());
            var runtimeRoslynRoot = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64");
            Directory.CreateDirectory(runtimeCliRoot);
            Directory.CreateDirectory(runtimeRoslynRoot);
            var runtimeExecutablePath = Path.Combine(runtimeCliRoot, McpRuntimeInitializer.GetProductExecutableName());
            File.WriteAllText(runtimeExecutablePath, "old-cli");
            File.WriteAllText(Path.Combine(runtimeRoslynRoot, "unity-roslyn-compiler.exe"), "old-roslyn");

            using (new FileStream(runtimeExecutablePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);
            }

            Assert.That(File.ReadAllText(runtimeExecutablePath), Is.EqualTo("old-cli"));
            Assert.That(File.ReadAllText(Path.Combine(runtimeRoslynRoot, "unity-roslyn-compiler.exe")), Is.EqualTo("new-roslyn"));
        }

        [Test]
        [Category("AGBM_P3")]
        public void McpRuntimeInitializer_MaterializeRuntimePayload_PreservesLocallyBuiltExecutablesWhenPackageContainsNoExe()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            Directory.CreateDirectory(Path.Combine(toolsRoot, "UnityAgentBridge", "cli"));
            Directory.CreateDirectory(Path.Combine(toolsRoot, "UnityAgentBridge", "runtime-build"));
            File.WriteAllText(Path.Combine(toolsRoot, "UnityAgentBridge", "runtime-build", "Build-LocalRuntime.ps1"), "param()");

            var runtimeRoot = Path.Combine(_tempDirectory, "runtime");
            var runtimeCliPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid(), McpRuntimeInitializer.GetProductExecutableName());
            var runtimeRoslynPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeCliPath));
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeRoslynPath));
            File.WriteAllText(runtimeCliPath, "built-cli");
            File.WriteAllText(runtimeRoslynPath, "built-roslyn");

            McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);

            Assert.That(File.Exists(runtimeCliPath), Is.True);
            Assert.That(File.Exists(runtimeRoslynPath), Is.True);
            Assert.That(File.ReadAllText(runtimeCliPath), Is.EqualTo("built-cli"));
            Assert.That(File.ReadAllText(runtimeRoslynPath), Is.EqualTo("built-roslyn"));
            Assert.That(File.Exists(Path.Combine(runtimeRoot, "UnityAgentBridge", "runtime-build", "Build-LocalRuntime.ps1")), Is.True);
        }

        [Test]
        [Category("AGBM_P3")]
        public void McpRuntimeInitializer_MaterializeRuntimePayload_CanRepeatWhenGeneratedExecutablesAlreadyExist()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            Directory.CreateDirectory(Path.Combine(toolsRoot, "UnityAgentBridge", "cli"));
            File.WriteAllText(Path.Combine(toolsRoot, "UnityAgentBridge", "README.md"), "payload");

            var runtimeRoot = Path.Combine(_tempDirectory, "runtime");
            var runtimeCliPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid(), McpRuntimeInitializer.GetProductExecutableName());
            var runtimeRoslynPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "roslyn-execution", "out", "win-x64", "unity-roslyn-compiler.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeCliPath));
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeRoslynPath));
            File.WriteAllText(runtimeCliPath, "built-cli");
            File.WriteAllText(runtimeRoslynPath, "built-roslyn");

            McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);
            McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);

            Assert.That(File.ReadAllText(runtimeCliPath), Is.EqualTo("built-cli"));
            Assert.That(File.ReadAllText(runtimeRoslynPath), Is.EqualTo("built-roslyn"));
            Assert.That(File.Exists(Path.Combine(runtimeRoot, "UnityAgentBridge", "README.md")), Is.True);
        }

        [Test]
        [Category("AGBM_P3")]
        public void McpRuntimeInitializer_MaterializeRuntimePayload_CanRepeatWhenGeneratedExecutableIsLocked()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "mcp-tools");
            Directory.CreateDirectory(Path.Combine(toolsRoot, "UnityAgentBridge", "cli"));
            File.WriteAllText(Path.Combine(toolsRoot, "UnityAgentBridge", "README.md"), "payload");

            var runtimeRoot = Path.Combine(_tempDirectory, "runtime");
            var runtimeCliPath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", McpRuntimeInitializer.GetCurrentRid(), McpRuntimeInitializer.GetProductExecutableName());
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeCliPath));
            File.WriteAllText(runtimeCliPath, "built-cli");

            using (new FileStream(runtimeCliPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);
                McpRuntimeInitializer.MaterializeRuntimePayload(toolsRoot, runtimeRoot);
            }

            Assert.That(File.ReadAllText(runtimeCliPath), Is.EqualTo("built-cli"));
            Assert.That(File.Exists(Path.Combine(runtimeRoot, "UnityAgentBridge", "README.md")), Is.True);
        }

        private static string CreatePreparedLauncher(string projectRoot)
        {
            var launcherPath = Path.Combine(projectRoot, ".unitymcp", "runtime", "AgentBridge", "Start-UnityAgentBridge-Mcp.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath) ?? projectRoot);
            File.WriteAllText(launcherPath, "echo launcher");
            return launcherPath;
        }

        private static string CreatePreparedExecutable(string projectRoot)
        {
            var executablePath = Path.Combine(
                projectRoot,
                ".unitymcp",
                "runtime",
                "UnityAgentBridge",
                "cli",
                "out",
                McpRuntimeInitializer.GetCurrentRid(),
                McpRuntimeInitializer.GetProductExecutableName());
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath) ?? projectRoot);
            File.WriteAllText(executablePath, "stub");
            return executablePath;
        }

        private static void AssertHasMcpServerArgs(string content)
        {
            Assert.That(content, Does.Match(@"(?m)^args\s*=\s*\[\s*""mcp-server""\s*\]"));
        }

        private static void AssertHasCodexProjectPathEnv(string content, string projectRoot)
        {
            Assert.That(content, Does.Contain("[mcp_servers.unity_agent_bridge.env]"));
            Assert.That(content, Does.Contain("UNITY_AGENT_BRIDGE_PROJECT_PATH = \"" + projectRoot.Replace("\\", "\\\\") + "\""));
        }

        private sealed class FakeProcessRunner : IAsyncProcessRunner
        {
            public ProcessExecutionRequest LastRequest { get; private set; }
            public ProcessExecutionResult Result { get; set; } = new ProcessExecutionResult
            {
                Outcome = ProcessOutcome.Completed,
                ExitCode = 0,
            };

            public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(Result);
            }
        }

    }

    public sealed class McpServerProcessProbeTests
    {
        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void Classify_CurrentProjectCommandLine_ReturnsCurrentProject()
        {
            var projectRoot = Normalize(Path.Combine(Path.GetTempPath(), "CurrentProject"));
            var descriptor = new McpProcessDescriptor
            {
                ProcessId = 101,
                ProcessName = "unity-agent-bridge",
                ExecutablePath = Path.Combine(projectRoot, ".unitymcp", "runtime", "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe"),
                CommandLine = "UNITY_PROJECT_PATH=" + projectRoot + " unity-agent-bridge.exe mcp-server",
            };

            var info = McpServerProcessProbe.Classify(descriptor, projectRoot, Path.Combine(projectRoot, ".unitymcp", "runtime"));

            Assert.That(info, Is.Not.Null);
            Assert.That(info.MatchKind, Is.EqualTo(McpServerProcessMatchKind.CurrentProject));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void Classify_RuntimePathEvidence_ReturnsPreparedRuntime()
        {
            var projectRoot = Normalize(Path.Combine(Path.GetTempPath(), "RuntimeProject"));
            var runtimeRoot = Path.Combine(projectRoot, ".unitymcp", "runtime");
            var descriptor = new McpProcessDescriptor
            {
                ProcessId = 102,
                ProcessName = "unity-agent-bridge",
                ExecutablePath = Path.Combine(runtimeRoot, "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe"),
                CommandLine = "unity-agent-bridge.exe mcp-server",
            };

            var info = McpServerProcessProbe.Classify(descriptor, projectRoot, runtimeRoot);

            Assert.That(info, Is.Not.Null);
            Assert.That(info.MatchKind, Is.EqualTo(McpServerProcessMatchKind.PreparedRuntime));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void Classify_DifferentProjectBinding_ReturnsMismatchedProject()
        {
            var projectRoot = Normalize(Path.Combine(Path.GetTempPath(), "CurrentProject"));
            var otherRoot = Normalize(Path.Combine(Path.GetTempPath(), "OtherProject"));
            var descriptor = new McpProcessDescriptor
            {
                ProcessId = 103,
                ProcessName = "unity-agent-bridge",
                ExecutablePath = Path.Combine(otherRoot, ".unitymcp", "runtime", "UnityAgentBridge", "cli", "out", "win-x64", "unity-agent-bridge.exe"),
                CommandLine = "UNITY_PROJECT_PATH=" + otherRoot + " unity-agent-bridge.exe mcp-server",
            };

            var info = McpServerProcessProbe.Classify(descriptor, projectRoot, Path.Combine(projectRoot, ".unitymcp", "runtime"));

            Assert.That(info, Is.Not.Null);
            Assert.That(info.MatchKind, Is.EqualTo(McpServerProcessMatchKind.MismatchedProject));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void StopCurrentProjectServers_DoesNotTerminateAmbiguousCandidate()
        {
            var provider = new FakeProcessProvider(new[]
            {
                new McpProcessDescriptor
                {
                    ProcessId = 104,
                    ProcessName = "unity-agent-bridge",
                    ExecutablePath = "C:/Tools/unity-agent-bridge.exe",
                    CommandLine = "unity-agent-bridge.exe mcp-server",
                }
            });
            var probe = CreateProbe(provider, "C:/Project", "C:/Project/.unitymcp/runtime");

            var result = probe.StopCurrentProjectServers(new McpEditorSettings());

            Assert.That(result.Attempted, Is.False);
            Assert.That(provider.TerminatedProcessIds, Is.Empty);
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void StopCurrentProjectServers_TerminatesPreparedRuntimeMatch()
        {
            var provider = new FakeProcessProvider(new[]
            {
                new McpProcessDescriptor
                {
                    ProcessId = 105,
                    ProcessName = "unity-agent-bridge",
                    ExecutablePath = "C:/Project/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe",
                    CommandLine = "unity-agent-bridge.exe mcp-server",
                }
            });
            var probe = CreateProbe(provider, "C:/Project", "C:/Project/.unitymcp/runtime");

            var result = probe.StopCurrentProjectServers(new McpEditorSettings());

            Assert.That(result.Attempted, Is.True);
            Assert.That(provider.TerminatedProcessIds, Is.EquivalentTo(new[] { 105 }));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void Inspect_NoLongRunningServerProcess_ReportsProcessStateWithoutImplyingBridgeFailure()
        {
            var provider = new FakeProcessProvider(Array.Empty<McpProcessDescriptor>());
            var probe = CreateProbe(provider, "C:/Project", "C:/Project/.unitymcp/runtime");

            var snapshot = probe.Inspect(new McpEditorSettings());

            Assert.That(snapshot.State, Is.EqualTo(McpServerProcessState.Stopped));
            Assert.That(snapshot.Summary, Is.EqualTo("Idle (0/0)"));
            Assert.That(snapshot.Detail, Does.Contain("MCP may still be available through on-demand CLI calls."));
            Assert.That(snapshot.Summary, Does.Not.Contain("not running for this project"));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void Inspect_MismatchedProjectServerProcess_ReportsZeroOfDetectedProcesses()
        {
            var provider = new FakeProcessProvider(new[]
            {
                new McpProcessDescriptor
                {
                    ProcessId = 106,
                    ProcessName = "unity-agent-bridge",
                    ExecutablePath = "C:/Other/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe",
                    CommandLine = "UNITY_PROJECT_PATH=C:/Other unity-agent-bridge.exe mcp-server",
                }
            });
            var probe = CreateProbe(provider, "C:/Project", "C:/Project/.unitymcp/runtime");

            var snapshot = probe.Inspect(new McpEditorSettings());

            Assert.That(snapshot.State, Is.EqualTo(McpServerProcessState.MismatchedProject));
            Assert.That(snapshot.Summary, Is.EqualTo("Foreign (0/1)"));
            Assert.That(snapshot.Detail, Is.EqualTo("1 long-running MCP server process is tied to another Unity project."));
        }

        [Test]
        [Category("AGBM_MCP_PROCESS")]
        public void SystemProcessProvider_Source_FiltersByProcessNameBeforeMainModuleInspection()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Mcp/Process/McpRuntimeInitializer.cs"));
            var filterIndex = content.IndexOf("LooksLikeUnityAgentBridgeProcessName(processName)");
            var mainModuleIndex = content.IndexOf("process.MainModule");

            Assert.That(filterIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(mainModuleIndex, Is.GreaterThan(filterIndex));
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }

        private static McpServerProcessProbe CreateProbe(FakeProcessProvider provider, string projectRoot, string runtimeRoot)
        {
            return new McpServerProcessProbe(
                provider,
                new McpPathResolver(),
                _ => projectRoot,
                _ => runtimeRoot);
        }

        private static string GetPackageRelativePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class FakeProcessProvider : IMcpServerProcessProvider
        {
            private readonly IReadOnlyList<McpProcessDescriptor> _processes;

            public FakeProcessProvider(IReadOnlyList<McpProcessDescriptor> processes)
            {
                _processes = processes;
            }

            public List<int> TerminatedProcessIds { get; } = new List<int>();

            public IReadOnlyList<McpProcessDescriptor> GetProcesses()
            {
                return _processes;
            }

            public bool TryTerminate(int processId, out string error)
            {
                error = string.Empty;
                TerminatedProcessIds.Add(processId);
                return true;
            }
        }
    }
}
