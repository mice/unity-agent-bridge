using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    /// <summary>Closed RPC projection; callers serialize access from both redirected streams.</summary>
    public sealed class PiRpcSession
    {
        public const string MarkerPrefix = "AGB_PI_GUARD:";
        public const int MaxFrameCharacters = 1048576;
        private readonly string nonce;
        private readonly int maxTurns;
        private readonly int maxOutputBytes;
        private readonly string task;

        public PiRpcSession(string nonce, int maxTurns, int maxOutputBytes, string task)
        {
            this.nonce = nonce;
            this.maxTurns = maxTurns;
            this.maxOutputBytes = maxOutputBytes;
            this.task = task;
        }

        public bool GuardReady { get; private set; }
        public bool PromptSent { get; private set; }
        public bool Completed { get; private set; }
        public string Output { get; private set; } = string.Empty;
        public string FailureCode { get; private set; }
        public string FailureMessage { get; private set; }

        public string AcceptGuardLine(string line)
        {
            if (!line.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return null;
            try
            {
                var marker = JObject.Parse(line.Substring(MarkerPrefix.Length));
                if (marker.Value<string>("nonce") != nonce || marker.Value<int?>("protocolVersion") != 1 ||
                    marker.Value<int?>("maxTurns") != maxTurns)
                    throw new InvalidOperationException("Pi guard readiness does not match this task.");
                var type = marker.Value<string>("type");
                if (type == "limit")
                {
                    if (marker.Value<int?>("requests") != maxTurns)
                        throw new InvalidOperationException("Pi guard request count is invalid.");
                    Fail("PI_TURN_LIMIT_EXCEEDED", "Pi exhausted the model-request ceiling of " + maxTurns + ".");
                    return null;
                }
                if (type != "ready" || marker.Value<int?>("requests") != 0 || PromptSent || FailureCode != null)
                    throw new InvalidOperationException("Unexpected Pi guard readiness marker.");
                GuardReady = true;
                PromptSent = true;
                return JsonConvert.SerializeObject(new { id = nonce, type = "prompt", message = task });
            }
            catch (Exception exception) { Fail("PI_GUARD_INVALID", exception.Message); return null; }
        }

        public void AcceptEventLine(string line)
        {
            if (FailureCode != null || string.IsNullOrWhiteSpace(line)) return;
            try
            {
                if (line.Length > MaxFrameCharacters) throw new InvalidOperationException("Pi RPC frame exceeds the size limit.");
                var message = JObject.Parse(line);
                var type = message.Value<string>("type");
                if (string.IsNullOrWhiteSpace(type)) throw new InvalidOperationException("Pi RPC event type is required.");
                if (type == "extension_error")
                {
                    Fail("PI_GUARD_INVALID", "The fixed Pi budget guard reported an extension failure.");
                    return;
                }
                if (type == "response" && message.Value<string>("command") == "prompt")
                {
                    if (message.Value<string>("id") != nonce || message.Value<bool?>("success") != true)
                        Fail("PI_RPC_REQUEST_FAILED", "Pi rejected the fixed task prompt.");
                }
                if (type != "agent_end") return;
                if (!GuardReady || !PromptSent) throw new InvalidOperationException("Pi completed before guard readiness.");
                if (message.Value<bool?>("willRetry") == true) return;
                if (Completed) throw new InvalidOperationException("Pi emitted duplicate terminal results.");
                var assistant = (message["messages"] as JArray)?.OfType<JObject>()
                    .LastOrDefault(item => item.Value<string>("role") == "assistant");
                if (assistant == null) throw new InvalidOperationException("Pi terminal event has no assistant message.");
                var stopReason = assistant.Value<string>("stopReason");
                if (stopReason != "stop")
                {
                    var diagnostic = assistant["errorMessage"]?.Type == JTokenType.String
                        ? assistant.Value<string>("errorMessage") : null;
                    Fail("PI_MODEL_FAILED", ProviderFailureMessage(diagnostic));
                    return;
                }
                var content = assistant["content"] as JArray;
                if (content == null) throw new InvalidOperationException("Pi assistant content is missing.");
                var text = string.Join("\n", content.OfType<JObject>().Where(item => item.Value<string>("type") == "text")
                    .Select(item => item.Value<string>("text") ?? string.Empty));
                Output = BoundText(text);
                Completed = true;
            }
            catch (Exception exception) { Fail("PI_RPC_PROTOCOL_INVALID", exception.Message); }
        }

        public void EndOfStream(int exitCode)
        {
            if (FailureCode != null) return;
            if (!GuardReady) Fail("PI_GUARD_NOT_READY", "Pi exited without confirming its turn-budget guard.");
            else if (exitCode == 78) Fail("PI_TURN_LIMIT_EXCEEDED", "Pi exhausted its model-request ceiling.");
            else if (exitCode == 0 && !Completed) Fail("PI_RPC_PROTOCOL_INVALID", "Pi exited without a terminal assistant response.");
        }

        public void Fail(string code, string message)
        {
            if (FailureCode != null) return;
            FailureCode = code;
            FailureMessage = message;
            Output = string.Empty;
        }

        private static string ProviderFailureMessage(string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
                return "Pi did not finish with a successful terminal assistant response.";
            var redacted = Regex.Replace(diagnostic, @"\bBearer\s+\S+", "Bearer [redacted]", RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, @"\bsk-[A-Za-z0-9_-]+", "[redacted]");
            redacted = Regex.Replace(redacted, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[redacted]");
            redacted = Regex.Replace(redacted, @"\b(access_token|refresh_token|api_key)\s*[:=]\s*[^\s,;]+", "$1=[redacted]", RegexOptions.IgnoreCase);
            var message = "Pi model failed: " + redacted;
            return message.Length > 1024 ? message.Substring(0, 1024) : message;
        }

        private string BoundText(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) <= maxOutputBytes) return text;
            var result = new StringBuilder();
            var bytes = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var count = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                var piece = text.Substring(i, count);
                var size = Encoding.UTF8.GetByteCount(piece);
                if (bytes + size > maxOutputBytes) break;
                result.Append(piece);
                bytes += size;
                i += count - 1;
            }
            return result.ToString();
        }
    }
}
