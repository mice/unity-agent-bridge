using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    internal static class MonoBehaviourSemanticsContract
    {
        public const int DefaultLimit = 200;
        public const int MaxLimit = 1000;
        public const int MaxLineTextLength = 300;
        public const string SemanticValidationNotPerformed = "not_performed";
    }

    internal static class MonoBehaviourSemanticsJson
    {
        public const string CurrentSchemaVersion = "1.0";

        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            var trimmed = rawArgsJson.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson) ?? new TArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = MonoBehaviourSemanticsResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None);
        }
    }

    internal static class MonoBehaviourSemanticsResult
    {
        public static UnityMcpToolResult InvalidArgs(string code, string message)
        {
            return InvalidArgs(code, message, null);
        }

        public static UnityMcpToolResult InvalidArgs(string code, string message, ReferenceProviderMetadata provider)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = UnityMcpToolStatus.InvalidArgs,
                Summary = message,
                MetricsObjectJson = provider == null ? null : MonoBehaviourSemanticsJson.Serialize(new { provider }),
                Errors =
                {
                    new UnityMcpToolError
                    {
                        Code = code,
                        Message = message
                    }
                }
            };
        }
    }

    internal static class MonoBehaviourSemanticsReportWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string WriteReport(UnityMcpToolContext context, string commandId, string prefix, object report)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                return null;
            }

            var projectRoot = ResolveProjectRoot(context);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var reportRoot = Path.Combine(
                projectRoot,
                (string.IsNullOrWhiteSpace(context?.TempRoot) ? "Temp/AgentBridge" : context.TempRoot).Replace('/', Path.DirectorySeparatorChar),
                "reports");
            Directory.CreateDirectory(reportRoot);

            var fileName = prefix + "_" + SanitizeFileName(commandId) + ".json";
            var absolutePath = Path.Combine(reportRoot, fileName);
            WriteJsonAtomic(absolutePath, MonoBehaviourSemanticsJson.Serialize(report));
            return GetRelativeProjectPath(projectRoot, absolutePath);
        }

        private static string ResolveProjectRoot(UnityMcpToolContext context)
        {
            if (!string.IsNullOrWhiteSpace(context?.ProjectRoot))
            {
                return context.ProjectRoot;
            }

            return Directory.GetParent(Application.dataPath)?.FullName;
        }

        private static void WriteJsonAtomic(string absolutePath, string content)
        {
            var tempPath = absolutePath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(content);
            }

            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            File.Move(tempPath, absolutePath);
        }

        private static string GetRelativeProjectPath(string projectRoot, string absolutePath)
        {
            var projectUri = new Uri(AppendDirectorySeparator(projectRoot));
            var fileUri = new Uri(absolutePath);
            var relativeUri = projectUri.MakeRelativeUri(fileUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty).Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }
    }

    internal static class MonoBehaviourSemanticsMetadata
    {
        public static ToolResultDetailsMetadata Details(bool recommendedRead)
        {
            return new ToolResultDetailsMetadata
            {
                available = false,
                recommendedRead = recommendedRead,
                recommendedPointers = new[] { "/matches" }
            };
        }

        public static ToolFollowUpMetadata CandidatePrecisionFollowUp()
        {
            return new ToolFollowUpMetadata
            {
                recommended = true,
                options = new[]
                {
                    new ToolFollowUpOption
                    {
                        tool = "unity.mono.find_script_guid_usages",
                        reason = "Results are text-level candidates. Future semantic validation can resolve GameObject paths, component indexes, and serialized fields.",
                        args = new JObject()
                    }
                }
            };
        }
    }
}
