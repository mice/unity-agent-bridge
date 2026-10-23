using System;
using System.Text;

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
        public AgentExecutionObservation Execution { get; set; }
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
            if (response != null) return "{\"version\":" + response.Version + ",\"success\":" + (response.Success ? "true" : "false") + ",\"errorCode\":" + StringValue(response.ErrorCode) + ",\"errorMessage\":" + StringValue(response.ErrorMessage) + ",\"agentTaskId\":" + StringValue(response.AgentTaskId ?? (response.Snapshot == null ? null : response.Snapshot.Id.Value)) + ",\"state\":" + StringValue(response.Snapshot == null ? response.State : response.Snapshot.State.ToString()) + ",\"resultKind\":" + StringValue(response.Snapshot?.Result?.Kind.ToString() ?? response.ResultKind) + ",\"resultReason\":" + StringValue(response.Snapshot?.Result?.Reason ?? response.ResultReason) + ",\"resultPayloadJson\":" + StringValue(response.ResultPayloadJson) + ",\"cancellation\":" + StringValue(response.Cancellation?.ToString()) + ",\"cancellationRequested\":" + (response.Snapshot != null && response.Snapshot.CancellationRequested ? "true" : "false") + ",\"createdAt\":" + StringValue(response.Snapshot == null ? null : response.Snapshot.CreatedAt.ToString("O")) + ",\"terminalAt\":" + StringValue(response.Snapshot?.TerminalAt?.ToString("O")) + ",\"execution\":" + SerializeExecution(response.Snapshot?.Execution ?? response.Execution ?? AgentExecutionObservation.Unknown) + "}";
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
                Cancellation = ParseCancellation(ReadString(value, "cancellation")),
                Execution = ParseExecution(value)
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
                    hasTerminal ? terminalAt : (DateTimeOffset?)null, response.Execution);
            }
            return response;
        }

        private static string SerializeExecution(AgentExecutionObservation value) =>
            "{\"schemaVersion\":1,\"stage\":" + StringValue(value.Stage) + ",\"providerId\":" + StringValue(value.ProviderId) +
            ",\"processStarted\":" + (value.ProcessStarted.HasValue ? (value.ProcessStarted.Value ? "true" : "false") : "null") +
            ",\"processStartedAt\":" + StringValue(value.ProcessStartedAt) + ",\"processId\":" + (value.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") +
            ",\"guardReadyAt\":" + StringValue(value.GuardReadyAt) + ",\"promptSubmittedAt\":" + StringValue(value.PromptSubmittedAt) +
            ",\"terminalAt\":" + StringValue(value.TerminalAt) + ",\"exitCode\":" + (value.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + "}";

        private static AgentExecutionObservation ParseExecution(string json)
        {
            var marker = "\"execution\":";
            // The execution object is serialized last and contains only fixed scalar fields.
            var start = json.LastIndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return AgentExecutionObservation.Unknown;
            var body = json.Substring(start + marker.Length);
            if (!body.StartsWith("{", StringComparison.Ordinal)) return AgentExecutionObservation.Unknown;
            var started = ReadString(body, "processStarted");
            int pid, exit;
            return new AgentExecutionObservation(ReadString(body, "stage"), ReadString(body, "providerId"),
                started == "true" ? true : started == "false" ? false : (bool?)null,
                ReadString(body, "processStartedAt"), int.TryParse(ReadString(body, "processId"), out pid) ? pid : (int?)null,
                ReadString(body, "guardReadyAt"), ReadString(body, "promptSubmittedAt"), ReadString(body, "terminalAt"),
                int.TryParse(ReadString(body, "exitCode"), out exit) ? exit : (int?)null);
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
            if (raw == "null") return null;
            if (raw.Length == 0 || raw[0] != '"') return raw;
            if (raw.Length < 2 || raw[raw.Length - 1] != '"') throw new FormatException("Unterminated JSON string.");
            return Unescape(raw.Substring(1, raw.Length - 2));
        }

        private static string ReadRaw(string json, string name)
        {
            var marker = "\"" + name + "\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) throw new FormatException("Missing JSON value: " + name);
            var end = start;
            var quoted = json[start] == '"';
            if (quoted)
            {
                end++;
                while (end < json.Length)
                {
                    if (json[end] == '"')
                    {
                        return json.Substring(start, end + 1 - start);
                    }
                    if (json[end] == '\\')
                    {
                        end++;
                        if (end >= json.Length) throw new FormatException("Truncated JSON escape.");
                    }
                    end++;
                }
                throw new FormatException("Unterminated JSON string.");
            }
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(start, end - start);
        }

        private static string StringValue(string value) => value == null ? "null" : "\"" + Escape(value) + "\"";
        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            var output = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': output.Append("\\\\"); break;
                    case '"': output.Append("\\\""); break;
                    case '\r': output.Append("\\r"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\t': output.Append("\\t"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    default:
                        if (c < 0x20 || char.IsSurrogate(c)) output.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        else output.Append(c);
                        break;
                }
            }
            return output.ToString();
        }
        private static string Unescape(string value)
        {
            var output = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c < 0x20 || c == '"') throw new FormatException("Invalid character in JSON string.");
                if (c != '\\') { output.Append(c); continue; }
                if (++i >= value.Length) throw new FormatException("Truncated JSON escape.");
                switch (value[i])
                {
                    case '\\': output.Append('\\'); break;
                    case '"': output.Append('"'); break;
                    case '/': output.Append('/'); break;
                    case 'r': output.Append('\r'); break;
                    case 'n': output.Append('\n'); break;
                    case 't': output.Append('\t'); break;
                    case 'b': output.Append('\b'); break;
                    case 'f': output.Append('\f'); break;
                    case 'u':
                        if (value.Length - i <= 4) throw new FormatException("Truncated JSON Unicode escape.");
                        var unit = 0;
                        for (var j = 0; j < 4; j++)
                        {
                            var digit = value[++i];
                            var hex = digit >= '0' && digit <= '9' ? digit - '0' : digit >= 'a' && digit <= 'f' ? digit - 'a' + 10 : digit >= 'A' && digit <= 'F' ? digit - 'A' + 10 : -1;
                            if (hex < 0) throw new FormatException("Invalid JSON Unicode escape.");
                            unit = unit * 16 + hex;
                        }
                        // Preserve UTF-16 units, including surrogate pairs, without normalization.
                        output.Append((char)unit);
                        break;
                    default: throw new FormatException("Invalid JSON escape.");
                }
            }
            return output.ToString();
        }
    }
}
