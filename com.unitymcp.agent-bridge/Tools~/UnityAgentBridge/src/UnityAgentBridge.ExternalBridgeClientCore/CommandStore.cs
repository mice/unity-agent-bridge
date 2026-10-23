using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class CommandStore
{
    public const int PollIntervalMs = 200;

    public static void EnsureQueueDirectories(QueuePaths queuePaths)
    {
        Directory.CreateDirectory(queuePaths.InboxDirectory);
        Directory.CreateDirectory(queuePaths.OutboxDirectory);
        Directory.CreateDirectory(queuePaths.ProcessingDirectory);
        Directory.CreateDirectory(queuePaths.StatusDirectory);
    }

    public void WriteInboxAtomic(QueuePaths queuePaths, string commandId, string commandJson)
    {
        if (!string.IsNullOrEmpty(queuePaths.TargetEditorSessionId))
        {
            using var text = new StringReader(commandJson);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var envelope = JObject.Load(reader);
            if ((envelope["projectId"] != null && envelope.Value<string>("projectId") != queuePaths.TargetProjectId) ||
                (envelope["editorSessionId"] != null && envelope.Value<string>("editorSessionId") != queuePaths.TargetEditorSessionId))
                throw new InvalidOperationException("PROJECT_TARGET_CONFLICT");
            envelope["targetSchemaVersion"] = 1;
            envelope["projectId"] = queuePaths.TargetProjectId;
            envelope["editorSessionId"] = queuePaths.TargetEditorSessionId;
            commandJson = envelope.ToString(Formatting.None);
        }
        var targetPath = Path.Combine(queuePaths.InboxDirectory, commandId + ".json");
        var tempPath = targetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        File.WriteAllText(tempPath, commandJson, new UTF8Encoding(false));
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
    }

    public async Task<ToolResultEnvelope> WaitForResultAsync(QueuePaths queuePaths, string commandId, int waitTimeoutMs, CancellationToken cancellationToken)
    {
        var resultPath = Path.Combine(queuePaths.OutboxDirectory, commandId + ".result.json");
        var startedAtUtc = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAtUtc).TotalMilliseconds <= waitTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(resultPath))
            {
                try
                {
                    var rawJson = File.ReadAllText(resultPath, Encoding.UTF8);
                    var document = JObject.Parse(rawJson);
                    var status = document.Value<string>("status") ?? string.Empty;
                    return new ToolResultEnvelope(rawJson, status, !IsKnownStatus(status));
                }
                catch (IOException)
                {
                }
                catch (JsonReaderException)
                {
                }
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for result '{commandId}' in '{queuePaths.OutboxDirectory}'.");
    }

    public static int CountJsonFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        return Directory.GetFiles(directoryPath, "*.json").Length;
    }

    private static bool IsKnownStatus(string status)
    {
        return status is "success" or "failed" or "timeout" or "invalid_args" or "unsupported" or "blocked" or "exception" or "cancelled";
    }
}
