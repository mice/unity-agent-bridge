using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    public sealed class FindScriptGuidUsagesTool : IUnityMcpTool
    {
        private static readonly Regex GuidRegex = new Regex("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);
        private readonly MonoBehaviourReferenceService _referenceService;

        public FindScriptGuidUsagesTool()
            : this(new MonoBehaviourReferenceService())
        {
        }

        internal FindScriptGuidUsagesTool(MonoBehaviourReferenceService referenceService)
        {
            _referenceService = referenceService ?? new MonoBehaviourReferenceService();
        }

        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.mono.find_script_guid_usages",
            Title = "Find MonoBehaviour Script GUID Usages",
            Description = "Find candidate prefab and scene YAML references to a MonoBehaviour script GUID through a read-only provider-based scan.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = MonoBehaviourSemanticsSchemas.FindScriptGuidUsages
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!MonoBehaviourSemanticsJson.TryDeserializeArgs<FindScriptGuidUsagesArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new FindScriptGuidUsagesArgs();
            if (!TryValidateLimit(args.limit, out var limit, out failure) ||
                !TryNormalizeProvider(args.provider, out var providerSelection, out failure) ||
                !TryNormalizeAssetTypes(args.assetTypes, out var assetTypes, out failure) ||
                !TryNormalizeFolders(args.searchFolders, out var searchFolders, out failure) ||
                !TryResolveTarget(args, out var script, out failure))
            {
                return failure;
            }

            if (!_referenceService.TryFindProvider(providerSelection, out var provider, out var unavailableProvider, out var providerFailure))
            {
                return MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_PROVIDER_UNAVAILABLE", providerFailure, unavailableProvider);
            }

            var query = new MonoBehaviourReferenceQuery
            {
                Script = script,
                SearchFolders = searchFolders,
                AssetTypes = assetTypes,
                Limit = limit,
                ProviderSelection = providerSelection
            };

            var searchResult = provider.FindUsages(query);
            var details = MonoBehaviourSemanticsMetadata.Details(searchResult.Truncated || searchResult.Matches.Length > 0);
            var metrics = new FindScriptGuidUsagesMetrics
            {
                script = script,
                provider = searchResult.Provider,
                usageCount = searchResult.UsageCount,
                matchedAssetCount = searchResult.MatchedAssetCount,
                returnedCount = searchResult.Matches.Length,
                limit = limit,
                truncated = searchResult.Truncated,
                semanticValidation = MonoBehaviourSemanticsContract.SemanticValidationNotPerformed,
                matches = searchResult.Matches,
                details = details,
                followUp = MonoBehaviourSemanticsMetadata.CandidatePrecisionFollowUp()
            };

            var report = CreateReport(args, query, metrics);
            var result = new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = $"Found {metrics.usageCount} {metrics.provider.id} matches for MonoBehaviour script GUID {script.guid}; returned {metrics.returnedCount}.",
                MetricsObjectJson = MonoBehaviourSemanticsJson.Serialize(metrics)
            };
            result.ReportPath = MonoBehaviourSemanticsReportWriter.WriteReport(context, context.CommandId, "mono_find_script_guid_usages", report);
            if (details != null)
            {
                details.available = !string.IsNullOrWhiteSpace(result.ReportPath);
                details.reportPath = result.ReportPath;
                result.MetricsObjectJson = MonoBehaviourSemanticsJson.Serialize(metrics);
            }

            return result;
        }

        private static JObject CreateReport(FindScriptGuidUsagesArgs args, MonoBehaviourReferenceQuery query, FindScriptGuidUsagesMetrics metrics)
        {
            return JObject.FromObject(new
            {
                schemaVersion = MonoBehaviourSemanticsJson.CurrentSchemaVersion,
                generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                request = new
                {
                    args.scriptGuid,
                    args.scriptPath,
                    args.typeName,
                    provider = string.IsNullOrWhiteSpace(args.provider) ? "auto" : args.provider.Trim(),
                    searchFolders = query.SearchFolders,
                    assetTypes = query.AssetTypes,
                    limit = query.Limit
                },
                script = metrics.script,
                provider = metrics.provider,
                counts = new
                {
                    metrics.usageCount,
                    metrics.matchedAssetCount,
                    metrics.returnedCount,
                    metrics.limit,
                    metrics.truncated
                },
                semanticValidation = metrics.semanticValidation,
                matches = metrics.matches
            });
        }

        private static bool TryValidateLimit(int? rawLimit, out int limit, out UnityMcpToolResult failure)
        {
            failure = null;
            limit = rawLimit ?? MonoBehaviourSemanticsContract.DefaultLimit;
            if (limit <= 0 || limit > MonoBehaviourSemanticsContract.MaxLimit)
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_LIMIT_INVALID", $"limit must be in the range 1..{MonoBehaviourSemanticsContract.MaxLimit}.");
                return false;
            }

            return true;
        }

        private static bool TryNormalizeProvider(string rawProvider, out string provider, out UnityMcpToolResult failure)
        {
            failure = null;
            provider = string.IsNullOrWhiteSpace(rawProvider) ? "auto" : rawProvider.Trim();
            if (provider == "auto" || provider == "guid_text_scan" || provider == "findreference2")
            {
                return true;
            }

            failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_PROVIDER_INVALID", "provider must be one of: auto, guid_text_scan, findreference2.");
            return false;
        }

        private static bool TryNormalizeAssetTypes(string[] rawAssetTypes, out string[] assetTypes, out UnityMcpToolResult failure)
        {
            failure = null;
            assetTypes = rawAssetTypes == null || rawAssetTypes.Length == 0
                ? new[] { "prefab", "scene" }
                : rawAssetTypes.Select(value => value?.Trim()).ToArray();

            if (assetTypes.Any(string.IsNullOrWhiteSpace) ||
                assetTypes.Any(value => value != "prefab" && value != "scene") ||
                assetTypes.Distinct(StringComparer.Ordinal).Count() != assetTypes.Length)
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_ASSET_TYPES_INVALID", "assetTypes must contain unique values from: prefab, scene.");
                return false;
            }

            assetTypes = assetTypes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return true;
        }

        private static bool TryNormalizeFolders(string[] rawFolders, out string[] folders, out UnityMcpToolResult failure)
        {
            failure = null;
            folders = rawFolders == null || rawFolders.Length == 0
                ? new[] { "Assets" }
                : rawFolders.Select(value => (value ?? string.Empty).Trim().Replace('\\', '/')).ToArray();

            if (folders.Any(string.IsNullOrWhiteSpace) ||
                folders.Any(folder => !string.Equals(folder, "Assets", StringComparison.Ordinal) && !folder.StartsWith("Assets/", StringComparison.Ordinal)) ||
                folders.Any(folder => !AssetDatabase.IsValidFolder(folder)))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_SEARCH_FOLDER_INVALID", "searchFolders must resolve to existing Unity asset folders under Assets/.");
                return false;
            }

            folders = folders.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return true;
        }

        private static bool TryResolveTarget(FindScriptGuidUsagesArgs args, out MonoBehaviourScriptIdentity script, out UnityMcpToolResult failure)
        {
            script = null;
            failure = null;

            var targetCount = CountProvided(args.scriptGuid) + CountProvided(args.scriptPath) + CountProvided(args.typeName);
            if (targetCount != 1)
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_TARGET_INVALID", "Provide exactly one of scriptGuid, scriptPath, or typeName.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(args.scriptGuid))
            {
                var guid = args.scriptGuid.Trim();
                if (!GuidRegex.IsMatch(guid))
                {
                    failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_GUID_INVALID", "scriptGuid must be a 32-character Unity asset GUID.");
                    return false;
                }

                script = new MonoBehaviourScriptIdentity
                {
                    guid = guid.ToLowerInvariant(),
                    path = AssetDatabase.GUIDToAssetPath(guid),
                    source = "scriptGuid"
                };
                FillScriptType(script);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(args.scriptPath))
            {
                return TryResolveScriptPath(args.scriptPath, out script, out failure);
            }

            return TryResolveTypeName(args.typeName, out script, out failure);
        }

        private static bool TryResolveScriptPath(string rawScriptPath, out MonoBehaviourScriptIdentity script, out UnityMcpToolResult failure)
        {
            script = null;
            failure = null;
            var scriptPath = rawScriptPath.Trim().Replace('\\', '/');
            if (!scriptPath.StartsWith("Assets/", StringComparison.Ordinal) || !scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_SCRIPT_PATH_INVALID", "scriptPath must reference an Assets/**/*.cs file.");
                return false;
            }

            var absolutePath = ToAbsoluteProjectPath(scriptPath);
            var metaPath = absolutePath + ".meta";
            if (!File.Exists(absolutePath) || !File.Exists(metaPath))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_SCRIPT_META_MISSING", "scriptPath and its .meta file must exist.");
                return false;
            }

            var guid = ReadMetaGuid(metaPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_SCRIPT_META_GUID_MISSING", "scriptPath .meta file does not contain a readable guid.");
                return false;
            }

            script = new MonoBehaviourScriptIdentity
            {
                guid = guid,
                path = scriptPath,
                source = "scriptPath"
            };
            FillScriptType(script);
            return true;
        }

        private static bool TryResolveTypeName(string rawTypeName, out MonoBehaviourScriptIdentity script, out UnityMcpToolResult failure)
        {
            script = null;
            failure = null;
            var typeName = rawTypeName?.Trim();
            if (string.IsNullOrWhiteSpace(typeName))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_TYPE_NAME_REQUIRED", "typeName is required.");
                return false;
            }

            var candidates = AssetDatabase.FindAssets(typeName + " t:MonoScript")
                .Select(guid => new { guid, path = AssetDatabase.GUIDToAssetPath(guid) })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.path))
                .Select(candidate => new { candidate.guid, candidate.path, monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(candidate.path) })
                .Where(candidate => candidate.monoScript != null)
                .Select(candidate => new { candidate.guid, candidate.path, type = candidate.monoScript.GetClass() })
                .Where(candidate => candidate.type != null && typeof(MonoBehaviour).IsAssignableFrom(candidate.type))
                .Where(candidate =>
                    string.Equals(candidate.type.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(candidate.type.FullName, typeName, StringComparison.Ordinal))
                .OrderBy(candidate => candidate.path, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length != 1)
            {
                var candidatePaths = candidates.Select(candidate => candidate.path).ToArray();
                var message = candidates.Length == 0
                    ? $"typeName '{typeName}' did not resolve to a MonoBehaviour script."
                    : $"typeName '{typeName}' is ambiguous: {string.Join(", ", candidatePaths)}";
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_MONO_TYPE_NAME_AMBIGUOUS", message);
                return false;
            }

            script = new MonoBehaviourScriptIdentity
            {
                guid = candidates[0].guid,
                path = candidates[0].path,
                typeName = candidates[0].type.FullName,
                assemblyName = candidates[0].type.Assembly.GetName().Name,
                source = "typeName"
            };
            return true;
        }

        private static void FillScriptType(MonoBehaviourScriptIdentity script)
        {
            if (script == null || string.IsNullOrWhiteSpace(script.path))
            {
                return;
            }

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(script.path);
            var type = monoScript != null ? monoScript.GetClass() : null;
            if (type == null)
            {
                return;
            }

            script.typeName = type.FullName;
            script.assemblyName = type.Assembly.GetName().Name;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            foreach (var line in File.ReadLines(metaPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("guid:", StringComparison.Ordinal))
                {
                    continue;
                }

                var guid = trimmed.Substring("guid:".Length).Trim();
                return GuidRegex.IsMatch(guid) ? guid.ToLowerInvariant() : null;
            }

            return null;
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static int CountProvided(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? 0 : 1;
        }
    }
}
