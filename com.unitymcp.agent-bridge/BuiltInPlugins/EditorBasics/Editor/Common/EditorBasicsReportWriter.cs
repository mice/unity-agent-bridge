using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    internal static class EditorBasicsReportWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string WriteReport(string projectRoot, string tempRoot, string commandId, string prefix, object report)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(prefix))
            {
                return null;
            }

            var relativeTempRoot = string.IsNullOrWhiteSpace(tempRoot) ? "Temp/AgentBridge" : tempRoot;
            var reportsDirectory = Path.GetFullPath(Path.Combine(projectRoot, relativeTempRoot.Replace('/', Path.DirectorySeparatorChar), "reports"));
            Directory.CreateDirectory(reportsDirectory);
            var absolutePath = Path.Combine(reportsDirectory, prefix + "_" + commandId + ".json");
            var relativePath = GetRelativeProjectPath(projectRoot, absolutePath);
            WriteJsonAtomic(absolutePath, SerializeReport(report));
            return relativePath;
        }

        private static string SerializeReport(object report)
        {
            return report is JToken token ? token.ToString(Formatting.None) : EditorBasicsJson.Serialize(report);
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
