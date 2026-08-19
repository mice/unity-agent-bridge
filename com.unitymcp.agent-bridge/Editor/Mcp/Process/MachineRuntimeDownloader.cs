using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    internal interface IMachineRuntimeArtifactClient
    {
        Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken);

        Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<MachineRuntimeDownloadProgress> progress,
            CancellationToken cancellationToken);
    }

    internal sealed class HttpMachineRuntimeArtifactClient : IMachineRuntimeArtifactClient
    {
        private static readonly HttpClient Client = new HttpClient();

        public async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
        {
            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        public async Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<MachineRuntimeDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(-1L);
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long receivedBytes = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        receivedBytes += read;
                        progress?.Report(MachineRuntimeDownloadProgress.Downloading(receivedBytes, totalBytes));
                    }
                }
            }
        }
    }

    internal sealed class MachineRuntimeDownloader
    {
        private readonly IMachineRuntimeArtifactClient _artifactClient;
        private readonly IAsyncProcessRunner _processRunner;
        private readonly McpPathResolver _pathResolver;
        private readonly TimeSpan _installTimeout;
        private readonly string _downloadCacheRootOverride;

        public MachineRuntimeDownloader()
            : this(new HttpMachineRuntimeArtifactClient(), new AsyncProcessRunner(), new McpPathResolver(), TimeSpan.FromMinutes(10), null)
        {
        }

        internal MachineRuntimeDownloader(
            IMachineRuntimeArtifactClient artifactClient,
            IAsyncProcessRunner processRunner,
            McpPathResolver pathResolver,
            TimeSpan installTimeout,
            string downloadCacheRootOverride = null)
        {
            _artifactClient = artifactClient ?? throw new ArgumentNullException(nameof(artifactClient));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _installTimeout = installTimeout > TimeSpan.Zero ? installTimeout : TimeSpan.FromMinutes(10);
            _downloadCacheRootOverride = downloadCacheRootOverride;
        }

        public async Task<MachineRuntimeDownloadResult> DownloadAndInstallAsync(
            PublishedMachineRuntimeVersion release,
            McpEditorSettings settings,
            IProgress<MachineRuntimeDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.Version))
            {
                return MachineRuntimeDownloadResult.Fail("release_missing", "Select a published runtime version first.");
            }

            var hasBinaryArtifact = !string.IsNullOrWhiteSpace(release.ArtifactUrl);
            Uri artifactUri = null;
            if (hasBinaryArtifact && !TryValidateArtifactUrl(release.ArtifactUrl, out artifactUri, out var urlFailure))
            {
                return MachineRuntimeDownloadResult.Fail("artifact_url_invalid", urlFailure);
            }

            var cacheRoot = ResolveDownloadCacheRoot(release.Version);
            var archiveName = hasBinaryArtifact
                ? Path.GetFileName(artifactUri.LocalPath)
                : "unity-agent-bridge-" + release.Version + "-win-x64.zip";
            var cachedArchive = ResolveCachedArchive(cacheRoot, archiveName);
            var partialArchive = string.Empty;
            var installedFromCache = !string.IsNullOrWhiteSpace(cachedArchive);
            var builtFromSource = false;
            try
            {
                string expectedSha256;
                if (installedFromCache)
                {
                    progress?.Report(MachineRuntimeDownloadProgress.Stage("Checking offline cache", 0.08f));
                    var cachedVersion = ReadCachedArtifactVersion(cachedArchive);
                    if (!string.Equals(cachedVersion, release.Version, StringComparison.Ordinal))
                    {
                        return MachineRuntimeDownloadResult.Fail(
                            "cached_artifact_version_mismatch",
                            "Cached runtime archive does not match selected version " + release.Version + ": " + cachedArchive);
                    }

                    expectedSha256 = ComputeSha256(cachedArchive);
                }
                else
                {
                    expectedSha256 = string.Empty;
                    if (!hasBinaryArtifact)
                    {
                        Directory.CreateDirectory(cacheRoot);
                        progress?.Report(MachineRuntimeDownloadProgress.Stage("Preparing tag source", 0.05f));
                        var sourceBuild = await BuildFromSourceAsync(
                                release,
                                settings,
                                cacheRoot,
                                archiveName,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!sourceBuild.Succeeded)
                        {
                            return MachineRuntimeDownloadResult.Fail(sourceBuild.Reason, sourceBuild.Summary);
                        }

                        cachedArchive = sourceBuild.ArchivePath;
                        expectedSha256 = ComputeSha256(cachedArchive);
                        builtFromSource = true;
                    }
                    else
                    {
                        Directory.CreateDirectory(cacheRoot);
                        var binaryFailure = string.Empty;
                        try
                        {
                            progress?.Report(MachineRuntimeDownloadProgress.Stage("Checking binary release", 0.02f));
                            var checksumText = await _artifactClient
                                .DownloadTextAsync(artifactUri.AbsoluteUri + ".sha256", cancellationToken)
                                .ConfigureAwait(false);
                            expectedSha256 = ParseSha256(checksumText);
                            if (string.IsNullOrWhiteSpace(expectedSha256))
                            {
                                binaryFailure = "The published binary checksum is missing or invalid.";
                            }

                            if (string.IsNullOrWhiteSpace(binaryFailure))
                            {
                                partialArchive = Path.Combine(cacheRoot, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
                                progress?.Report(MachineRuntimeDownloadProgress.Stage("Starting binary download", 0.05f));
                                await _artifactClient
                                    .DownloadFileAsync(artifactUri.AbsoluteUri, partialArchive, progress, cancellationToken)
                                    .ConfigureAwait(false);

                                var downloadedVersion = ReadCachedArtifactVersion(partialArchive);
                                if (!string.Equals(downloadedVersion, release.Version, StringComparison.Ordinal))
                                {
                                    return MachineRuntimeDownloadResult.Fail(
                                        "downloaded_artifact_version_mismatch",
                                        "Downloaded runtime archive does not match selected version " + release.Version + ".");
                                }

                                cachedArchive = Path.Combine(cacheRoot, archiveName);
                                File.Move(partialArchive, cachedArchive);
                                partialArchive = string.Empty;
                            }
                        }
                        catch (HttpRequestException exception)
                        {
                            binaryFailure = exception.Message;
                        }

                        if (!string.IsNullOrWhiteSpace(binaryFailure))
                        {
                            progress?.Report(MachineRuntimeDownloadProgress.Stage("Binary unavailable; preparing tag source", 0.05f));
                            var sourceBuild = await BuildFromSourceAsync(
                                    release,
                                    settings,
                                    cacheRoot,
                                    archiveName,
                                    progress,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (!sourceBuild.Succeeded)
                            {
                                return MachineRuntimeDownloadResult.Fail(
                                    sourceBuild.Reason,
                                    "Binary release unavailable (" + binaryFailure + "). " + sourceBuild.Summary);
                            }

                            cachedArchive = sourceBuild.ArchivePath;
                            expectedSha256 = ComputeSha256(cachedArchive);
                            builtFromSource = true;
                        }
                    }
                }

                progress?.Report(MachineRuntimeDownloadProgress.Stage("Verifying and installing", 0.92f));
                var toolsRoot = _pathResolver.ResolvePackageToolsRoot(settings);
                var managerScript = string.IsNullOrWhiteSpace(toolsRoot)
                    ? string.Empty
                    : Path.Combine(toolsRoot, "UnityAgentBridge", "manager", "AgentBridgeManager.ps1");
                if (!File.Exists(managerScript))
                {
                    return MachineRuntimeDownloadResult.Fail(
                        "manager_missing",
                        "Machine runtime manager is missing: " + managerScript);
                }

                var managerRoot = MachineRuntimeLocator.ResolveDefaultManagerRoot();
                var request = new ProcessExecutionRequest
                {
                    FilePath = "pwsh",
                    Arguments = new[]
                    {
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        managerScript,
                        "-Command",
                        "install",
                        "-ArtifactPath",
                        cachedArchive,
                        "-ArtifactSha256",
                        expectedSha256,
                        "-RuntimeHome",
                        managerRoot,
                        "-Json",
                    },
                    WorkingDirectory = Path.GetDirectoryName(managerScript) ?? toolsRoot,
                    Timeout = _installTimeout,
                    CancellationMode = ProcessCancellationMode.TerminateOnCancel,
                };

                var installResult = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
                if (installResult.Outcome != ProcessOutcome.Completed || installResult.ExitCode.GetValueOrDefault(-1) != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(installResult.Stderr)
                        ? installResult.Stdout
                        : installResult.Stderr;
                    return MachineRuntimeDownloadResult.Fail(
                        "runtime_install_failed",
                        "Runtime installation failed." + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim()));
                }

                progress?.Report(MachineRuntimeDownloadProgress.Stage("Installed", 1f));
                return new MachineRuntimeDownloadResult
                {
                    Succeeded = true,
                    Version = release.Version,
                    CachePath = cachedArchive,
                    Summary = installedFromCache
                        ? "Runtime " + release.Version + " installed from offline cache."
                        : builtFromSource
                            ? "Runtime " + release.Version + " built from tag source and installed."
                            : "Runtime " + release.Version + " downloaded and installed.",
                };
            }
            catch (InvalidDataException exception)
            {
                return MachineRuntimeDownloadResult.Fail("cached_artifact_invalid", "Runtime archive is invalid: " + exception.Message);
            }
            catch (OperationCanceledException)
            {
                return MachineRuntimeDownloadResult.Fail("cancelled", "Runtime download was cancelled.");
            }
            catch (HttpRequestException exception)
            {
                return MachineRuntimeDownloadResult.Fail("download_failed", "Runtime download failed: " + exception.Message);
            }
            catch (IOException exception)
            {
                return MachineRuntimeDownloadResult.Fail("download_io_failed", "Runtime download failed: " + exception.Message);
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(partialArchive) && File.Exists(partialArchive))
                    {
                        File.Delete(partialArchive);
                    }
                }
                catch
                {
                }
            }
        }

        private async Task<SourceBuildResult> BuildFromSourceAsync(
            PublishedMachineRuntimeVersion release,
            McpEditorSettings settings,
            string cacheRoot,
            string outputArchiveName,
            IProgress<MachineRuntimeDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (!TryValidateArtifactUrl(release.SourceArchiveUrl, out var sourceUri, out var sourceUrlFailure))
            {
                return SourceBuildResult.Fail("source_archive_url_invalid", sourceUrlFailure);
            }
            if (string.IsNullOrWhiteSpace(release.CommitSha) ||
                release.CommitSha.Length != 40 ||
                release.CommitSha.Any(character => !Uri.IsHexDigit(character)))
            {
                return SourceBuildResult.Fail(
                    "source_commit_missing",
                    "The published tag catalog does not contain a valid immutable commit SHA.");
            }

            var sourceCacheRoot = Path.Combine(cacheRoot, "source");
            Directory.CreateDirectory(sourceCacheRoot);
            var sourceArchive = Path.Combine(sourceCacheRoot, "unity-agent-bridge-" + release.Version + "-source.zip");
            var partialSourceArchive = string.Empty;
            var partialBuiltArchive = string.Empty;
            var partialBuiltChecksum = string.Empty;
            try
            {
                if (!File.Exists(sourceArchive))
                {
                    partialSourceArchive = Path.Combine(sourceCacheRoot, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
                    progress?.Report(MachineRuntimeDownloadProgress.Stage("Downloading tag source", 0.08f));
                    await _artifactClient
                        .DownloadFileAsync(sourceUri.AbsoluteUri, partialSourceArchive, progress, cancellationToken)
                        .ConfigureAwait(false);
                    File.Move(partialSourceArchive, sourceArchive);
                    partialSourceArchive = string.Empty;
                }
                else
                {
                    progress?.Report(MachineRuntimeDownloadProgress.Stage("Using cached tag source", 0.12f));
                }

                var toolsRoot = _pathResolver.ResolvePackageToolsRoot(settings);
                var buildScript = string.IsNullOrWhiteSpace(toolsRoot)
                    ? string.Empty
                    : Path.Combine(toolsRoot, "UnityAgentBridge", "runtime-build", "Build-MachineRuntimeArtifactFromSource.ps1");
                if (!File.Exists(buildScript))
                {
                    return SourceBuildResult.Fail("source_builder_missing", "Tag source builder is missing: " + buildScript);
                }

                partialBuiltArchive = Path.Combine(cacheRoot, ".source-build-output-" + Guid.NewGuid().ToString("N") + ".zip");
                partialBuiltChecksum = partialBuiltArchive + ".sha256";
                progress?.Report(MachineRuntimeDownloadProgress.Stage("Building runtime from tag source", 0.90f));
                var dotnetPath = settings != null && !string.IsNullOrWhiteSpace(settings.DotnetPath)
                    ? settings.DotnetPath.Trim()
                    : "dotnet";
                var request = new ProcessExecutionRequest
                {
                    FilePath = "pwsh",
                    Arguments = new[]
                    {
                        "-NoProfile",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        buildScript,
                        "-SourceArchivePath",
                        sourceArchive,
                        "-Version",
                        release.Version,
                        "-TagName",
                        release.Tag,
                        "-CommitSha",
                        release.CommitSha,
                        "-OutputArchivePath",
                        partialBuiltArchive,
                        "-UnityProjectPath",
                        _pathResolver.GetProjectRoot(),
                        "-SourceArchiveUrl",
                        sourceUri.AbsoluteUri,
                        "-DotnetPath",
                        dotnetPath,
                    },
                    WorkingDirectory = Path.GetDirectoryName(buildScript) ?? toolsRoot,
                    Timeout = _installTimeout,
                    CancellationMode = ProcessCancellationMode.TerminateOnCancel,
                };

                var buildResult = await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (buildResult.Outcome != ProcessOutcome.Completed || buildResult.ExitCode.GetValueOrDefault(-1) != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(buildResult.Stderr) ? buildResult.Stdout : buildResult.Stderr;
                    return SourceBuildResult.Fail(
                        "source_build_failed",
                        "Tag source build failed." + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim()));
                }
                if (!File.Exists(partialBuiltArchive))
                {
                    return SourceBuildResult.Fail("source_build_output_missing", "Tag source build did not create a runtime ZIP.");
                }

                var builtVersion = ReadCachedArtifactVersion(partialBuiltArchive);
                if (!string.Equals(builtVersion, release.Version, StringComparison.Ordinal))
                {
                    return SourceBuildResult.Fail(
                        "source_build_version_mismatch",
                        "Built runtime archive does not match selected version " + release.Version + ".");
                }

                var outputArchive = Path.Combine(cacheRoot, outputArchiveName);
                File.Move(partialBuiltArchive, outputArchive);
                partialBuiltArchive = string.Empty;
                if (File.Exists(partialBuiltChecksum))
                {
                    File.Delete(partialBuiltChecksum);
                    partialBuiltChecksum = string.Empty;
                    File.WriteAllText(
                        outputArchive + ".sha256",
                        ComputeSha256(outputArchive) + "  " + Path.GetFileName(outputArchive));
                }
                return SourceBuildResult.Success(outputArchive);
            }
            catch (HttpRequestException exception)
            {
                return SourceBuildResult.Fail("source_download_failed", "Tag source download failed: " + exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return SourceBuildResult.Fail("source_archive_invalid", "Tag source or built archive is invalid: " + exception.Message);
            }
            catch (IOException exception)
            {
                return SourceBuildResult.Fail("source_build_io_failed", "Tag source build failed: " + exception.Message);
            }
            finally
            {
                TryDeleteFile(partialSourceArchive);
                TryDeleteFile(partialBuiltArchive);
                TryDeleteFile(partialBuiltChecksum);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        internal static string ParseSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var token = value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (token.Length != 64)
            {
                return string.Empty;
            }

            foreach (var character in token)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return string.Empty;
                }
            }

            return token.ToLowerInvariant();
        }

        private string ResolveDownloadCacheRoot(string version)
        {
            var root = string.IsNullOrWhiteSpace(_downloadCacheRootOverride)
                ? Path.Combine(_pathResolver.GetProjectRoot(), "Temp", "AgentBridge")
                : Path.GetFullPath(_downloadCacheRootOverride);
            return Path.Combine(root, version);
        }

        private static string ResolveCachedArchive(string cacheRoot, string archiveName)
        {
            if (!Directory.Exists(cacheRoot))
            {
                return string.Empty;
            }

            var expectedPath = Path.Combine(cacheRoot, archiveName);
            if (File.Exists(expectedPath))
            {
                return expectedPath;
            }

            return Directory.GetFiles(cacheRoot, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string ReadCachedArtifactVersion(string archivePath)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var manifestEntry = archive.Entries.FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry.FullName), "release-manifest.json", StringComparison.OrdinalIgnoreCase));
                if (manifestEntry == null)
                {
                    throw new InvalidDataException("release-manifest.json is missing from " + archivePath);
                }

                using (var reader = new StreamReader(manifestEntry.Open()))
                {
                    var manifest = JsonUtility.FromJson<CachedReleaseManifest>(reader.ReadToEnd());
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                    {
                        throw new InvalidDataException("release-manifest.json does not contain a version.");
                    }

                    return manifest.version.Trim();
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static bool TryValidateArtifactUrl(string value, out Uri artifactUri, out string failure)
        {
            artifactUri = null;
            failure = string.Empty;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out artifactUri) ||
                (artifactUri.Scheme != Uri.UriSchemeHttps && artifactUri.Scheme != Uri.UriSchemeHttp))
            {
                failure = "The selected release does not provide a valid download URL.";
                return false;
            }

            if (!artifactUri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                failure = "The selected release URL is not a downloadable ZIP artifact.";
                return false;
            }

            return true;
        }

        [Serializable]
        private sealed class CachedReleaseManifest
        {
            public string version;
        }

        private sealed class SourceBuildResult
        {
            public bool Succeeded { get; private set; }
            public string Reason { get; private set; } = string.Empty;
            public string Summary { get; private set; } = string.Empty;
            public string ArchivePath { get; private set; } = string.Empty;

            public static SourceBuildResult Success(string archivePath)
            {
                return new SourceBuildResult { Succeeded = true, ArchivePath = archivePath ?? string.Empty };
            }

            public static SourceBuildResult Fail(string reason, string summary)
            {
                return new SourceBuildResult
                {
                    Reason = reason ?? string.Empty,
                    Summary = summary ?? string.Empty,
                };
            }
        }
    }

    internal sealed class MachineRuntimeDownloadProgress
    {
        public string Status { get; private set; } = string.Empty;
        public float Fraction { get; private set; }
        public long ReceivedBytes { get; private set; }
        public long TotalBytes { get; private set; }

        public static MachineRuntimeDownloadProgress Stage(string status, float fraction)
        {
            return new MachineRuntimeDownloadProgress
            {
                Status = status ?? string.Empty,
                Fraction = Math.Max(0f, Math.Min(1f, fraction)),
            };
        }

        public static MachineRuntimeDownloadProgress Downloading(long receivedBytes, long totalBytes)
        {
            var transferFraction = totalBytes > 0
                ? Math.Max(0d, Math.Min(1d, (double)receivedBytes / totalBytes))
                : 0d;
            return new MachineRuntimeDownloadProgress
            {
                Status = totalBytes > 0 ? "Downloading" : "Downloading " + FormatBytes(receivedBytes),
                Fraction = (float)(0.05d + (transferFraction * 0.85d)),
                ReceivedBytes = receivedBytes,
                TotalBytes = totalBytes,
            };
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.0") + " KB";
            return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
        }
    }

    internal sealed class MachineRuntimeDownloadResult
    {
        public bool Succeeded { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string CachePath { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        public static MachineRuntimeDownloadResult Fail(string reason, string summary)
        {
            return new MachineRuntimeDownloadResult
            {
                Reason = reason ?? string.Empty,
                Summary = summary ?? string.Empty,
            };
        }
    }
}
