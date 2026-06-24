using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    public static class JsonUtil
    {
        public const string CurrentSchemaVersion = "1.0";

        public static string BuildCommandEnvelope(string commandId, string tool, int timeoutMs, string argsJson, DateTime? createdAtUtc = null)
        {
            var argsNode = ParseTokenNoDate(argsJson);
            if (argsNode is not JObject argsObject)
            {
                throw new ArgumentException("args must be a JSON object.", nameof(argsJson));
            }

            var envelope = new JObject
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["commandId"] = commandId,
                ["tool"] = tool,
                ["timeoutMs"] = timeoutMs,
                ["createdAt"] = (createdAtUtc ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture),
                ["args"] = argsObject
            };

            return envelope.ToString(Formatting.None);
        }

        public static CommandParseResult ExtractCommand(string rawCommandJson)
        {
            if (string.IsNullOrWhiteSpace(rawCommandJson))
            {
                return CommandParseResult.FromFailure(ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_EMPTY", "Command JSON is empty."));
            }

            JObject parsedObject;
            try
            {
                parsedObject = ParseObjectNoDate(rawCommandJson);
            }
            catch (JsonReaderException exception)
            {
                return CommandParseResult.FromFailure(ToolResult.InvalidArgs("AGENTBRIDGE_COMMAND_PARSE_FAILED", exception.Message));
            }

            if (!TryReadRequiredString(parsedObject, "schemaVersion", out var schemaVersion, out var failure))
            {
                return CommandParseResult.FromFailure(failure);
            }

            if (!string.Equals(schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                return CommandParseResult.FromFailure(ToolResult.Unsupported("AGENTBRIDGE_SCHEMA_UNSUPPORTED", $"Unsupported schemaVersion '{schemaVersion}'."));
            }

            if (!TryReadRequiredString(parsedObject, "commandId", out var commandId, out failure) ||
                !TryReadRequiredString(parsedObject, "tool", out var tool, out failure) ||
                !TryReadRequiredInt(parsedObject, "timeoutMs", out var timeoutMs, out failure) ||
                !TryReadRequiredString(parsedObject, "createdAt", out var createdAt, out failure))
            {
                return CommandParseResult.FromFailure(failure);
            }

            if (!parsedObject.TryGetValue("args", out var argsToken) || argsToken is not JObject argsObject)
            {
                return CommandParseResult.FromFailure(ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object."));
            }

            return CommandParseResult.FromCommand(new AgentCommand
            {
                schemaVersion = schemaVersion,
                commandId = commandId,
                tool = tool,
                timeoutMs = timeoutMs,
                createdAt = createdAt,
                rawArgsJson = argsObject.ToString(Formatting.None)
            });
        }

        public static string SerializeResult(ToolResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var builder = new StringBuilder(512);
            builder.Append('{');
            AppendProperty(builder, "schemaVersion", result.schemaVersion ?? CurrentSchemaVersion);
            AppendProperty(builder, "commandId", result.commandId);
            AppendProperty(builder, "tool", result.tool);
            AppendProperty(builder, "success", result.success);
            AppendProperty(builder, "status", result.status);
            AppendProperty(builder, "startedAt", result.startedAt);
            AppendProperty(builder, "finishedAt", result.finishedAt);
            AppendProperty(builder, "durationMs", result.durationMs);
            AppendProperty(builder, "summary", result.summary);
            AppendErrors(builder, "errors", result.errors);
            AppendWarnings(builder, "warnings", result.warnings);
            AppendLogs(builder, "logs", result.logs);
            AppendRawJsonProperty(builder, "metrics", string.IsNullOrWhiteSpace(result.metricsObjectJson) ? "{}" : result.metricsObjectJson);
            AppendStringArray(builder, "changedFiles", result.changedFiles);
            AppendProperty(builder, "reportPath", result.reportPath, false);
            builder.Append('}');
            return builder.ToString();
        }

        public static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out ToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (!IsJsonObject(rawArgsJson))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson);
                if (args == null)
                {
                    args = new TArgs();
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        public static string SerializeObject(object value)
        {
            if (value == null)
            {
                return "{}";
            }

            return JsonConvert.SerializeObject(value, Formatting.None);
        }

        public static bool TryReadTopLevelObject(string rawJson, out Dictionary<string, string> properties, out ToolResult failure)
        {
            failure = null;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                properties = null;
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            if (!TrySplitTopLevelObject(rawJson, out properties, out var parseError))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_PARSE_FAILED", parseError);
                return false;
            }

            return true;
        }

        public static bool TryReadStringProperty(
            IReadOnlyDictionary<string, string> properties,
            string propertyName,
            bool required,
            out string value,
            out ToolResult failure)
        {
            failure = null;
            value = null;

            if (!properties.TryGetValue(propertyName, out var rawValue))
            {
                if (!required)
                {
                    return true;
                }

                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            value = ParseJsonString(rawValue);
            if (value == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", $"Field '{propertyName}' must be a JSON string.");
                return false;
            }

            return true;
        }

        public static bool TryReadObjectProperty(
            IReadOnlyDictionary<string, string> properties,
            string propertyName,
            bool required,
            out string rawObjectJson,
            out ToolResult failure)
        {
            failure = null;
            rawObjectJson = null;

            if (!properties.TryGetValue(propertyName, out var rawValue))
            {
                if (!required)
                {
                    return true;
                }

                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            if (!IsJsonObject(rawValue))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", $"Field '{propertyName}' must be a JSON object.");
                return false;
            }

            rawObjectJson = rawValue.Trim();
            return true;
        }

        private static bool TryReadRequiredString(
            IReadOnlyDictionary<string, string> properties,
            string propertyName,
            out string value,
            out ToolResult failure)
        {
            failure = null;
            value = null;

            if (!properties.TryGetValue(propertyName, out var rawValue))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            value = ParseJsonString(rawValue);
            if (value == null)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", $"Field '{propertyName}' must be a JSON string.");
                return false;
            }

            return true;
        }

        private static bool TryReadRequiredInt(
            IReadOnlyDictionary<string, string> properties,
            string propertyName,
            out int value,
            out ToolResult failure)
        {
            failure = null;
            value = 0;

            if (!properties.TryGetValue(propertyName, out var rawValue))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", $"Field '{propertyName}' must be an integer.");
                return false;
            }

            return true;
        }

        private static bool TryReadRequiredString(
            JObject properties,
            string propertyName,
            out string value,
            out ToolResult failure)
        {
            failure = null;
            value = null;

            if (!properties.TryGetValue(propertyName, out var token))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            if (token.Type != JTokenType.String)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", $"Field '{propertyName}' must be a JSON string.");
                return false;
            }

            value = token.Value<string>();
            return true;
        }

        private static bool TryReadRequiredInt(
            JObject properties,
            string propertyName,
            out int value,
            out ToolResult failure)
        {
            failure = null;
            value = 0;

            if (!properties.TryGetValue(propertyName, out var token))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", $"Missing required field '{propertyName}'.");
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_FIELD_TYPE_INVALID", $"Field '{propertyName}' must be an integer.");
                return false;
            }

            value = token.Value<int>();
            return true;
        }

        private static bool TrySplitTopLevelObject(string json, out Dictionary<string, string> properties, out string error)
        {
            properties = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;

            JObject parsedObject;
            try
            {
                parsedObject = ParseObjectNoDate(json);
            }
            catch (JsonReaderException exception)
            {
                error = exception.Message;
                return false;
            }

            foreach (var property in parsedObject.Properties())
            {
                properties[property.Name] = JsonConvert.SerializeObject(property.Value);
            }

            return true;
        }

        private static bool IsJsonObject(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            var trimmed = rawJson.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}';
        }

        private static string ParseJsonString(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                return ParseTokenNoDate(rawJson) is JValue { Type: JTokenType.String } stringValue
                    ? stringValue.Value<string>()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static JToken ParseTokenNoDate(string rawJson)
        {
            using (var reader = new JsonTextReader(new StringReader(rawJson)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JToken.ReadFrom(reader);
            }
        }

        private static JObject ParseObjectNoDate(string rawJson)
        {
            using (var reader = new JsonTextReader(new StringReader(rawJson)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                var token = JToken.ReadFrom(reader);
                if (reader.Read())
                {
                    throw new JsonReaderException("Additional text encountered after finished reading JSON content.");
                }

                if (token is JObject parsedObject)
                {
                    return parsedObject;
                }

                throw new JsonReaderException("JSON root must be an object.");
            }
        }

        private static void AppendProperty(StringBuilder builder, string propertyName, string value, bool appendTrailingComma = true)
        {
            builder.Append('"').Append(propertyName).Append("\":");
            if (value == null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append('"').Append(EscapeJsonString(value)).Append('"');
            }

            if (appendTrailingComma)
            {
                builder.Append(',');
            }
        }

        private static void AppendProperty(StringBuilder builder, string propertyName, bool value)
        {
            builder.Append('"').Append(propertyName).Append("\":").Append(value ? "true" : "false").Append(',');
        }

        private static void AppendProperty(StringBuilder builder, string propertyName, long value)
        {
            builder.Append('"').Append(propertyName).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        private static void AppendProperty(StringBuilder builder, string propertyName, int value, bool appendTrailingComma)
        {
            builder.Append('"').Append(propertyName).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
            if (appendTrailingComma)
            {
                builder.Append(',');
            }
        }

        private static void AppendRawJsonProperty(StringBuilder builder, string propertyName, string rawJson)
        {
            builder.Append('"').Append(propertyName).Append("\":").Append(rawJson).Append(',');
        }

        private static void AppendErrors(StringBuilder builder, string propertyName, IList<ToolError> errors)
        {
            builder.Append('"').Append(propertyName).Append("\":[");
            for (var index = 0; index < (errors?.Count ?? 0); index++)
            {
                var error = errors[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                AppendProperty(builder, "code", error.code);
                AppendProperty(builder, "message", error.message);
                AppendProperty(builder, "file", error.file);
                AppendProperty(builder, "line", error.line);
                AppendProperty(builder, "column", error.column, false);
                builder.Append('}');
            }

            builder.Append("],");
        }

        private static void AppendWarnings(StringBuilder builder, string propertyName, IList<ToolWarning> warnings)
        {
            builder.Append('"').Append(propertyName).Append("\":[");
            for (var index = 0; index < (warnings?.Count ?? 0); index++)
            {
                var warning = warnings[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                AppendProperty(builder, "code", warning.code);
                AppendProperty(builder, "message", warning.message, false);
                builder.Append('}');
            }

            builder.Append("],");
        }

        private static void AppendLogs(StringBuilder builder, string propertyName, IList<ToolLog> logs)
        {
            builder.Append('"').Append(propertyName).Append("\":[");
            for (var index = 0; index < (logs?.Count ?? 0); index++)
            {
                var log = logs[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{');
                AppendProperty(builder, "level", log.level);
                AppendProperty(builder, "message", log.message);
                AppendProperty(builder, "timestamp", log.timestamp, false);
                builder.Append('}');
            }

            builder.Append("],");
        }

        private static void AppendStringArray(StringBuilder builder, string propertyName, IList<string> values)
        {
            builder.Append('"').Append(propertyName).Append("\":[");
            for (var index = 0; index < (values?.Count ?? 0); index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('"').Append(EscapeJsonString(values[index] ?? string.Empty)).Append('"');
            }

            builder.Append("],");
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }

    public sealed class CommandParseResult
    {
        private CommandParseResult()
        {
        }

        public AgentCommand Command { get; private set; }

        public ToolResult Failure { get; private set; }

        public bool Success => Command != null && Failure == null;

        public static CommandParseResult FromCommand(AgentCommand command)
        {
            return new CommandParseResult
            {
                Command = command
            };
        }

        public static CommandParseResult FromFailure(ToolResult failure)
        {
            return new CommandParseResult
            {
                Failure = failure
            };
        }
    }
}
