using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    internal sealed class MonoBehaviourYamlSemanticValidator
    {
        private static readonly Regex BlockHeaderRegex = new Regex(
            @"^--- !u!(?<classId>\d+) &(?<fileId>-?\d+)",
            RegexOptions.Compiled);
        private static readonly Regex FileIdRegex = new Regex(@"\{fileID:\s*(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex GuidRegex = new Regex(@"guid:\s*[""']?(?<guid>[a-fA-F0-9]{32})", RegexOptions.Compiled);
        private static readonly Regex ComponentRegex = new Regex(@"^\s*-\s*component:\s*\{fileID:\s*(-?\d+)", RegexOptions.Compiled);
        private static readonly Regex FieldRegex = new Regex(@"^\s{2}(?<name>[A-Za-z0-9_]+):\s*(?<value>.*)$", RegexOptions.Compiled);
        private static readonly HashSet<string> UnityMetadataFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_Script",
            "m_Name",
            "m_EditorClassIdentifier",
            "m_Enabled",
            "m_EditorHideFlags"
        };

        public SemanticValidationSummary Enrich(MonoBehaviourReferenceQuery query, ScriptGuidUsageMatch[] matches)
        {
            var summary = new SemanticValidationSummary
            {
                mode = query.SemanticValidationMode,
                status = "performed"
            };

            if (!string.Equals(query.SemanticValidationMode, MonoBehaviourSemanticsContract.SemanticValidationYaml, StringComparison.Ordinal))
            {
                summary.status = "not_requested";
                return summary;
            }

            var parsedAssets = new Dictionary<string, ParsedAsset>(StringComparer.Ordinal);
            var countedAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches ?? Array.Empty<ScriptGuidUsageMatch>())
            {
                if (match == null || string.IsNullOrWhiteSpace(match.assetPath))
                {
                    continue;
                }

                if (!parsedAssets.TryGetValue(match.assetPath, out var parsed))
                {
                    parsed = Parse(match.assetPath);
                    parsedAssets[match.assetPath] = parsed;
                }

                if (countedAssets.Add(match.assetPath))
                {
                    summary.missingScriptCount += parsed.MissingScriptCount;
                    AddDiagnostics(summary, parsed.Diagnostics);
                }

                var target = parsed.FindTarget(query.Script.guid, match.line);
                if (target == null)
                {
                    match.semanticStatus = "unresolved";
                    summary.unresolvedCount++;
                    AddDiagnostic(summary, match.assetPath + ": target MonoBehaviour block could not be resolved.");
                    continue;
                }

                var gameObject = parsed.GetGameObject(target.GameObjectFileId);
                match.componentType = string.IsNullOrWhiteSpace(query.Script.typeName)
                    ? "MonoBehaviour"
                    : query.Script.typeName;
                match.componentIndex = gameObject?.ComponentIds.IndexOf(target.FileId) ?? -1;
                if (match.componentIndex < 0)
                {
                    match.componentIndex = null;
                }

                match.gameObjectPath = parsed.BuildGameObjectPath(target.GameObjectFileId);
                match.serializedFieldPaths = target.SerializedFields
                    .Select(field => field.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Take(MonoBehaviourSemanticsContract.MaxSemanticFieldPaths)
                    .ToArray();
                match.serializedFieldPath = match.serializedFieldPaths.FirstOrDefault();
                match.riskCodes = target.SerializedFields
                    .Where(field => field.IsNullReferenceCandidate)
                    .Select(field => "null_reference_candidate")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                summary.nullReferenceCandidateCount += target.SerializedFields.Count(field => field.IsNullReferenceCandidate);

                if (!string.IsNullOrWhiteSpace(match.gameObjectPath) && match.componentIndex.HasValue)
                {
                    match.semanticStatus = "resolved";
                    summary.resolvedCount++;
                }
                else
                {
                    match.semanticStatus = "unresolved";
                    summary.unresolvedCount++;
                    AddDiagnostic(summary, match.assetPath + ": GameObject path or component index is incomplete.");
                }
            }

            summary.status = summary.unresolvedCount > 0 || summary.diagnostics.Length > 0 ? "partial" : "performed";
            return summary;
        }

        private static ParsedAsset Parse(string assetPath)
        {
            var result = new ParsedAsset(assetPath);
            var projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? string.Empty;
            var absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                result.Diagnostics.Add("Asset file does not exist.");
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(absolutePath);
                YamlBlock current = null;
                for (var index = 0; index < lines.Length; index++)
                {
                    var header = BlockHeaderRegex.Match(lines[index]);
                    if (header.Success)
                    {
                        if (current != null)
                        {
                            current.EndLine = index;
                            result.Blocks.Add(current);
                        }

                        current = new YamlBlock
                        {
                            ClassId = ParseInt(header.Groups["classId"].Value),
                            FileId = ParseLong(header.Groups["fileId"].Value),
                            StartLine = index + 1,
                            Lines = new List<string>()
                        };
                    }

                    current?.Lines.Add(lines[index]);
                }

                if (current != null)
                {
                    current.EndLine = lines.Length;
                    result.Blocks.Add(current);
                }

                if (result.Blocks.Count == 0)
                {
                    result.Diagnostics.Add("No Unity YAML object blocks were found.");
                    return result;
                }

                result.BuildIndexes();
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add("YAML parse failed: " + exception.Message);
            }

            return result;
        }

        private static void AddDiagnostics(SemanticValidationSummary summary, IEnumerable<string> diagnostics)
        {
            foreach (var diagnostic in diagnostics ?? Array.Empty<string>())
            {
                AddDiagnostic(summary, diagnostic);
            }
        }

        private static void AddDiagnostic(SemanticValidationSummary summary, string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic) || summary.diagnostics.Length >= MonoBehaviourSemanticsContract.MaxSemanticDiagnostics)
            {
                return;
            }

            summary.diagnostics = summary.diagnostics
                .Concat(new[] { diagnostic.Length > 240 ? diagnostic.Substring(0, 240) : diagnostic })
                .Distinct(StringComparer.Ordinal)
                .Take(MonoBehaviourSemanticsContract.MaxSemanticDiagnostics)
                .ToArray();
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, out var result) ? result : 0L;
        }

        private static long? ReadFileId(string value)
        {
            var match = FileIdRegex.Match(value ?? string.Empty);
            return match.Success && long.TryParse(match.Groups[1].Value, out var fileId) ? fileId : (long?)null;
        }

        private static string ReadGuid(string value)
        {
            var match = GuidRegex.Match(value ?? string.Empty);
            return match.Success ? match.Groups["guid"].Value.ToLowerInvariant() : null;
        }

        private static string Unquote(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length >= 2 && ((trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') ||
                                        (trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')))
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private sealed class ParsedAsset
        {
            public ParsedAsset(string assetPath)
            {
                AssetPath = assetPath;
            }

            public string AssetPath { get; }
            public List<YamlBlock> Blocks { get; } = new List<YamlBlock>();
            public List<string> Diagnostics { get; } = new List<string>();
            public int MissingScriptCount { get; private set; }
            private readonly Dictionary<long, GameObjectInfo> _gameObjects = new Dictionary<long, GameObjectInfo>();
            private readonly Dictionary<long, TransformInfo> _transforms = new Dictionary<long, TransformInfo>();

            public void BuildIndexes()
            {
                foreach (var block in Blocks)
                {
                    block.ParseFields();
                    if (block.ClassId == 1)
                    {
                        var gameObject = new GameObjectInfo
                        {
                            FileId = block.FileId,
                            Name = Unquote(block.Fields.TryGetValue("m_Name", out var name) ? name : string.Empty)
                        };
                        gameObject.ComponentIds.AddRange(block.ComponentIds);
                        _gameObjects[gameObject.FileId] = gameObject;
                    }
                    else if (block.ClassId == 4 || block.ClassId == 224)
                    {
                        var gameObjectId = ReadFileId(block.GetField("m_GameObject"));
                        if (gameObjectId.HasValue)
                        {
                            _transforms[block.FileId] = new TransformInfo
                            {
                                FileId = block.FileId,
                                GameObjectFileId = gameObjectId.Value,
                                ParentTransformFileId = ReadFileId(block.GetField("m_Father")) ?? 0L
                            };
                        }
                    }
                    else if (block.ClassId == 114 && !IsValidScriptReference(block.GetField("m_Script")))
                    {
                        MissingScriptCount++;
                    }
                }
            }

            public YamlBlock FindTarget(string guid, int line)
            {
                var normalizedGuid = (guid ?? string.Empty).ToLowerInvariant();
                var candidates = Blocks
                    .Where(block => block.ClassId == 114 && string.Equals(block.ScriptGuid, normalizedGuid, StringComparison.Ordinal))
                    .ToArray();
                return candidates.FirstOrDefault(block => line > 0 && line >= block.StartLine && line <= block.EndLine) ?? candidates.FirstOrDefault();
            }

            public GameObjectInfo GetGameObject(long? fileId)
            {
                return fileId.HasValue && _gameObjects.TryGetValue(fileId.Value, out var gameObject) ? gameObject : null;
            }

            public string BuildGameObjectPath(long? fileId)
            {
                if (!fileId.HasValue || !_gameObjects.ContainsKey(fileId.Value))
                {
                    return null;
                }

                var parts = new List<string>();
                var visited = new HashSet<long>();
                var currentGameObjectId = fileId.Value;
                while (visited.Add(currentGameObjectId) && _gameObjects.TryGetValue(currentGameObjectId, out var gameObject))
                {
                    parts.Add(string.IsNullOrWhiteSpace(gameObject.Name) ? "<unnamed>" : gameObject.Name);
                    var transform = _transforms.Values.FirstOrDefault(candidate => candidate.GameObjectFileId == currentGameObjectId);
                    if (transform == null || transform.ParentTransformFileId == 0 || !_transforms.TryGetValue(transform.ParentTransformFileId, out var parentTransform))
                    {
                        break;
                    }

                    currentGameObjectId = parentTransform.GameObjectFileId;
                }

                parts.Reverse();
                return parts.Count == 0 ? null : string.Join("/", parts.ToArray());
            }

            private static bool IsValidScriptReference(string value)
            {
                var fileId = ReadFileId(value);
                var guid = ReadGuid(value);
                return fileId.HasValue && fileId.Value != 0 && !string.IsNullOrWhiteSpace(guid);
            }
        }

        private sealed class YamlBlock
        {
            public int ClassId;
            public long FileId;
            public int StartLine;
            public int EndLine;
            public List<string> Lines;
            public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<long> ComponentIds { get; } = new List<long>();
            public string ScriptGuid { get; private set; }
            public long? GameObjectFileId { get; private set; }
            public List<SerializedFieldInfo> SerializedFields { get; } = new List<SerializedFieldInfo>();

            public void ParseFields()
            {
                foreach (var line in Lines.Skip(1))
                {
                    var component = ComponentRegex.Match(line);
                    if (ClassId == 1 && component.Success && long.TryParse(component.Groups[1].Value, out var componentId))
                    {
                        ComponentIds.Add(componentId);
                        continue;
                    }

                    var field = FieldRegex.Match(line);
                    if (!field.Success)
                    {
                        continue;
                    }

                    var name = field.Groups["name"].Value;
                    var value = field.Groups["value"].Value;
                    Fields[name] = value;
                }

                GameObjectFileId = ReadFileId(GetField("m_GameObject"));
                ScriptGuid = ReadGuid(GetField("m_Script"));
                if (ClassId != 114)
                {
                    return;
                }

                foreach (var field in Fields)
                {
                    if (UnityMetadataFields.Contains(field.Key) || field.Key == "m_Script" || field.Value.IndexOf("{fileID:", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    var fileId = ReadFileId(field.Value);
                    var guid = ReadGuid(field.Value);
                    var isNull = fileId.HasValue && fileId.Value == 0;
                    if (!fileId.HasValue)
                    {
                        continue;
                    }

                    SerializedFields.Add(new SerializedFieldInfo
                    {
                        Path = field.Key,
                        IsNullReferenceCandidate = isNull || string.Equals(guid, "00000000000000000000000000000000", StringComparison.Ordinal)
                    });
                }
            }

            public string GetField(string name)
            {
                return Fields.TryGetValue(name, out var value) ? value : string.Empty;
            }
        }

        private sealed class GameObjectInfo
        {
            public long FileId;
            public string Name;
            public List<long> ComponentIds { get; } = new List<long>();
        }

        private sealed class TransformInfo
        {
            public long FileId;
            public long GameObjectFileId;
            public long ParentTransformFileId;
        }

        private sealed class SerializedFieldInfo
        {
            public string Path;
            public bool IsNullReferenceCandidate;
        }
    }
}
