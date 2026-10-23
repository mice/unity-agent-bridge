using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityAgentBridge.ExternalBridgeClientCore;

namespace UnityAgentBridge.Mcp;

internal static class McpRetainedReportReader
{
    internal static JsonObject Read(QueuePaths paths, JsonObject arguments, McpTaskRoute? route)
    {
        var reportPath = arguments["reportPath"]!.GetValue<string>();
        JsonNode? selected;
        try { selected = JsonNode.Parse(new BridgeResultHistory(paths).ReadRetainedReport(reportPath)); }
        catch (JsonException e) { throw new InvalidDataException("REPORT_CORRUPT: " + e.Message, e); }
        var pointer = arguments["jsonPointer"]?.GetValue<string>() ?? string.Empty;
        if (pointer.Length > 0)
        {
            if (!pointer.StartsWith('/')) throw new InvalidOperationException("AGENTBRIDGE_REPORT_POINTER_INVALID: JSON pointer must begin with '/'.");
            foreach (var segment in pointer[1..].Split('/'))
            {
                var key = segment.Replace("~1", "/").Replace("~0", "~");
                if (selected is JsonObject obj && obj.TryGetPropertyValue(key, out var child)) selected = child;
                else if (selected is JsonArray array && int.TryParse(key, out var index) && index >= 0 && index < array.Count) selected = array[index];
                else throw new InvalidOperationException("AGENTBRIDGE_REPORT_POINTER_INVALID: JSON pointer does not select a retained value.");
            }
        }
        var offset = arguments["offset"]?.GetValue<int>() ?? 0;
        var limit = arguments["limit"]?.GetValue<int>() ?? 100;
        var maxBytes = arguments["maxBytes"]?.GetValue<int>() ?? 65536;
        var metrics = new JsonObject { ["contractVersion"] = "report_read.v1", ["reportPath"] = reportPath, ["jsonPointer"] = pointer,
            ["offset"] = offset, ["limit"] = limit, ["maxBytes"] = maxBytes };
        JsonNode? value;
        if (selected is JsonArray values)
        {
            var slice = new JsonArray(values.Skip(offset).Take(limit).Select(v => v?.DeepClone()).ToArray());
            int? next = offset + slice.Count < values.Count ? offset + slice.Count : null;
            value = slice;
            metrics["selectedIsArray"] = true; metrics["returnedCount"] = slice.Count; metrics["totalCount"] = values.Count;
            metrics["nextOffset"] = next; metrics["truncated"] = next.HasValue; metrics["items"] = slice; metrics["value"] = null;
        }
        else
        {
            value = selected?.DeepClone(); metrics["selectedIsArray"] = false; metrics["returnedCount"] = null;
            metrics["totalCount"] = null; metrics["nextOffset"] = null; metrics["truncated"] = false; metrics["items"] = null; metrics["value"] = value;
        }
        var bytes = Encoding.UTF8.GetByteCount(value?.ToJsonString() ?? "null");
        metrics["byteCount"] = bytes;
        if (bytes > maxBytes) throw new InvalidOperationException("AGENTBRIDGE_REPORT_SLICE_TOO_LARGE: Selected retained report slice exceeds maxBytes.");
        return new JsonObject { ["success"] = true, ["status"] = "success", ["summary"] = "Read the selected retained report value.",
            ["reportPath"] = reportPath, ["metrics"] = metrics, ["projectId"] = route?.ProjectId ?? ProjectIdentityService.GetProjectId(paths.ProjectPath),
            ["projectPath"] = paths.ProjectPath, ["editorSessionId"] = route?.EditorSessionId, ["connectionId"] = route?.ConnectionId };
    }
}
