using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    [Serializable]
    public sealed class ToolResultDetailsMetadata
    {
        public bool available;
        public string reportPath;
        public bool recommendedRead;
        public string[] recommendedPointers = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ToolFollowUpOption
    {
        public string tool;
        public string reason;
        public JObject args = new JObject();
    }

    [Serializable]
    public sealed class ToolFollowUpMetadata
    {
        public bool recommended;
        public ToolFollowUpOption[] options = Array.Empty<ToolFollowUpOption>();
    }

    internal static class ToolResultMetadata
    {
        public static ToolResultDetailsMetadata CreateDetails(bool available, bool recommendedRead, params string[] recommendedPointers)
        {
            return new ToolResultDetailsMetadata
            {
                available = available,
                recommendedRead = recommendedRead,
                recommendedPointers = recommendedPointers ?? Array.Empty<string>()
            };
        }

        public static ToolFollowUpMetadata None()
        {
            return new ToolFollowUpMetadata
            {
                recommended = false,
                options = Array.Empty<ToolFollowUpOption>()
            };
        }

        public static ToolFollowUpMetadata Recommended(params ToolFollowUpOption[] options)
        {
            if (options == null || options.Length == 0 || options.Length > 3)
            {
                throw new ArgumentException("followUp options must contain between one and three entries when recommended is true.", nameof(options));
            }

            return new ToolFollowUpMetadata
            {
                recommended = true,
                options = options
            };
        }

        public static ToolFollowUpOption Option(string tool, string reason, JObject args = null)
        {
            if (string.IsNullOrWhiteSpace(tool))
            {
                throw new ArgumentException("tool is required.", nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("reason is required.", nameof(reason));
            }

            return new ToolFollowUpOption
            {
                tool = tool,
                reason = reason,
                args = args ?? new JObject()
            };
        }

        public static void AttachReportPath(ToolResultDetailsMetadata details, string reportPath)
        {
            if (details != null)
            {
                details.reportPath = reportPath;
            }
        }
    }

    internal static class UnityQueriesJsonUtil
    {
        public const string CurrentSchemaVersion = "1.0";

        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (string.IsNullOrWhiteSpace(rawArgsJson))
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            var trimmed = rawArgsJson.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson) ?? new TArgs();
                return true;
            }
            catch (Exception exception)
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string SerializeObject(object value)
        {
            return JsonConvert.SerializeObject(value);
        }
    }

    internal static class UnityQueriesResult
    {
        public static UnityMcpToolResult InvalidArgs(string code, string message)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = UnityMcpToolStatus.InvalidArgs,
                Summary = message,
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

    internal static class UnityQueriesReportWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string GetReportRootAbsolutePath(UnityMcpToolContext context)
        {
            var projectRoot = string.IsNullOrWhiteSpace(context?.ProjectRoot)
                ? Directory.GetParent(Application.dataPath)?.FullName
                : context.ProjectRoot;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var tempRoot = string.IsNullOrWhiteSpace(context?.TempRoot) ? "Temp/AgentBridge" : context.TempRoot;
            var queueRoot = Path.GetFullPath(Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar)));
            return Path.Combine(queueRoot, "reports");
        }

        public static string GetReportRelativePath(UnityMcpToolContext context, string commandId, string prefix)
        {
            ResolveReportPaths(context, commandId, prefix, out _, out var relativePath);
            return relativePath;
        }

        public static string WriteReport(UnityMcpToolContext context, string commandId, string prefix, object report)
        {
            ResolveReportPaths(context, commandId, prefix, out var absolutePath, out var relativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            WriteJsonAtomic(absolutePath, SerializeReport(report));
            return relativePath;
        }

        private static void ResolveReportPaths(UnityMcpToolContext context, string commandId, string prefix, out string absolutePath, out string relativePath)
        {
            absolutePath = null;
            relativePath = null;

            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("commandId is required.", nameof(commandId));
            }

            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("prefix is required.", nameof(prefix));
            }

            var projectRoot = string.IsNullOrWhiteSpace(context?.ProjectRoot)
                ? Directory.GetParent(Application.dataPath)?.FullName
                : context.ProjectRoot;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var reportsDirectory = GetReportRootAbsolutePath(context);
            Directory.CreateDirectory(reportsDirectory);

            var fileName = prefix + "_" + commandId + ".json";
            absolutePath = Path.Combine(reportsDirectory, fileName);
            relativePath = GetRelativeProjectPath(projectRoot, absolutePath);
        }

        private static string SerializeReport(object report)
        {
            return report is JToken token
                ? token.ToString(Formatting.None)
                : UnityQueriesJsonUtil.SerializeObject(report);
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
    }
}
