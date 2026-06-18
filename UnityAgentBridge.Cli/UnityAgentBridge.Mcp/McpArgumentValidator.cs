using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.Mcp;

internal static class McpArgumentValidator
{
    public static void ValidateOrThrow(string toolName, string schemaJson, string argumentsJson)
    {
        var schema = JObject.Parse(schemaJson);
        var arguments = JObject.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

        EnsureObjectType(schema, arguments, "$");
        EnsureRequiredProperties(toolName, schema, arguments);
        EnsureNoAdditionalProperties(toolName, schema, arguments);
        EnsurePropertyConstraints(toolName, schema, arguments);
    }

    private static void EnsureObjectType(JObject schema, JToken arguments, string path)
    {
        var type = schema.Value<string>("type");
        if (string.Equals(type, "object", StringComparison.Ordinal) && arguments.Type != JTokenType.Object)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName: null,
                expected: "object",
                code: "invalid_type",
                path,
                $"Invalid input: expected object, received {ToNodeTypeName(arguments.Type)}"));
        }
    }

    private static void EnsureRequiredProperties(string toolName, JObject schema, JObject arguments)
    {
        if (schema["required"] is not JArray required)
        {
            return;
        }

        foreach (var requiredName in required.Values<string>().Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            if (arguments.TryGetValue(requiredName!, StringComparison.Ordinal, out _))
            {
                continue;
            }

            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                "string",
                "invalid_type",
                requiredName!,
                "Invalid input: expected string, received undefined"));
        }
    }

    private static void EnsureNoAdditionalProperties(string toolName, JObject schema, JObject arguments)
    {
        if (schema["additionalProperties"]?.Type != JTokenType.Boolean || schema.Value<bool>("additionalProperties"))
        {
            return;
        }

        var allowed = ((JObject?)schema["properties"])?.Properties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in arguments.Properties())
        {
            if (allowed.Contains(property.Name))
            {
                continue;
            }

            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                "never",
                "unrecognized_keys",
                property.Name,
                $"Unrecognized key: \"{property.Name}\""));
        }
    }

    private static void EnsurePropertyConstraints(string toolName, JObject schema, JObject arguments)
    {
        var properties = (JObject?)schema["properties"];
        if (properties is null)
        {
            return;
        }

        foreach (var property in properties.Properties())
        {
            if (!arguments.TryGetValue(property.Name, StringComparison.Ordinal, out var value) || property.Value is not JObject propertySchema)
            {
                continue;
            }

            EnsureSchemaMatches(toolName, property.Name, propertySchema, value);
        }
    }

    private static void EnsureSchemaMatches(string toolName, string propertyName, JObject schema, JToken value)
    {
        if (schema["anyOf"] is JArray anyOf)
        {
            foreach (var candidate in anyOf.OfType<JObject>())
            {
                if (Matches(candidate, value))
                {
                    return;
                }
            }

            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                DescribeAnyOf(anyOf.OfType<JObject>()),
                "invalid_union",
                propertyName,
                $"Invalid input for '{propertyName}'."));
        }

        EnsureType(toolName, propertyName, schema, value);
        EnsureEnum(toolName, propertyName, schema, value);
        EnsureStringConstraints(toolName, propertyName, schema, value);
        EnsureIntegerConstraints(toolName, propertyName, schema, value);
        EnsureArrayConstraints(toolName, propertyName, schema, value);
    }

    private static void EnsureType(string toolName, string propertyName, JObject schema, JToken value)
    {
        var expectedType = schema.Value<string>("type");
        if (string.IsNullOrWhiteSpace(expectedType))
        {
            return;
        }

        if (MatchesType(expectedType!, value))
        {
            return;
        }

        throw new McpArgumentValidationException(BuildSingleIssueMessage(
            toolName,
            expectedType!,
            "invalid_type",
            propertyName,
            $"Invalid input: expected {expectedType}, received {ToNodeTypeName(value.Type)}"));
    }

    private static void EnsureEnum(string toolName, string propertyName, JObject schema, JToken value)
    {
        if (schema["enum"] is not JArray enumValues)
        {
            return;
        }

        if (enumValues.Any(candidate => JToken.DeepEquals(candidate, value)))
        {
            return;
        }

        throw new McpArgumentValidationException(BuildSingleIssueMessage(
            toolName,
            "enum",
            "invalid_enum_value",
            propertyName,
            $"Invalid enum value. Expected one of: {string.Join(", ", enumValues.Select(candidate => candidate.ToString(Newtonsoft.Json.Formatting.None)))}"));
    }

    private static void EnsureStringConstraints(string toolName, string propertyName, JObject schema, JToken value)
    {
        if (value.Type != JTokenType.String)
        {
            return;
        }

        var text = value.Value<string>() ?? string.Empty;
        var minLength = schema.Value<int?>("minLength");
        if (minLength.HasValue && text.Length < minLength.Value)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                minLength.Value.ToString(),
                "too_small",
                propertyName,
                $"String must contain at least {minLength.Value} character(s)."));
        }
    }

    private static void EnsureIntegerConstraints(string toolName, string propertyName, JObject schema, JToken value)
    {
        if (value.Type != JTokenType.Integer)
        {
            return;
        }

        var number = value.Value<long>();
        var minimum = schema.Value<long?>("minimum");
        if (minimum.HasValue && number < minimum.Value)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                minimum.Value.ToString(),
                "too_small",
                propertyName,
                $"Number must be greater than or equal to {minimum.Value}."));
        }

        var maximum = schema.Value<long?>("maximum");
        if (maximum.HasValue && number > maximum.Value)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                maximum.Value.ToString(),
                "too_big",
                propertyName,
                $"Number must be less than or equal to {maximum.Value}."));
        }
    }

    private static void EnsureArrayConstraints(string toolName, string propertyName, JObject schema, JToken value)
    {
        if (value is not JArray array)
        {
            return;
        }

        var minItems = schema.Value<int?>("minItems");
        if (minItems.HasValue && array.Count < minItems.Value)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                minItems.Value.ToString(),
                "too_small",
                propertyName,
                $"Array must contain at least {minItems.Value} item(s)."));
        }

        var maxItems = schema.Value<int?>("maxItems");
        if (maxItems.HasValue && array.Count > maxItems.Value)
        {
            throw new McpArgumentValidationException(BuildSingleIssueMessage(
                toolName,
                maxItems.Value.ToString(),
                "too_big",
                propertyName,
                $"Array must contain at most {maxItems.Value} item(s)."));
        }

        if (schema["items"] is not JObject itemSchema)
        {
            return;
        }

        foreach (var item in array)
        {
            EnsureSchemaMatches(toolName, propertyName, itemSchema, item);
        }
    }

    private static bool Matches(JObject schema, JToken value)
    {
        if (schema["anyOf"] is JArray nestedAnyOf)
        {
            return nestedAnyOf.OfType<JObject>().Any(candidate => Matches(candidate, value));
        }

        var expectedType = schema.Value<string>("type");
        if (!string.IsNullOrWhiteSpace(expectedType) && !MatchesType(expectedType!, value))
        {
            return false;
        }

        if (schema["enum"] is JArray enumValues && !enumValues.Any(candidate => JToken.DeepEquals(candidate, value)))
        {
            return false;
        }

        if (value.Type == JTokenType.String)
        {
            var minLength = schema.Value<int?>("minLength");
            if (minLength.HasValue && (value.Value<string>()?.Length ?? 0) < minLength.Value)
            {
                return false;
            }
        }

        if (value.Type == JTokenType.Integer)
        {
            var number = value.Value<long>();
            var minimum = schema.Value<long?>("minimum");
            if (minimum.HasValue && number < minimum.Value)
            {
                return false;
            }

            var maximum = schema.Value<long?>("maximum");
            if (maximum.HasValue && number > maximum.Value)
            {
                return false;
            }
        }

        if (value is JArray array)
        {
            var minItems = schema.Value<int?>("minItems");
            if (minItems.HasValue && array.Count < minItems.Value)
            {
                return false;
            }

            var maxItems = schema.Value<int?>("maxItems");
            if (maxItems.HasValue && array.Count > maxItems.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesType(string expectedType, JToken value)
    {
        return expectedType switch
        {
            "string" => value.Type == JTokenType.String,
            "integer" => value.Type == JTokenType.Integer,
            "boolean" => value.Type == JTokenType.Boolean,
            "object" => value.Type == JTokenType.Object,
            "array" => value.Type == JTokenType.Array,
            "null" => value.Type == JTokenType.Null,
            _ => true
        };
    }

    private static string DescribeAnyOf(IEnumerable<JObject> anyOf)
    {
        return string.Join(" | ", anyOf.Select(candidate => candidate.Value<string>("type") ?? "value"));
    }

    private static string BuildSingleIssueMessage(string? toolName, string expected, string code, string path, string message)
    {
        var issue = "[\n  {\n" +
                    $"    \"expected\": \"{Escape(expected)}\",\n" +
                    $"    \"code\": \"{Escape(code)}\",\n" +
                    "    \"path\": [\n" +
                    $"      \"{Escape(path)}\"\n" +
                    "    ],\n" +
                    $"    \"message\": \"{Escape(message)}\"\n" +
                    "  }\n]";

        return toolName is null
            ? issue
            : $"MCP error -32602: Input validation error: Invalid arguments for tool {toolName}: {issue}";
    }

    private static string ToNodeTypeName(JTokenType type)
    {
        return type switch
        {
            JTokenType.String => "string",
            JTokenType.Integer or JTokenType.Float => "number",
            JTokenType.Boolean => "boolean",
            JTokenType.Array => "array",
            JTokenType.Object => "object",
            JTokenType.Null => "null",
            _ => type.ToString().ToLowerInvariant()
        };
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
