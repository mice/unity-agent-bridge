using System;
using System.IO;
using NUnit.Framework;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class McpPathResolverTests
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
            AgentBridgeBootstrap.SetSuppressStartForTests(false);
        }

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "UnityMcp.AgentBridge", Guid.NewGuid().ToString("N"));
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

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_013.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_013")]
        public void ResolveExecutablePath_UsesConfiguredPathWhenFileExists()
        {
            var filePath = CreateExecutable("node.exe");
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath(filePath, "node");

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(filePath)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_014.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_014")]
        public void ResolveExecutablePath_InvalidConfiguredPath_FallsBackToPathSearch()
        {
            var filePath = CreateExecutable(IsWindows() ? "npm.cmd" : "npm");
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath("C:/missing/npm.cmd", "npm");

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(filePath)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveExecutablePath_WindowsPrefersRunnableExtensionBeforeExtensionlessShim()
        {
            if (!IsWindows())
            {
                Assert.Pass("Windows-only PATH extension ordering.");
            }

            CreateExecutable("npm");
            var cmdPath = CreateExecutable("npm.cmd");
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath(string.Empty, "npm");

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(cmdPath)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_015.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_015")]
        public void ResolveExecutablePath_ShellSnippetConfiguredPath_IsRejected()
        {
            var filePath = CreateExecutable("dotnet.exe");
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath("dotnet.exe && whoami", "dotnet");

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(filePath)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_016.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_016")]
        public void ResolveExecutablePath_EmptyExecutableName_ReturnsEmpty()
        {
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath(string.Empty, string.Empty);

            Assert.That(resolved, Is.Empty);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_017.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_017")]
        public void ResolveExecutablePath_PathSearchFindsExeExtensionOnWindows()
        {
            var filePath = CreateExecutable("codex");
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath(string.Empty, "codex");

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(filePath)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_018.md
        [Test]
        [Category("AGBM_Discovery")]
        [Category("AGBM_018")]
        public void ResolveExecutablePath_MissingExecutable_ReturnsEmpty()
        {
            Environment.SetEnvironmentVariable("PATH", _tempDirectory);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveExecutablePath(string.Empty, "claude");

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveLauncherPath_UsesPreparedProjectRuntimeWhenLauncherExists()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var launcherPath = Path.Combine(projectRoot, ".unitymcp", "runtime", "AgentBridge", "Start-UnityAgentBridge-Mcp.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(launcherPath) ?? projectRoot);
            File.WriteAllText(launcherPath, "echo launcher");
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.ResolveLauncherPath(new McpEditorSettings());

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(launcherPath)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveMcpServerRoot_UsesPreparedProjectRuntimeWhenServerExists()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var serverRoot = Path.Combine(projectRoot, ".unitymcp", "runtime", "UnityAgentBridge");
            Directory.CreateDirectory(serverRoot);
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.ResolveMcpServerRoot(new McpEditorSettings());

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(serverRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveCliRoot_UsesPreparedProjectRuntimeWhenCliExists()
        {
            var projectRoot = Path.Combine(_tempDirectory, "UnityProject");
            var cliRoot = Path.Combine(projectRoot, ".unitymcp", "runtime", "UnityAgentBridge", "cli");
            Directory.CreateDirectory(cliRoot);
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.ResolveCliRoot(new McpEditorSettings());

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(cliRoot)));
        }

        [Test]
        [Timeout(5000)]
        [Category("AGBM_Discovery")]
        public void ResolveWorkspaceRuntimeRoot_UsesUnityProjectRootInsteadOfWorkspaceRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "UnityProject");
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(projectRoot);
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.ResolveWorkspaceRuntimeRoot(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
            });

            Assert.That(resolved, Is.EqualTo(Path.Combine(Path.GetFullPath(projectRoot), ".unitymcp", "runtime")));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveToolsRoot_PrefersConfiguredExistingPath()
        {
            var toolsRoot = Path.Combine(_tempDirectory, "Tools");
            Directory.CreateDirectory(toolsRoot);
            var resolver = new McpPathResolver();

            var resolved = resolver.ResolveToolsRoot(new McpEditorSettings
            {
                ToolsRoot = toolsRoot,
            });

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(toolsRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveToolsRoot_ManifestFileDependency_ResolvesRelativeToPackagesDirectory()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "UnityMCP");
            var packageRoot = Path.Combine(workspaceRoot, "..", "unity-agent-bridge", "com.unitymcp.agent-bridge");
            var projectToolsRoot = Path.Combine(projectRoot, "Tools");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
            Directory.CreateDirectory(projectToolsRoot);
            CreatePackageToolsRoot(packageRoot);
            File.WriteAllText(
                Path.Combine(projectRoot, "Packages", "manifest.json"),
                "{\"dependencies\":{\"com.unitymcp.agent-bridge\":\"file:../../../unity-agent-bridge/com.unitymcp.agent-bridge\"}}");

            var resolved = McpPathResolver.TryResolveManifestPackageToolsRoot(projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(Path.Combine(packageRoot, "Tools~"))));
            Assert.That(resolved, Is.Not.EqualTo(Path.GetFullPath(projectToolsRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveToolsRoot_PackageCachePackage_ResolvesPackageToolsRoot()
        {
            var projectRoot = Path.Combine(_tempDirectory, "MVVMBind");
            var projectToolsRoot = Path.Combine(projectRoot, "Tools");
            var packageRoot = Path.Combine(projectRoot, "Library", "PackageCache", "com.unitymcp.agent-bridge@1.2.5");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Packages"));
            Directory.CreateDirectory(projectToolsRoot);
            CreatePackageToolsRoot(packageRoot);
            File.WriteAllText(
                Path.Combine(projectRoot, "Packages", "manifest.json"),
                "{\"dependencies\":{\"com.unitymcp.agent-bridge\":\"git+https://github.com/mice/unity-agent-bridge.git?path=/com.unitymcp.agent-bridge#v1.2.5\"}}");

            var resolved = McpPathResolver.TryResolvePackageCacheToolsRoot(projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(Path.Combine(packageRoot, "Tools~"))));
            Assert.That(resolved, Is.Not.EqualTo(Path.GetFullPath(projectToolsRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveWorkspaceRoot_PrefersNearestAncestorWithCodexDirectory()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".codex"));
            Directory.CreateDirectory(projectRoot);

            var resolved = McpPathResolver.ResolveWorkspaceRoot(projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void ResolveWorkspaceRoot_FallsBackToProjectRootWhenNoWorkspaceMarkersExist()
        {
            var projectRoot = Path.Combine(_tempDirectory, "standalone-project");
            Directory.CreateDirectory(projectRoot);

            var resolved = McpPathResolver.ResolveWorkspaceRoot(projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(projectRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void GetWorkspaceRoot_PrefersConfiguredExistingWorkspaceRoot()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var projectRoot = Path.Combine(workspaceRoot, "nested", "RuntimeCallSample");
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(projectRoot);
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.GetWorkspaceRoot(new McpEditorSettings
            {
                WorkspaceRoot = workspaceRoot,
            });

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void GetWorkspaceRoot_IgnoresConfiguredWorkspaceRootOutsideAllowedAncestors()
        {
            var configuredWorkspaceRoot = Path.Combine(_tempDirectory, "outside-workspace");
            var projectRoot = Path.Combine(_tempDirectory, "workspace", "nested", "RuntimeCallSample");
            Directory.CreateDirectory(configuredWorkspaceRoot);
            Directory.CreateDirectory(projectRoot);
            var resolver = new McpPathResolver(() => projectRoot);

            var resolved = resolver.GetWorkspaceRoot(new McpEditorSettings
            {
                WorkspaceRoot = configuredWorkspaceRoot,
            });

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(projectRoot)));
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void IsWorkspaceRootAllowedForProject_AllowsProjectAndFirstThreeAncestors()
        {
            var workspaceRoot = Path.Combine(_tempDirectory, "workspace");
            var level1 = Path.Combine(workspaceRoot, "a");
            var level2 = Path.Combine(level1, "b");
            var projectRoot = Path.Combine(level2, "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);

            Assert.That(McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, projectRoot), Is.True);
            Assert.That(McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, level2), Is.True);
            Assert.That(McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, level1), Is.True);
            Assert.That(McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, workspaceRoot), Is.True);
        }

        [Test]
        [Category("AGBM_Discovery")]
        public void IsWorkspaceRootAllowedForProject_RejectsAncestorBeyondThirdParent()
        {
            var outerRoot = Path.Combine(_tempDirectory, "outer");
            var workspaceRoot = Path.Combine(outerRoot, "workspace");
            var level1 = Path.Combine(workspaceRoot, "a");
            var level2 = Path.Combine(level1, "b");
            var projectRoot = Path.Combine(level2, "RuntimeCallSample");
            Directory.CreateDirectory(projectRoot);

            Assert.That(McpPathResolver.IsWorkspaceRootAllowedForProject(projectRoot, outerRoot), Is.False);
        }

        private string CreateExecutable(string fileName)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, "stub");
            return filePath;
        }

        private static void CreatePackageToolsRoot(string packageRoot)
        {
            var toolsRoot = Path.Combine(packageRoot, "Tools~", "UnityAgentBridge");
            Directory.CreateDirectory(Path.Combine(toolsRoot, "runtime-build"));
            Directory.CreateDirectory(Path.Combine(toolsRoot, "src", "UnityAgentBridge.Cli"));
            Directory.CreateDirectory(Path.Combine(toolsRoot, "src", "UnityAgentBridge.RoslynCompiler"));
            File.WriteAllText(Path.Combine(packageRoot, "package.json"), "{}");
            File.WriteAllText(Path.Combine(toolsRoot, "runtime-build", "Build-LocalRuntime.ps1"), "param()");
            File.WriteAllText(Path.Combine(toolsRoot, "src", "UnityAgentBridge.Cli", "UnityAgentBridge.Cli.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(toolsRoot, "src", "UnityAgentBridge.RoslynCompiler", "UnityAgentBridge.RoslynCompiler.csproj"), "<Project />");
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }
    }
}
