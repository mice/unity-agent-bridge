using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class AgentTaskDurableResults
    {
        private static double nextCleanup;
        private static readonly AgentTaskControlCodec Codec = new AgentTaskControlCodec();
        internal static string Root => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "AgentBridge");
        internal static readonly string EditorSession = System.Diagnostics.Process.GetCurrentProcess().Id + ":" + System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        static AgentTaskDurableResults()
        {
            AgentTaskTemplateExecutionService.ConfigurePersistence(Save, id => Read(id));
            EditorApplication.update += Maintain;
        }

        internal static AgentTaskControlResponse Save(AgentTaskControlResponse response)
        {
            if (response?.Snapshot == null || !response.Snapshot.IsTerminal) return response;
            response.ErrorCode = response.ErrorCode ?? response.Snapshot.Error?.Code;
            response.ErrorMessage = response.ErrorMessage ?? response.Snapshot.Error?.Message;
            var path = PathFor(response.AgentTaskId ?? response.Snapshot.Id.Value);
            if (!File.Exists(path)) WriteAtomic(path, JsonConvert.SerializeObject(new RetainedResult
            { version = 1, project = Directory.GetParent(Application.dataPath).FullName, terminalAt = response.Snapshot.TerminalAt ?? DateTimeOffset.UtcNow, response = Codec.SerializeResponse(response) }));
            return response;
        }

        internal static AgentTaskControlResponse Read(string id, bool cancellation = false)
        {
            if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id) return null;
            var path = PathFor(id);
            if (!File.Exists(path)) return null;
            try
            {
                var record = JsonConvert.DeserializeObject<RetainedResult>(File.ReadAllText(path));
                if (record == null || record.version != 1 || record.project != Directory.GetParent(Application.dataPath).FullName)
                    return new AgentTaskControlResponse { Success = false, AgentTaskId = id, State = "Failed", ErrorCode = "AGENT_TASK_RECORD_INCOMPATIBLE", ErrorMessage = "Durable task result is incompatible." };
                var result = Codec.DeserializeResponse(record.response);
                if (cancellation) result.Cancellation = result.State == "Cancelled" ? AgentTaskCancellationDisposition.AlreadyCancelled : AgentTaskCancellationDisposition.AlreadyTerminal;
                return result;
            }
            catch (Exception exception)
            { return new AgentTaskControlResponse { Success = false, AgentTaskId = id, State = "Failed", ErrorCode = "AGENT_TASK_RECORD_CORRUPT", ErrorMessage = exception.Message }; }
        }

        internal static void WriteAtomic(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, content);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static string ImportLegacy(string category, string pattern, string legacyDirectory)
        {
            var directory = Path.Combine(Root, category);
            Directory.CreateDirectory(directory);
            var marker = Path.Combine(directory, ".legacy-imported");
            if (!File.Exists(marker))
            {
                if (Directory.Exists(legacyDirectory))
                    foreach (var source in Directory.GetFiles(legacyDirectory, pattern))
                    {
                        var destination = Path.Combine(directory, Path.GetFileName(source));
                        if (!File.Exists(destination)) File.Copy(source, destination);
                    }
                File.WriteAllText(marker, "1");
            }
            return directory;
        }

        private static string PathFor(string id) => Path.Combine(Root, "agent-task-results", id + ".json");
        private static void Maintain()
        {
            if (EditorApplication.timeSinceStartup < nextCleanup) return;
            nextCleanup = EditorApplication.timeSinceStartup + 30;
            foreach (var category in new[] { "agent-task-results", "compile-results", "compile-reports" })
            {
                var directory = Path.Combine(Root, category);
                if (!Directory.Exists(directory)) continue;
                var files = new DirectoryInfo(directory).GetFiles("*.json").OrderBy(file => file.LastWriteTimeUtc).ToList();
                long bytes = files.Sum(file => file.Length);
                var count = files.Count;
                var referencedReports = category == "compile-reports" ? ReferencedCompileReports() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    if (referencedReports.Contains(file.FullName)) continue;
                    if (DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromDays(7) && count <= 1000 && bytes <= 256L * 1024 * 1024) continue;
                    var length = file.Length;
                    try { file.Delete(); bytes -= length; count--; }
                    catch (IOException) { }
                }
            }
        }

        private static HashSet<string> ReferencedCompileReports()
        {
            var reports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in new[] { "agent-task-results", "compile-results" })
            {
                var directory = Path.Combine(Root, category);
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.GetFiles(directory, "*.json"))
                {
                    try
                    {
                        var payload = JObject.Parse(File.ReadAllText(file));
                        if (payload.Value<string>("response") is string response)
                        {
                            var nested = JObject.Parse(response).Value<string>("resultPayloadJson");
                            if (string.IsNullOrEmpty(nested)) continue;
                            payload = JObject.Parse(nested);
                        }
                        var report = payload.Value<string>("reportPath");
                        if (!string.IsNullOrEmpty(report)) reports.Add(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, report)));
                    }
                    catch (Exception exception) when (exception is IOException || exception is JsonException) { }
                }
            }
            return reports;
        }

        [Serializable]
        private sealed class RetainedResult
        {
            public int version;
            public string project;
            public DateTimeOffset terminalAt;
            public string response;
        }
    }
}
