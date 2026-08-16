using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityMcp.AgentBridge;
using UnityMcp.AgentBridge.Mcp;

namespace UnityMcp.AgentBridge.Tests.Mcp
{
    public sealed class MachineRuntimeSelectionTests
    {
        private string _tempDirectory;

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
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_201.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_201")]
        public void MachineSelection_RoundTripsExactVersionAndChannel()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            store.Save(new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "1.2.3",
                RuntimeChannel = "preview",
                MachineRuntimeRoot = " D:/AgentBridge "
            });

            var loaded = store.Load();
            Assert.That(loaded.RuntimeMode, Is.EqualTo("machine"));
            Assert.That(loaded.RuntimeVersion, Is.EqualTo("1.2.3"));
            Assert.That(loaded.RuntimeChannel, Is.EqualTo("preview"));
            Assert.That(loaded.MachineRuntimeRoot, Is.EqualTo("D:/AgentBridge"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_202.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_202")]
        public void MachineSelection_InvalidChannelFallsBackToExactOnly()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            store.Save(new McpEditorSettings { RuntimeMode = "machine", RuntimeVersion = "1.2.3", RuntimeChannel = "unsupported" });

            var loaded = store.Load();
            Assert.That(loaded.RuntimeMode, Is.EqualTo("machine"));
            Assert.That(loaded.RuntimeVersion, Is.EqualTo("1.2.3"));
            Assert.That(loaded.RuntimeChannel, Is.Empty);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_203.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_203")]
        public void MachineSelection_RepeatedSaveRemainsSingular()
        {
            var path = Path.Combine(_tempDirectory, "mcp-editor-settings.json");
            var store = new McpEditorSettingsStore(path);
            var settings = new McpEditorSettings { RuntimeMode = "machine", RuntimeVersion = "1.2.3", RuntimeChannel = "stable" };
            store.Save(settings);
            store.Save(settings);

            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"runtimeVersion\": \"1.2.3\""));
            Assert.That(json.Split(new[] { "runtimeVersion" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
        }

        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_204")]
        public void MachineSelection_FlatReleaseRuntimeResolvesExecutableAndLauncher()
        {
            var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");
            var versionRoot = Path.Combine(managerRoot, "versions", "1.2.3");
            var runtimeExecutable = Path.Combine(versionRoot, "runtime", "win-x64", "unity-agent-bridge.exe");
            var launcher = Path.Combine(versionRoot, "launcher", "Start-UnityAgentBridge-Mcp.cmd");
            var stableLauncher = Path.Combine(managerRoot, "bin", "agent-bridge-mcp.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeExecutable));
            Directory.CreateDirectory(Path.GetDirectoryName(launcher));
            Directory.CreateDirectory(Path.GetDirectoryName(stableLauncher));
            File.WriteAllText(runtimeExecutable, "runtime");
            File.WriteAllText(launcher, "launcher");
            File.WriteAllText(stableLauncher, "stable launcher");
            File.WriteAllText(Path.Combine(versionRoot, "release-manifest.json"), "{\"version\":\"1.2.3\"}");

            var settings = new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "1.2.3",
                MachineRuntimeRoot = managerRoot,
            };
            var locator = new MachineRuntimeLocator();

            Assert.That(locator.ResolveRuntimeExecutablePath(settings), Is.EqualTo(Path.GetFullPath(runtimeExecutable)));
            Assert.That(locator.ResolveLauncherPath(settings), Is.EqualTo(Path.GetFullPath(launcher)));
            Assert.That(locator.ResolveRuntimeExecutablePath(new McpEditorSettings
            {
                RuntimeMode = "machine",
                RuntimeVersion = "../escape",
                MachineRuntimeRoot = managerRoot,
            }), Is.Empty);
        }

        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_205")]
        public void MachineSelection_ListsOnlyUsableInstalledVersionsInDescendingOrder()
        {
            var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");
            CreateMachineRuntimeVersion(managerRoot, "1.2.9");
            CreateMachineRuntimeVersion(managerRoot, "1.2.10-rc.1");
            CreateMachineRuntimeVersion(managerRoot, "1.2.10");
            CreateMachineRuntimeVersion(managerRoot, "1.2.11", manifestVersion: "1.2.10");
            Directory.CreateDirectory(Path.Combine(managerRoot, "versions", "1.2.12"));

            var versions = new MachineRuntimeLocator().ListInstalledVersions(new McpEditorSettings
            {
                RuntimeMode = "machine",
                MachineRuntimeRoot = managerRoot,
            });

            Assert.That(versions, Is.EqualTo(new[] { "1.2.10", "1.2.10-rc.1", "1.2.9" }));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_206.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_206")]
        public void MachineSelection_ListsPublishedVersionsWithLocalInstallState()
        {
            var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");
            CreatePublishedMachineRuntimeVersion(managerRoot, "1.2.11", "v1.2.11", "https://example.invalid/1.2.11.zip");
            CreatePublishedMachineRuntimeVersion(managerRoot, "1.2.12-rc.1", "v1.2.12-rc.1", "https://example.invalid/1.2.12-rc.1.zip");
            CreatePublishedMachineRuntimeVersion(managerRoot, "1.2.12-rc.2", "v1.2.12-rc.2", "https://example.invalid/1.2.12-rc.2.zip");
            CreatePublishedMachineRuntimeVersion(managerRoot, "1.2.12", "v1.2.12", "https://example.invalid/1.2.12.zip");
            CreatePublishedMachineRuntimeVersion(managerRoot, "1.2.13", "v1.2.13", "https://example.invalid/1.2.13.zip", "1.2.12");
            CreateMachineRuntimeVersion(managerRoot, "1.2.12-rc.2");

            var versions = new MachineRuntimeLocator().ListPublishedVersions(new McpEditorSettings
            {
                RuntimeMode = "machine",
                MachineRuntimeRoot = managerRoot,
            });

            Assert.That(versions.Count, Is.EqualTo(3));
            Assert.That(versions[0].Version, Is.EqualTo("1.2.12"));
            Assert.That(versions[0].IsInstalled, Is.False);
            Assert.That(versions[1].Version, Is.EqualTo("1.2.12-rc.2"));
            Assert.That(versions[1].Tag, Is.EqualTo("v1.2.12-rc.2"));
            Assert.That(versions[1].ArtifactUrl, Is.EqualTo("https://example.invalid/1.2.12-rc.2.zip"));
            Assert.That(versions[1].IsInstalled, Is.True);
            Assert.That(versions[2].Version, Is.EqualTo("1.2.12-rc.1"));
            Assert.That(versions[2].IsInstalled, Is.False);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_208.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_208")]
        public void MachineSelection_BuiltInCatalogListsPublishedTagsForEmptyManagerRoot()
        {
            var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");

            var versions = MachineRuntimeLocator.CreateBuiltInPublishedVersions(managerRoot);

            Assert.That(versions.Count, Is.EqualTo(1));
            Assert.That(versions[0].Version, Is.EqualTo("1.2.12-rc.1"));
            Assert.That(versions[0].Tag, Is.EqualTo("v1.2.12-rc.1"));
            Assert.That(versions[0].IsInstalled, Is.False);
            Assert.That(versions[0].ArtifactUrl, Does.EndWith("/v1.2.12-rc.1/unity-agent-bridge-1.2.12-rc.1-win-x64.zip"));
            Assert.That(versions[0].SourceArchiveUrl, Does.EndWith("/archive/refs/tags/v1.2.12-rc.1.zip"));
            Assert.That(versions[0].CommitSha, Is.EqualTo("af3638f0a992835293d3bb88aa6c1bd9842c1338"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_209.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_209")]
        public void MachineSelection_MissingDefaultReleasesDirectoryUsesPublishedCatalog()
        {
            var previousManagerRoot = Environment.GetEnvironmentVariable("UNITY_AGENT_BRIDGE_HOME");
            try
            {
                var managerRoot = Path.Combine(_tempDirectory, "UnityAgentBridge");
                Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_HOME", managerRoot);

                var versions = new MachineRuntimeLocator().ListPublishedVersions(new McpEditorSettings
                {
                    RuntimeMode = "machine",
                });

                Assert.That(Directory.Exists(Path.Combine(managerRoot, "releases")), Is.False);
                Assert.That(versions.Count, Is.EqualTo(1));
                Assert.That(versions[0].Tag, Is.EqualTo("v1.2.12-rc.1"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UNITY_AGENT_BRIDGE_HOME", previousManagerRoot);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_210.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_210")]
        public void MachineRuntimeDownload_DownloadsChecksumAndInvokesVerifiedManagerInstall()
        {
            var artifactClient = new FakeArtifactClient
            {
                Checksum = new string('a', 64) + "  runtime.zip",
                ArtifactVersion = "1.2.12-rc.1",
            };
            var processRunner = new RecordingProcessRunner();
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                Path.Combine(_tempDirectory, "Temp", "AgentBridge"));

            var result = downloader.DownloadAndInstallAsync(
                new PublishedMachineRuntimeVersion
                {
                    Version = "1.2.12-rc.1",
                    Tag = "v1.2.12-rc.1",
                    ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                },
                new McpEditorSettings { RuntimeMode = "machine" },
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(artifactClient.TextUrl, Does.EndWith(".zip.sha256"));
            Assert.That(artifactClient.FileUrl, Does.EndWith(".zip"));
            Assert.That(processRunner.Request, Is.Not.Null);
            Assert.That(processRunner.Request.Arguments, Does.Contain("install"));
            Assert.That(processRunner.Request.Arguments, Does.Contain(new string('a', 64)));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_211.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_211")]
        public void MachineRuntimeDownload_InvalidBinaryChecksumBuildsFromTagSource()
        {
            var artifactClient = new FakeArtifactClient
            {
                Checksum = "not-a-checksum",
            };
            var processRunner = new RecordingProcessRunner { SourceBuildVersion = "1.2.12-rc.1" };
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                Path.Combine(_tempDirectory, "Temp", "AgentBridge"));

            var result = downloader.DownloadAndInstallAsync(
                new PublishedMachineRuntimeVersion
                {
                    Version = "1.2.12-rc.1",
                    Tag = "v1.2.12-rc.1",
                    ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                    SourceArchiveUrl = "https://example.invalid/archive/refs/tags/v1.2.12-rc.1.zip",
                    CommitSha = "af3638f0a992835293d3bb88aa6c1bd9842c1338",
                },
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Summary, Does.Contain("built from tag source"));
            Assert.That(artifactClient.FileUrls, Is.EqualTo(new[] { "https://example.invalid/archive/refs/tags/v1.2.12-rc.1.zip" }));
            Assert.That(processRunner.Requests.Count, Is.EqualTo(2));
            Assert.That(processRunner.Requests[0].Arguments, Does.Contain("-SourceArchivePath"));
            Assert.That(processRunner.Requests[1].Arguments, Does.Contain("install"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_212.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_212")]
        public void MachineRuntimeDownload_ReportsBinaryAndSourceTransferFailureWithoutInstalling()
        {
            var artifactClient = new FakeArtifactClient
            {
                Checksum = new string('b', 64),
                ArtifactVersion = "1.2.12-rc.1",
                DownloadException = new HttpRequestException("asset unavailable"),
            };
            var processRunner = new RecordingProcessRunner();
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                Path.Combine(_tempDirectory, "Temp", "AgentBridge"));

            var result = downloader.DownloadAndInstallAsync(
                new PublishedMachineRuntimeVersion
                {
                    Version = "1.2.12-rc.1",
                    Tag = "v1.2.12-rc.1",
                    ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                    SourceArchiveUrl = "https://example.invalid/archive/refs/tags/v1.2.12-rc.1.zip",
                    CommitSha = "af3638f0a992835293d3bb88aa6c1bd9842c1338",
                },
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("source_download_failed"));
            Assert.That(result.Summary, Does.Contain("asset unavailable"));
            Assert.That(processRunner.Request, Is.Null);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_215.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_215")]
        public void MachineRuntimeDownload_ReusesCachedTagSourceWithoutSourceTransfer()
        {
            var cacheRoot = Path.Combine(_tempDirectory, "Temp", "AgentBridge");
            var sourceRoot = Path.Combine(cacheRoot, "1.2.12-rc.1", "source");
            Directory.CreateDirectory(sourceRoot);
            CreateRuntimeArchive(Path.Combine(sourceRoot, "unity-agent-bridge-1.2.12-rc.1-source.zip"), "1.2.12-rc.1");
            var artifactClient = new FakeArtifactClient { Checksum = "not-a-checksum" };
            var processRunner = new RecordingProcessRunner { SourceBuildVersion = "1.2.12-rc.1" };
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                cacheRoot);

            var result = downloader.DownloadAndInstallAsync(
                CreateSourceBuildRelease(),
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(artifactClient.FileUrls, Is.Empty);
            Assert.That(processRunner.Requests.Count, Is.EqualTo(2));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_216.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_216")]
        public void MachineRuntimeDownload_SourceBuildFailureDoesNotInvokeManagerInstall()
        {
            var cacheRoot = Path.Combine(_tempDirectory, "Temp", "AgentBridge");
            var sourceRoot = Path.Combine(cacheRoot, "1.2.12-rc.1", "source");
            Directory.CreateDirectory(sourceRoot);
            CreateRuntimeArchive(Path.Combine(sourceRoot, "unity-agent-bridge-1.2.12-rc.1-source.zip"), "1.2.12-rc.1");
            var artifactClient = new FakeArtifactClient { Checksum = "not-a-checksum" };
            var processRunner = new RecordingProcessRunner { SourceBuildFailure = "compile failed" };
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                cacheRoot);

            var result = downloader.DownloadAndInstallAsync(
                CreateSourceBuildRelease(),
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("source_build_failed"));
            Assert.That(result.Summary, Does.Contain("compile failed"));
            Assert.That(processRunner.Requests.Count, Is.EqualTo(1));
            Assert.That(processRunner.Requests[0].Arguments, Does.Not.Contain("install"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_213.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_213")]
        public void MachineRuntimeDownload_UsesMatchingProjectCacheWithoutNetwork()
        {
            var cacheRoot = Path.Combine(_tempDirectory, "Temp", "AgentBridge");
            var versionRoot = Path.Combine(cacheRoot, "1.2.12-rc.1");
            var archivePath = Path.Combine(versionRoot, "offline-runtime.zip");
            Directory.CreateDirectory(versionRoot);
            CreateRuntimeArchive(archivePath, "1.2.12-rc.1");
            var artifactClient = new FakeArtifactClient { DownloadException = new HttpRequestException("network must not be used") };
            var processRunner = new RecordingProcessRunner();
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                cacheRoot);

            var result = downloader.DownloadAndInstallAsync(
                new PublishedMachineRuntimeVersion
                {
                    Version = "1.2.12-rc.1",
                    ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                },
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CachePath, Is.EqualTo(archivePath));
            Assert.That(result.Summary, Does.Contain("offline cache"));
            Assert.That(artifactClient.TextUrl, Is.Empty);
            Assert.That(artifactClient.FileUrl, Is.Empty);
            Assert.That(processRunner.Request.Arguments, Does.Contain(archivePath));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_214.md
        [Test]
        [Category("AGBM_RuntimeSelection")]
        [Category("AGBM_214")]
        public void MachineRuntimeDownload_RejectsMismatchedProjectCacheWithoutOverwriteOrNetwork()
        {
            var cacheRoot = Path.Combine(_tempDirectory, "Temp", "AgentBridge");
            var versionRoot = Path.Combine(cacheRoot, "1.2.12-rc.1");
            var archivePath = Path.Combine(versionRoot, "offline-runtime.zip");
            Directory.CreateDirectory(versionRoot);
            CreateRuntimeArchive(archivePath, "1.2.12-rc.2");
            var artifactClient = new FakeArtifactClient();
            var processRunner = new RecordingProcessRunner();
            var downloader = new MachineRuntimeDownloader(
                artifactClient,
                processRunner,
                new McpPathResolver(),
                TimeSpan.FromSeconds(5),
                cacheRoot);

            var result = downloader.DownloadAndInstallAsync(
                new PublishedMachineRuntimeVersion
                {
                    Version = "1.2.12-rc.1",
                    ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                },
                new McpEditorSettings(),
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Reason, Is.EqualTo("cached_artifact_version_mismatch"));
            Assert.That(File.Exists(archivePath), Is.True);
            Assert.That(artifactClient.TextUrl, Is.Empty);
            Assert.That(artifactClient.FileUrl, Is.Empty);
            Assert.That(processRunner.Request, Is.Null);
        }

        private static void CreateMachineRuntimeVersion(string managerRoot, string version, string manifestVersion = null)
        {
            var versionRoot = Path.Combine(managerRoot, "versions", version);
            var executablePath = Path.Combine(versionRoot, "runtime", "win-x64", "unity-agent-bridge.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
            File.WriteAllText(executablePath, "runtime");
            File.WriteAllText(Path.Combine(versionRoot, "release-manifest.json"), "{\"version\":\"" + (manifestVersion ?? version) + "\"}");
        }

        private static void CreatePublishedMachineRuntimeVersion(
            string managerRoot,
            string version,
            string tag,
            string artifactUrl,
            string manifestVersion = null)
        {
            var releaseRoot = Path.Combine(managerRoot, "releases", version);
            Directory.CreateDirectory(releaseRoot);
            File.WriteAllText(
                Path.Combine(releaseRoot, "release-manifest.json"),
                "{\"version\":\"" + (manifestVersion ?? version) + "\",\"tag\":\"" + tag + "\",\"artifactUrl\":\"" + artifactUrl + "\"}");
        }

        private static void CreateRuntimeArchive(string archivePath, string version)
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("release-manifest.json");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write("{\"version\":\"" + version + "\"}");
                }
            }
        }

        private static PublishedMachineRuntimeVersion CreateSourceBuildRelease()
        {
            return new PublishedMachineRuntimeVersion
            {
                Version = "1.2.12-rc.1",
                Tag = "v1.2.12-rc.1",
                ArtifactUrl = "https://example.invalid/unity-agent-bridge-1.2.12-rc.1-win-x64.zip",
                SourceArchiveUrl = "https://example.invalid/archive/refs/tags/v1.2.12-rc.1.zip",
                CommitSha = "af3638f0a992835293d3bb88aa6c1bd9842c1338",
            };
        }

        private sealed class FakeArtifactClient : IMachineRuntimeArtifactClient
        {
            public string Checksum { get; set; } = string.Empty;
            public string ArtifactVersion { get; set; } = "1.2.12-rc.1";
            public string TextUrl { get; private set; } = string.Empty;
            public string FileUrl { get; private set; } = string.Empty;
            public List<string> FileUrls { get; } = new List<string>();
            public Exception DownloadException { get; set; }

            public Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
            {
                TextUrl = url;
                return Task.FromResult(Checksum);
            }

            public Task DownloadFileAsync(
                string url,
                string destinationPath,
                IProgress<MachineRuntimeDownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                FileUrl = url;
                FileUrls.Add(url);
                if (DownloadException != null)
                {
                    throw DownloadException;
                }

                CreateRuntimeArchive(destinationPath, ArtifactVersion);
                progress?.Report(MachineRuntimeDownloadProgress.Downloading(new FileInfo(destinationPath).Length, new FileInfo(destinationPath).Length));
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingProcessRunner : IAsyncProcessRunner
        {
            public ProcessExecutionRequest Request { get; private set; }
            public List<ProcessExecutionRequest> Requests { get; } = new List<ProcessExecutionRequest>();
            public string SourceBuildVersion { get; set; } = string.Empty;
            public string SourceBuildFailure { get; set; } = string.Empty;

            public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                Requests.Add(request);
                var outputIndex = -1;
                for (var index = 0; index < request.Arguments.Count; index++)
                {
                    if (string.Equals(request.Arguments[index], "-OutputArchivePath", StringComparison.Ordinal))
                    {
                        outputIndex = index;
                        break;
                    }
                }
                if (outputIndex >= 0)
                {
                    if (!string.IsNullOrWhiteSpace(SourceBuildFailure))
                    {
                        return Task.FromResult(new ProcessExecutionResult
                        {
                            Outcome = ProcessOutcome.Completed,
                            ExitCode = 1,
                            Stderr = SourceBuildFailure,
                        });
                    }

                    if (outputIndex + 1 < request.Arguments.Count && !string.IsNullOrWhiteSpace(SourceBuildVersion))
                    {
                        CreateRuntimeArchive(request.Arguments[outputIndex + 1], SourceBuildVersion);
                    }
                }

                return Task.FromResult(new ProcessExecutionResult
                {
                    Outcome = ProcessOutcome.Completed,
                    ExitCode = 0,
                });
            }
        }
    }
}
