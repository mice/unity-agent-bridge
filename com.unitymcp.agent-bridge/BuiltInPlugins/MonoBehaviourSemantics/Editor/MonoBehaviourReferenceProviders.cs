using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    internal interface IMonoBehaviourReferenceProvider
    {
        string ProviderId { get; }
        ReferenceProviderCapabilities Capabilities { get; }
        bool CanHandle(string selection, out string reason);
        MonoBehaviourReferenceResult FindUsages(MonoBehaviourReferenceQuery query);
    }

    internal sealed class GuidTextScanReferenceProvider : IMonoBehaviourReferenceProvider
    {
        public string ProviderId => "guid_text_scan";

        public ReferenceProviderCapabilities Capabilities { get; } = new ReferenceProviderCapabilities
        {
            textMatches = true,
            gameObjectPath = false,
            componentIndex = false,
            serializedFields = false,
            dependencyCache = false
        };

        public bool CanHandle(string selection, out string reason)
        {
            if (string.Equals(selection, "auto", StringComparison.Ordinal) ||
                string.Equals(selection, ProviderId, StringComparison.Ordinal))
            {
                reason = null;
                return true;
            }

            reason = $"Provider '{selection}' is not available in this build.";
            return false;
        }

        public MonoBehaviourReferenceResult FindUsages(MonoBehaviourReferenceQuery query)
        {
            var allMatches = new List<ScriptGuidUsageMatch>();
            var extensions = new HashSet<string>(
                query.AssetTypes.Select(AssetTypeToExtension),
                StringComparer.OrdinalIgnoreCase);

            var assetPaths = query.SearchFolders
                .SelectMany(EnumerateUnityAssetFiles)
                .Where(path => extensions.Contains(Path.GetExtension(path)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            foreach (var assetPath in assetPaths)
            {
                var absolutePath = ToAbsoluteProjectPath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                ScanFile(assetPath, absolutePath, query.Script.guid, allMatches);
            }

            var matchedAssetCount = allMatches.Select(match => match.assetPath).Distinct(StringComparer.Ordinal).Count();
            var returnedMatches = allMatches.Take(query.Limit).ToArray();
            return new MonoBehaviourReferenceResult
            {
                Provider = new ReferenceProviderMetadata
                {
                    id = ProviderId,
                    selection = "guid_text_scan",
                    confidence = "text_candidate",
                    capabilities = Capabilities
                },
                UsageCount = allMatches.Count,
                MatchedAssetCount = matchedAssetCount,
                Truncated = allMatches.Count > returnedMatches.Length,
                Matches = returnedMatches
            };
        }

        private static void ScanFile(string assetPath, string absolutePath, string guid, List<ScriptGuidUsageMatch> matches)
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(absolutePath))
            {
                lineNumber++;
                var column = line.IndexOf(guid, StringComparison.Ordinal);
                if (column < 0)
                {
                    continue;
                }

                matches.Add(new ScriptGuidUsageMatch
                {
                    assetPath = assetPath,
                    assetType = ClassifyAssetType(assetPath),
                    line = lineNumber,
                    column = column + 1,
                    text = Truncate(line.Trim(), MonoBehaviourSemanticsContract.MaxLineTextLength)
                });
            }
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string AssetTypeToExtension(string assetType)
        {
            return string.Equals(assetType, "scene", StringComparison.Ordinal) ? ".unity" : ".prefab";
        }

        private static IEnumerable<string> EnumerateUnityAssetFiles(string folder)
        {
            var absoluteFolder = ToAbsoluteProjectPath(folder);
            if (!Directory.Exists(absoluteFolder))
            {
                yield break;
            }

            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? string.Empty;
            var files = Directory.EnumerateFiles(absoluteFolder, "*.*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var relativePath = file.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                yield return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        private static string ClassifyAssetType(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase) ? "scene" : "prefab";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength);
        }
    }

    internal sealed class MonoBehaviourReferenceService
    {
        private readonly IMonoBehaviourReferenceProvider[] _providers;

        public MonoBehaviourReferenceService(params IMonoBehaviourReferenceProvider[] providers)
        {
            _providers = providers == null || providers.Length == 0
                ? new IMonoBehaviourReferenceProvider[] { new GuidTextScanReferenceProvider() }
                : providers;
        }

        public bool TryFindProvider(string selection, out IMonoBehaviourReferenceProvider provider, out string failure)
        {
            var normalized = string.IsNullOrWhiteSpace(selection) ? "auto" : selection.Trim();
            foreach (var candidate in _providers)
            {
                if (candidate.CanHandle(normalized, out _))
                {
                    provider = candidate;
                    failure = null;
                    return true;
                }
            }

            provider = null;
            failure = $"Provider '{normalized}' is not available. MVP supports 'auto' and 'guid_text_scan'.";
            return false;
        }
    }
}
