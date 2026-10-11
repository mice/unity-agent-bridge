using System;

namespace UnityMcp.AgentTaskCore
{
    public static class AgentTaskControlProtocol
    {
        public const int CurrentVersion = 1;
        public const string Create = "create";
        public const string Query = "query";
        public const string Result = "result";
        public const string Cancel = "cancel";
    }

    public sealed class AgentTaskControlRequest
    {
        public int Version { get; set; } = AgentTaskControlProtocol.CurrentVersion;
        public string Operation { get; set; }
        public string ProjectIdentity { get; set; }
        public string AgentTaskId { get; set; }
        public string TaskType { get; set; }
        public string Payload { get; set; }
        public int TimeoutMs { get; set; }
    }

    public sealed class AgentTaskControlResponse
    {
        public int Version { get; set; } = AgentTaskControlProtocol.CurrentVersion;
        public bool Success { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public string AgentTaskId { get; set; }
        public AgentTaskSnapshot Snapshot { get; set; }
        public AgentTaskCancellationDisposition? Cancellation { get; set; }
        public string State { get; set; }
        public string ResultKind { get; set; }
        public string ResultReason { get; set; }
        public string ResultPayloadJson { get; set; }
    }

    public sealed class AgentTaskBinding
    {
        public int Version { get; set; } = AgentTaskControlProtocol.CurrentVersion;
        public string ProjectIdentity { get; set; }
        public string ExternalTaskId { get; set; }
        public string AgentTaskId { get; set; }
        public string CommandId { get; set; }
    }

    public interface IAgentTaskControlCodec
    {
        string SerializeRequest(AgentTaskControlRequest request);
        AgentTaskControlRequest DeserializeRequest(string value);
        string SerializeResponse(AgentTaskControlResponse response);
        AgentTaskControlResponse DeserializeResponse(string value);
    }

    public sealed class AgentTaskControlCodec : IAgentTaskControlCodec
    {
        public string SerializeRequest(AgentTaskControlRequest request) => Serialize(request);
        public AgentTaskControlRequest DeserializeRequest(string value) => Deserialize<AgentTaskControlRequest>(value);
        public string SerializeResponse(AgentTaskControlResponse response) => Serialize(response);
        public AgentTaskControlResponse DeserializeResponse(string value) => Deserialize<AgentTaskControlResponse>(value);

        private static string Serialize(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return SimpleJson.Serialize(value);
        }

        private static T Deserialize<T>(string value) where T : class
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("JSON cannot be empty.", nameof(value));
            return SimpleJson.Deserialize<T>(value);
        }
    }

    // Small deterministic JSON adapter for transport envelopes. Payloads remain opaque strings.
    internal static class SimpleJson
    {
        public static string Serialize(object value)
        {
            var request = value as AgentTaskControlRequest;
            if (request != null) return "{\"version\":" + request.Version + ",\"operation\":" + StringValue(request.Operation) + ",\"projectIdentity\":" + StringValue(request.ProjectIdentity) + ",\"agentTaskId\":" + StringValue(request.AgentTaskId) + ",\"taskType\":" + StringValue(request.TaskType) + ",\"payload\":" + StringValue(request.Payload) + ",\"timeoutMs\":" + request.TimeoutMs + "}";
            var response = value as AgentTaskControlResponse;
            if (response != null) return "{\"version\":" + response.Version + ",\"success\":" + (response.Success ? "true" : "false") + ",\"errorCode\":" + StringValue(response.ErrorCode) + ",\"errorMessage\":" + StringValue(response.ErrorMessage) + ",\"agentTaskId\":" + StringValue(response.AgentTaskId ?? (response.Snapshot == null ? null : response.Snapshot.Id.Value)) + ",\"state\":" + StringValue(response.Snapshot == null ? response.State : response.Snapshot.State.ToString()) + ",\"resultKind\":" + StringValue(response.Snapshot?.Result?.Kind.ToString() ?? response.ResultKind) + ",\"resultReason\":" + StringValue(response.Snapshot?.Result?.Reason ?? response.ResultReason) + ",\"resultPayloadJson\":" + StringValue(response.ResultPayloadJson) + ",\"cancellation\":" + StringValue(response.Cancellation?.ToString()) + ",\"cancellationRequested\":" + (response.Snapshot != null && response.Snapshot.CancellationRequested ? "true" : "false") + ",\"createdAt\":" + StringValue(response.Snapshot == null ? null : response.Snapshot.CreatedAt.ToString("O")) + ",\"terminalAt\":" + StringValue(response.Snapshot?.TerminalAt?.ToString("O")) + "}";
            throw new ArgumentException("Unsupported control message type.", nameof(value));
        }

