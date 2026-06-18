using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    internal static class AgentBridgeReportWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string GetReportRootAbsolutePath(AgentBridgeSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var queueRoot = Path.GetFullPath(Path.Combine(projectRoot, settings.tempRoot.Replace('/', Path.DirectorySeparatorChar)));
            return Path.Combine(queueRoot, "reports");
        }

        public static string GetReportRelativePath(AgentBridgeSettings settings, string commandId, string prefix)
        {
            ResolveReportPaths(settings, commandId, prefix, out _, out var relativePath);
            return relativePath;
        }

        public static string WriteReport(AgentBridgeSettings settings, string commandId, string prefix, object report)
        {
            ResolveReportPaths(settings, commandId, prefix, out var absolutePath, out var relativePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            WriteJsonAtomic(absolutePath, SerializeReport(report));
            return relativePath;
        }

        private static void ResolveReportPaths(AgentBridgeSettings settings, string commandId, string prefix, out string absolutePath, out string relativePath)
        {
            absolutePath = null;
            relativePath = null;

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("commandId is required.", nameof(commandId));
            }

            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("prefix is required.", nameof(prefix));
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var reportsDirectory = GetReportRootAbsolutePath(settings);
            Directory.CreateDirectory(reportsDirectory);

            var fileName = prefix + "_" + commandId + ".json";
            absolutePath = Path.Combine(reportsDirectory, fileName);
            relativePath = GetRelativeProjectPath(projectRoot, absolutePath);
        }

        private static string SerializeReport(object report)
        {
            if (report is JToken token)
            {
                return token.ToString(Formatting.None);
            }

            return JsonUtil.SerializeObject(report);
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
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
