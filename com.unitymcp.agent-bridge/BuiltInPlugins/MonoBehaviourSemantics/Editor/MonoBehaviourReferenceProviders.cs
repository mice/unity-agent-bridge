using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            if (string.Equals(selection, ProviderId, StringComparison.Ordinal))
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

    internal sealed class FindReference2ReflectionProvider : IMonoBehaviourReferenceProvider
    {
        internal const string SettingsFieldName = "monoBehaviourFindReference2ProviderEnabled";
        internal const string DefaultAssemblyName = "FR2";
        internal const string DefaultTypeName = "vietlabs.fr2.FR2";
        internal const string DefaultMethodName = "GetUsedBy";

        private readonly Func<bool> _isEnabled;
        private readonly string _assemblyName;
        private readonly string _typeName;
        private readonly string _methodName;
        private ReflectedContract _contract;

        public FindReference2ReflectionProvider()
            : this(IsEnabledInProjectSettings, DefaultAssemblyName, DefaultTypeName, DefaultMethodName)
        {
        }

        internal FindReference2ReflectionProvider(Func<bool> isEnabled, string assemblyName, string typeName, string methodName)
        {
            _isEnabled = isEnabled ?? (() => false);
            _assemblyName = string.IsNullOrWhiteSpace(assemblyName) ? DefaultAssemblyName : assemblyName;
            _typeName = string.IsNullOrWhiteSpace(typeName) ? DefaultTypeName : typeName;
            _methodName = string.IsNullOrWhiteSpace(methodName) ? DefaultMethodName : methodName;
        }

        public string ProviderId => "findreference2";

        public ReferenceProviderCapabilities Capabilities { get; } = new ReferenceProviderCapabilities
        {
            textMatches = false,
            gameObjectPath = false,
            componentIndex = false,
            serializedFields = false,
            dependencyCache = true
        };

        public bool CanHandle(string selection, out string reason)
        {
            if (!string.Equals(selection, ProviderId, StringComparison.Ordinal))
            {
                reason = $"Provider '{selection}' is not handled by FindReference2.";
                return false;
            }

            var readiness = ProbeReadiness();
            reason = readiness.Message;
            return readiness.Ready;
        }

        public MonoBehaviourReferenceResult FindUsages(MonoBehaviourReferenceQuery query)
        {
            var readiness = ProbeReadiness();
            if (!readiness.Ready)
            {
                return CreateUnavailableResult(query, readiness);
            }

            try
            {
                var raw = readiness.Contract.Method.Invoke(null, CreateInvocationArguments(readiness.Contract, query));
                var allMatches = ConvertMatches(raw)
                    .Where(match => MatchesQuery(match, query))
                    .OrderBy(match => match.assetPath, StringComparer.Ordinal)
                    .ToArray();
                var matches = allMatches
                    .Take(query.Limit)
                    .ToArray();
                var matchedAssetCount = matches.Select(match => match.assetPath).Distinct(StringComparer.Ordinal).Count();
                return new MonoBehaviourReferenceResult
                {
                    Provider = CreateProviderMetadata(query.ProviderSelection, "enabled", null, null),
                    UsageCount = allMatches.Length,
                    MatchedAssetCount = matchedAssetCount,
                    Truncated = allMatches.Length > matches.Length,
                    Matches = matches
                };
            }
            catch (Exception exception)
            {
                var message = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException.Message
                    : exception.Message;
                return CreateUnavailableResult(query, new ReadinessResult(false, "enabled but incompatible", "FINDREFERENCE2_INVOCATION_FAILED", message, null));
            }
        }

        internal ReadinessResult ProbeReadiness()
        {
            if (!_isEnabled())
            {
                return new ReadinessResult(false, "disabled", "FINDREFERENCE2_DISABLED", "FindReference2 provider is disabled in Agent Bridge settings.", null);
            }

            if (_contract != null)
            {
                return new ReadinessResult(true, "enabled", null, null, _contract);
            }

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, _assemblyName, StringComparison.Ordinal));
            if (assembly == null)
            {
                return new ReadinessResult(false, "not installed", "FINDREFERENCE2_ASSEMBLY_MISSING", $"Assembly '{_assemblyName}' is not loaded.", null);
            }

            var type = assembly.GetType(_typeName, false);
            if (type == null)
            {
                return new ReadinessResult(false, "enabled but incompatible", "FINDREFERENCE2_TYPE_MISSING", $"Type '{_typeName}' was not found.", null);
            }

            var method = FindSupportedMethod(type, _methodName);
            if (method == null)
            {
                return new ReadinessResult(false, "enabled but incompatible", "FINDREFERENCE2_METHOD_MISSING", $"Method '{_methodName}' with the supported reflection signature was not found.", null);
            }

            _contract = new ReflectedContract(method);
            return new ReadinessResult(true, "enabled", null, null, _contract);
        }

        private static MethodInfo FindSupportedMethod(Type type, string methodName)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 5 &&
                           parameters[0].ParameterType == typeof(string[]) &&
                           parameters[1].ParameterType.IsEnum &&
                           parameters[2].ParameterType == typeof(int) &&
                           parameters[3].ParameterType.IsEnum &&
                           parameters[4].ParameterType.IsEnum;
                });
        }

        private static object[] CreateInvocationArguments(ReflectedContract contract, MonoBehaviourReferenceQuery query)
        {
            var parameters = contract.Method.GetParameters();
            return new[]
            {
                new[] { query.Script.guid },
                CreateEnumValue(parameters[1].ParameterType, "Direct"),
                1,
                CreateEnumValue(parameters[3].ParameterType, "All"),
                CreateEnumValue(parameters[4].ParameterType, "None")
            };
        }

        private static object CreateEnumValue(Type enumType, string preferredName)
        {
            if (Enum.IsDefined(enumType, preferredName))
            {
                return Enum.Parse(enumType, preferredName);
            }

            var values = Enum.GetValues(enumType);
            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(enumType);
        }

        private static bool MatchesQuery(ScriptGuidUsageMatch match, MonoBehaviourReferenceQuery query)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.assetPath))
            {
                return false;
            }

            var normalizedPath = match.assetPath.Replace('\\', '/');
            if (!query.SearchFolders.Any(folder =>
                    string.Equals(normalizedPath, folder, StringComparison.Ordinal) ||
                    normalizedPath.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal)))
            {
                return false;
            }

            var assetType = ClassifyAssetType(normalizedPath);
            return query.AssetTypes.Any(type => string.Equals(type, assetType, StringComparison.Ordinal));
        }

        internal ReferenceProviderMetadata CreateUnavailableMetadata(string selection)
        {
            var readiness = ProbeReadiness();
            return CreateProviderMetadata(selection, readiness.State, readiness.Code, readiness.Message);
        }

        private MonoBehaviourReferenceResult CreateUnavailableResult(MonoBehaviourReferenceQuery query, ReadinessResult readiness)
        {
            return new MonoBehaviourReferenceResult
            {
                Provider = CreateProviderMetadata(query.ProviderSelection, readiness.State, readiness.Code, readiness.Message),
                UsageCount = 0,
                MatchedAssetCount = 0,
                Truncated = false,
                Matches = Array.Empty<ScriptGuidUsageMatch>()
            };
        }

        private ReferenceProviderMetadata CreateProviderMetadata(string selection, string readinessState, string diagnosticCode, string diagnosticMessage)
        {
            return new ReferenceProviderMetadata
            {
                id = ProviderId,
                selection = string.IsNullOrWhiteSpace(selection) ? ProviderId : selection,
                confidence = "reference_index_candidate",
                capabilities = Capabilities,
                readinessState = readinessState,
                diagnosticCode = diagnosticCode,
                diagnosticMessage = diagnosticMessage
            };
        }

        private static IEnumerable<ScriptGuidUsageMatch> ConvertMatches(object raw)
        {
            if (raw == null)
            {
                yield break;
            }

            if (!(raw is System.Collections.IEnumerable enumerable) || raw is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                var type = item.GetType();
                var assetPath = ReadAssetPath(type, item);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                yield return new ScriptGuidUsageMatch
                {
                    assetPath = assetPath,
                    assetType = ClassifyAssetType(assetPath),
                    line = ReadInt(type, item, "line") ?? ReadInt(type, item, "Line") ?? 0,
                    column = ReadInt(type, item, "column") ?? ReadInt(type, item, "Column") ?? 0,
                    text = ReadString(type, item, "text") ?? ReadString(type, item, "Text") ?? string.Empty,
                    gameObjectPath = ReadString(type, item, "gameObjectPath") ?? ReadString(type, item, "GameObjectPath"),
                    componentIndex = ReadInt(type, item, "componentIndex") ?? ReadInt(type, item, "ComponentIndex"),
                    componentType = ReadString(type, item, "componentType") ?? ReadString(type, item, "ComponentType"),
                    serializedFieldPath = ReadString(type, item, "serializedFieldPath") ?? ReadString(type, item, "SerializedFieldPath")
                };
            }
        }

        private static string ReadString(Type type, object instance, string name)
        {
            var value = ReadMember(type, instance, name);
            return value as string;
        }

        private static string ReadAssetPath(Type type, object instance)
        {
            var path = ReadFirstString(type, instance, "assetPath", "AssetPath", "path", "Path", "filePath", "FilePath");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var guid = ReadFirstString(type, instance, "guid", "Guid", "assetGuid", "AssetGuid");
            if (!string.IsNullOrWhiteSpace(guid))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            var asset = ReadFirstUnityObject(type, instance, "asset", "Asset", "mainAsset", "MainAsset", "target", "Target");
            return asset == null ? null : AssetDatabase.GetAssetPath(asset);
        }

        private static string ReadFirstString(Type type, object instance, params string[] names)
        {
            foreach (var name in names)
            {
                var value = ReadString(type, instance, name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static UnityEngine.Object ReadFirstUnityObject(Type type, object instance, params string[] names)
        {
            foreach (var name in names)
            {
                var value = ReadMember(type, instance, name) as UnityEngine.Object;
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static int? ReadInt(Type type, object instance, string name)
        {
            var value = ReadMember(type, instance, name);
            if (value is int intValue)
            {
                return intValue;
            }

            return null;
        }

        private static object ReadMember(Type type, object instance, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(instance) : null;
        }

        private static bool IsEnabledInProjectSettings()
        {
            var guids = AssetDatabase.FindAssets("t:AgentBridgeSettings");
            if (guids == null || guids.Length == 0)
            {
                return false;
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return false;
            }

            using (var serialized = new SerializedObject(asset))
            {
                var property = serialized.FindProperty(SettingsFieldName);
                return property != null && property.propertyType == SerializedPropertyType.Boolean && property.boolValue;
            }
        }

        private static string ClassifyAssetType(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".unity", StringComparison.OrdinalIgnoreCase) ? "scene" : "prefab";
        }

        internal sealed class ReadinessResult
        {
            public ReadinessResult(bool ready, string state, string code, string message, ReflectedContract contract)
            {
                Ready = ready;
                State = state;
                Code = code;
                Message = message;
                Contract = contract;
            }

            public bool Ready { get; }
            public string State { get; }
            public string Code { get; }
            public string Message { get; }
            public ReflectedContract Contract { get; }
        }

        internal sealed class ReflectedContract
        {
            public ReflectedContract(MethodInfo method)
            {
                Method = method;
            }

            public MethodInfo Method { get; }
        }
    }

    internal sealed class MonoBehaviourReferenceService
    {
        private readonly IMonoBehaviourReferenceProvider[] _providers;

        public MonoBehaviourReferenceService(params IMonoBehaviourReferenceProvider[] providers)
        {
            _providers = providers == null || providers.Length == 0
                ? new IMonoBehaviourReferenceProvider[] { new FindReference2ReflectionProvider(), new GuidTextScanReferenceProvider() }
                : providers;
        }

        public bool TryFindProvider(string selection, out IMonoBehaviourReferenceProvider provider, out ReferenceProviderMetadata unavailableProvider, out string failure)
        {
            var normalized = string.IsNullOrWhiteSpace(selection) ? "auto" : selection.Trim();
            if (string.Equals(normalized, "auto", StringComparison.Ordinal))
            {
                provider = _providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.ProviderId, "findreference2", StringComparison.Ordinal) &&
                    candidate.CanHandle("findreference2", out _))
                    ?? _providers.FirstOrDefault(candidate => string.Equals(candidate.ProviderId, "guid_text_scan", StringComparison.Ordinal));
                unavailableProvider = null;
                failure = provider == null ? "No MonoBehaviour reference provider is available." : null;
                return provider != null;
            }

            foreach (var candidate in _providers)
            {
                if (candidate.CanHandle(normalized, out _))
                {
                    provider = candidate;
                    unavailableProvider = null;
                    failure = null;
                    return true;
                }
            }

            provider = null;
            var findReference2 = _providers.OfType<FindReference2ReflectionProvider>().FirstOrDefault();
            unavailableProvider = findReference2 != null && string.Equals(normalized, "findreference2", StringComparison.Ordinal)
                ? findReference2.CreateUnavailableMetadata(normalized)
                : null;
            failure = unavailableProvider != null && !string.IsNullOrWhiteSpace(unavailableProvider.diagnosticMessage)
                ? unavailableProvider.diagnosticMessage
                : $"Provider '{normalized}' is not available. Supported providers are 'auto', 'guid_text_scan', and opt-in 'findreference2'.";
            return false;
        }
    }
}