        public static T Deserialize<T>(string value) where T : class
        {
            if (typeof(T) == typeof(AgentTaskControlRequest))
                return ParseRequest(value) as T;
            if (typeof(T) == typeof(AgentTaskControlResponse))
                return ParseResponse(value) as T;
            throw new ArgumentException("Unsupported control message type.", nameof(T));
        }

        private static AgentTaskControlRequest ParseRequest(string value)
        {
            return new AgentTaskControlRequest
            {
                Version = ReadInt(value, "version"),
                Operation = ReadString(value, "operation"),
                ProjectIdentity = ReadString(value, "projectIdentity"),
                AgentTaskId = ReadString(value, "agentTaskId"),
                TaskType = ReadString(value, "taskType"),
                Payload = ReadString(value, "payload"),
                TimeoutMs = ReadOptionalInt(value, "timeoutMs")
            };
        }

        private static AgentTaskControlResponse ParseResponse(string value)
        {
            var response = new AgentTaskControlResponse
            {
                Version = ReadInt(value, "version"),
                Success = ReadString(value, "success") == "true",
                ErrorCode = ReadString(value, "errorCode"),
                ErrorMessage = ReadString(value, "errorMessage"),
                AgentTaskId = ReadString(value, "agentTaskId"),
                State = ReadString(value, "state"),
                ResultKind = ReadString(value, "resultKind"),
                ResultReason = ReadString(value, "resultReason"),
                ResultPayloadJson = ReadString(value, "resultPayloadJson"),
                Cancellation = ParseCancellation(ReadString(value, "cancellation"))
            };
            response.AgentTaskId = ReadString(value, "agentTaskId");
            if (!string.IsNullOrEmpty(response.AgentTaskId))
            {
                AgentTaskState state;
                AgentTaskResultKind resultKind;
                DateTimeOffset createdAt;
                DateTimeOffset terminalAt = DateTimeOffset.MinValue;
                if (!Enum.TryParse(response.State, out state)) throw new FormatException("Invalid task state.");
                if (!Enum.TryParse(response.ResultKind, out resultKind)) resultKind = AgentTaskResultKind.Succeeded;
                if (!DateTimeOffset.TryParse(ReadString(value, "createdAt"), out createdAt)) createdAt = DateTimeOffset.MinValue;
                var terminalText = ReadString(value, "terminalAt");
                var hasTerminal = !string.IsNullOrEmpty(terminalText) && DateTimeOffset.TryParse(terminalText, out terminalAt);
                AgentTaskResult result = null;
                if (!string.IsNullOrEmpty(response.ResultKind))
                {
                    switch (resultKind)
                    {
                        case AgentTaskResultKind.DomainFailure: result = AgentTaskResult.DomainFailure(response.ResultReason); break;
                        case AgentTaskResultKind.TimedOut: result = AgentTaskResult.TimedOut(response.ResultReason); break;
                        case AgentTaskResultKind.Cancelled: result = AgentTaskResult.Cancelled(response.ResultReason); break;
                        default: result = AgentTaskResult.Succeeded(reason: response.ResultReason); break;
                    }
                }
                response.Snapshot = new AgentTaskSnapshot(new AgentTaskId(response.AgentTaskId), state, result,
                    null, ReadString(value, "cancellationRequested") == "true", createdAt,
                    hasTerminal ? terminalAt : (DateTimeOffset?)null);
            }
            return response;
        }

        private static AgentTaskCancellationDisposition? ParseCancellation(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            AgentTaskCancellationDisposition parsed;
            return Enum.TryParse(value, out parsed) ? parsed : (AgentTaskCancellationDisposition?)null;
        }

        private static int ReadInt(string json, string name)
        {
            var raw = ReadRaw(json, name);
            int result;
            if (!int.TryParse(raw, out result)) throw new FormatException("Invalid integer field: " + name);
            return result;
        }

        private static int ReadOptionalInt(string json, string name)
        {
            var raw = ReadRaw(json, name);
            int result;
            return int.TryParse(raw, out result) ? result : 0;
        }

        private static string ReadString(string json, string name)
        {
            var raw = ReadRaw(json, name);
            return raw == "null" ? null : Unescape(raw.Trim('"'));
        }

        private static string ReadRaw(string json, string name)
        {
            var marker = "\"" + name + "\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            var end = start;
            var quoted = json[start] == '"';
            if (quoted)
            {
                end++;
                while (end < json.Length && (json[end] != '"' || json[end - 1] == '\\')) end++;
                return json.Substring(start, Math.Min(end + 1, json.Length) - start);
            }
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(start, end - start);
        }

        private static string StringValue(string value) => value == null ? "null" : "\"" + Escape(value) + "\"";
        private static string Escape(string value) => value == null ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        private static string Unescape(string value) => value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\r", "\r").Replace("\\n", "\n");
    }
}
