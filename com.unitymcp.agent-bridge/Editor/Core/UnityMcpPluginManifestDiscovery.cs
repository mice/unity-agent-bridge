using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge
{
    internal static class UnityMcpPluginDiagnosticCodes
    {
        public const string ManifestInvalid = "UNITYMCP_PLUGIN_MANIFEST_INVALID";
        public const string SchemaUnsupported = "UNITYMCP_PLUGIN_SCHEMA_UNSUPPORTED";
        public const string IdentityInvalid = "UNITYMCP_PLUGIN_IDENTITY_INVALID";
        public const string DuplicateId = "UNITYMCP_PLUGIN_ID_DUPLICATE";
        public const string CompatibilityFailed = "UNITYMCP_PLUGIN_INCOMPATIBLE";
        public const string PathInvalid = "UNITYMCP_PLUGIN_PATH_INVALID";
        public const string EntryMissing = "UNITYMCP_PLUGIN_ENTRY_MISSING";
        public const string AssemblyUnavailable = "UNITYMCP_PLUGIN_ASSEMBLY_UNAVAILABLE";
        public const string ProviderInvalid = "UNITYMCP_PLUGIN_PROVIDER_INVALID";
        public const string PayloadUnavailable = "UNITYMCP_PLUGIN_PAYLOAD_UNAVAILABLE";
        public const string PayloadHashMismatch = "UNITYMCP_PLUGIN_PAYLOAD_HASH_MISMATCH";
    }

    [Serializable]
    internal sealed class UnityMcpPackagePluginManifest
    {
        public int schemaVersion;
        public string pluginId;
        public string displayName;
        public string version;
        public bool defaultEnabled;
        public UnityMcpPackagePluginCompatibility compatibility;
        public UnityMcpPackagePluginEntry entry;
        public List<UnityMcpPackagePluginPayload> payloads = new List<UnityMcpPackagePluginPayload>();
    }

    [Serializable]
    internal sealed class UnityMcpPackagePluginCompatibility
    {
        public string unity;
        public string pluginAbstractions;
        public string agentBridge;
    }

    [Serializable]
    internal sealed class UnityMcpPackagePluginEntry
    {
        public string kind;
        public string dllPath;
        public string assemblyName;
        public string providerType;
    }

    [Serializable]
    internal sealed class UnityMcpPackagePluginPayload
    {
        public string id;
        public string rid;
        public string path;
        public string sha256;
        public bool required = true;
    }

    public sealed class UnityMcpInstalledPlugin
    {
        public string PluginId { get; internal set; }
        public string DisplayName { get; internal set; }
        public string Version { get; internal set; }
        public string PackageName { get; internal set; }
        public string PackageRoot { get; internal set; }
        public bool Installed { get; internal set; }
        public bool Enabled { get; internal set; }
        public bool Ready { get; internal set; }
        public bool Exposed { get; internal set; }
        public string DiagnosticCode { get; internal set; }
        public string DiagnosticMessage { get; internal set; }
        internal Assembly Assembly { get; set; }
        internal Type ProviderType { get; set; }
        internal IReadOnlyList<UnityMcpPluginPayload> Payloads { get; set; } = Array.Empty<UnityMcpPluginPayload>();
    }

    internal sealed class UnityMcpResolvedPackage
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string ResolvedPath { get; set; }
    }

    internal static class UnityMcpPluginManifestDiscovery
    {
        internal static Func<IReadOnlyList<UnityMcpResolvedPackage>> PackageResolverOverride;

        public static IReadOnlyList<UnityMcpInstalledPlugin> Discover(AgentBridgeSettings settings)
        {
            var states = settings?.externalPluginStates ?? new List<UnityMcpExternalPluginState>();
            var enabledById = states
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.pluginId))
                .GroupBy(item => item.pluginId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().enabled, StringComparer.Ordinal);
            var candidates = new List<UnityMcpInstalledPlugin>();

            foreach (var package in ResolvePackages().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var manifestPath = Path.Combine(package.ResolvedPath ?? string.Empty, "unitymcp-plugin.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                candidates.Add(ResolveCandidate(package, manifestPath, enabledById));
            }

            foreach (var duplicate in candidates
                         .Where(item => !string.IsNullOrWhiteSpace(item.PluginId))
                         .GroupBy(item => item.PluginId, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                foreach (var candidate in duplicate)
                {
                    Fail(candidate, UnityMcpPluginDiagnosticCodes.DuplicateId, $"Plugin id '{duplicate.Key}' is declared by multiple resolved packages.");
                }
            }

            return candidates;
        }

        private static UnityMcpInstalledPlugin ResolveCandidate(
            UnityMcpResolvedPackage package,
            string manifestPath,
            IReadOnlyDictionary<string, bool> enabledById)
        {
            var candidate = new UnityMcpInstalledPlugin
            {
                PackageName = package.Name ?? string.Empty,
                PackageRoot = Path.GetFullPath(package.ResolvedPath ?? string.Empty),
                Installed = true,
                DisplayName = package.Name ?? "Invalid UnityMCP plugin",
                Version = package.Version ?? string.Empty
            };

            UnityMcpPackagePluginManifest manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<UnityMcpPackagePluginManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception exception)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.ManifestInvalid, exception.Message);
            }

            if (manifest == null)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.ManifestInvalid, "Manifest is empty.");
            }

            candidate.PluginId = manifest.pluginId ?? string.Empty;
            candidate.DisplayName = string.IsNullOrWhiteSpace(manifest.displayName) ? candidate.PluginId : manifest.displayName;
            candidate.Version = manifest.version ?? string.Empty;
            candidate.Enabled = enabledById.TryGetValue(candidate.PluginId, out var enabled) && enabled;

            if (manifest.schemaVersion != 1)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.SchemaUnsupported, $"Unsupported manifest schema version '{manifest.schemaVersion}'.");
            }

            if (!IsPluginId(manifest.pluginId) || string.IsNullOrWhiteSpace(manifest.version))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.IdentityInvalid, "pluginId and version are required, and pluginId must use reverse-domain segments.");
            }

            if (!IsCompatible(manifest.compatibility, out var compatibilityFailure))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.CompatibilityFailed, compatibilityFailure);
            }

            if (manifest.entry == null ||
                !string.Equals(manifest.entry.kind, "managedDll", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.entry.dllPath) ||
                string.IsNullOrWhiteSpace(manifest.entry.assemblyName) ||
                string.IsNullOrWhiteSpace(manifest.entry.providerType))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.EntryMissing, "A managedDll entry with dllPath, assemblyName, and providerType is required.");
            }

            if (!TryResolvePackagePath(candidate.PackageRoot, manifest.entry.dllPath, out var dllPath))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.PathInvalid, "Managed DLL path must remain inside the package root.");
            }

            if (!File.Exists(dllPath))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.EntryMissing, $"Managed DLL was not found: {manifest.entry.dllPath}");
            }

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(item => string.Equals(item.GetName().Name, manifest.entry.assemblyName, StringComparison.Ordinal));
            if (assembly == null)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.AssemblyUnavailable, $"Unity has not loaded assembly '{manifest.entry.assemblyName}'.");
            }

            var providerType = assembly.GetType(manifest.entry.providerType, false, false);
            if (providerType == null || providerType.IsAbstract || !typeof(IUnityMcpToolProvider).IsAssignableFrom(providerType))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.ProviderInvalid, $"Provider '{manifest.entry.providerType}' is not a concrete IUnityMcpToolProvider.");
            }

            var attribute = providerType.GetCustomAttribute<UnityMcpPluginAttribute>();
            if (attribute == null ||
                !string.Equals(attribute.PluginId, manifest.pluginId, StringComparison.Ordinal) ||
                !string.Equals(attribute.PluginVersion, manifest.version, StringComparison.Ordinal))
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.ProviderInvalid, "Provider attribute identity does not match the package manifest.");
            }

            var resolvedPayloads = new List<UnityMcpPluginPayload>();
            foreach (var payload in manifest.payloads ?? new List<UnityMcpPackagePluginPayload>())
            {
                if (!string.Equals(payload.rid, CurrentRid, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryResolvePackagePath(candidate.PackageRoot, payload.path, out var payloadPath))
                {
                    return Fail(candidate, UnityMcpPluginDiagnosticCodes.PathInvalid, $"Payload '{payload.id}' path must remain inside the package root.");
                }

                if (!File.Exists(payloadPath))
                {
                    return Fail(candidate, UnityMcpPluginDiagnosticCodes.PayloadUnavailable, $"Payload '{payload.id}' was not found for {CurrentRid}.");
                }

                if (!string.IsNullOrWhiteSpace(payload.sha256) && !HashMatches(payloadPath, payload.sha256))
                {
                    return Fail(candidate, UnityMcpPluginDiagnosticCodes.PayloadHashMismatch, $"Payload '{payload.id}' failed SHA-256 validation.");
                }

                resolvedPayloads.Add(new UnityMcpPluginPayload
                {
                    Id = payload.id,
                    Rid = payload.rid,
                    Path = payloadPath,
                    Sha256 = payload.sha256
                });
            }

            if ((manifest.payloads ?? new List<UnityMcpPackagePluginPayload>()).Any(item => item.required) && resolvedPayloads.Count == 0)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.PayloadUnavailable, $"No required payload is available for {CurrentRid}.");
            }

            var context = new UnityMcpPluginContext
            {
                PluginId = manifest.pluginId,
                PluginVersion = manifest.version,
                PluginRoot = candidate.PackageRoot,
                Payloads = resolvedPayloads
            };
            try
            {
                UnityMcpPluginContractValidator.ValidatePluginContext(context);
            }
            catch (Exception exception)
            {
                return Fail(candidate, UnityMcpPluginDiagnosticCodes.PayloadUnavailable, exception.Message);
            }

            candidate.Assembly = assembly;
            candidate.ProviderType = providerType;
            candidate.Payloads = resolvedPayloads;
            candidate.Ready = true;
            return candidate;
        }

        private static IReadOnlyList<UnityMcpResolvedPackage> ResolvePackages()
        {
            if (PackageResolverOverride != null)
            {
                return PackageResolverOverride() ?? Array.Empty<UnityMcpResolvedPackage>();
            }

            return PackageInfo.GetAllRegisteredPackages()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.resolvedPath))
                .Select(item => new UnityMcpResolvedPackage
                {
                    Name = item.name,
                    Version = item.version,
                    ResolvedPath = item.resolvedPath
                })
                .ToArray();
        }

        private static string CurrentRid => Application.platform == RuntimePlatform.WindowsEditor && Environment.Is64BitProcess
            ? "win-x64"
            : "unsupported";

        private static bool IsCompatible(UnityMcpPackagePluginCompatibility compatibility, out string failure)
        {
            failure = null;
            if (compatibility == null)
            {
                return true;
            }

            var bridge = PackageInfo.FindForAssembly(typeof(UnityMcpPluginManifestDiscovery).Assembly)?.version;
            var abstractions = PackageInfo.FindForAssembly(typeof(IUnityMcpToolProvider).Assembly)?.version;
            if (!MeetsMinimum(Application.unityVersion, compatibility.unity) ||
                !MeetsMinimum(bridge, compatibility.agentBridge) ||
                !MeetsMinimum(abstractions, compatibility.pluginAbstractions))
            {
                failure = $"Compatibility requirement failed. unity>={compatibility.unity}, agentBridge>={compatibility.agentBridge}, pluginAbstractions>={compatibility.pluginAbstractions}.";
                return false;
            }

            return true;
        }

        private static bool MeetsMinimum(string actual, string minimum)
        {
            if (string.IsNullOrWhiteSpace(minimum))
            {
                return true;
            }

            return ParseVersion(actual).CompareTo(ParseVersion(minimum)) >= 0;
        }

        private static Version ParseVersion(string value)
        {
            var parts = (value ?? string.Empty).Split('.');
            var numbers = new int[3];
            for (var index = 0; index < numbers.Length && index < parts.Length; index++)
            {
                var digits = new string(parts[index].TakeWhile(char.IsDigit).ToArray());
                int.TryParse(digits, out numbers[index]);
            }

            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        private static bool TryResolvePackagePath(string packageRoot, string relativePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(packageRoot);
            var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(normalizedRoot, candidate);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private static bool HashMatches(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsPluginId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Contains(".") &&
                   value.Split('.').All(segment => segment.Length > 0 && segment.All(character => char.IsLetterOrDigit(character) || character == '-'));
        }

        private static UnityMcpInstalledPlugin Fail(UnityMcpInstalledPlugin candidate, string code, string message)
        {
            candidate.Ready = false;
            candidate.Exposed = false;
            candidate.DiagnosticCode = code;
            candidate.DiagnosticMessage = message;
            return candidate;
        }
    }
}
