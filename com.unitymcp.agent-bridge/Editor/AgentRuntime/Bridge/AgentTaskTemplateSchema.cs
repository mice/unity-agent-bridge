using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityMcp.AgentBridge
{
    /// <summary>Closed draft-07 subset. Unknown keywords fail closed, including $ref and combinators.</summary>
    public static class AgentTaskTemplateSchema
    {
        public const string Dialect = "http://json-schema.org/draft-07/schema#";
        private static readonly HashSet<string> Keywords = new HashSet<string>(new[]
        { "$schema", "type", "description", "properties", "required", "additionalProperties", "items", "enum", "minLength", "maxLength", "minItems", "maxItems", "minimum", "maximum" }, StringComparer.Ordinal);
        public static void Check(JToken schema, int depth = 0)
        {
            if (depth > 16 || !(schema is JObject node)) throw new ArgumentException("Schema must be an object with depth <= 16.");
            if (node.Properties().Any(p => !Keywords.Contains(p.Name))) throw new ArgumentException("Unsupported schema keyword.");
            if (node["$schema"] != null && (node["$schema"].Type != JTokenType.String || (string)node["$schema"] != Dialect)) throw new ArgumentException("Unsupported schema dialect.");
            var type = node.Value<string>("type");
            if (!new[] { "object", "array", "string", "integer", "number", "boolean", "null" }.Contains(type)) throw new ArgumentException("One concrete schema type is required.");
            if (node["description"] != null && node["description"].Type != JTokenType.String) throw new ArgumentException("Schema description must be a string.");
            foreach (var key in new[] { "minLength", "maxLength", "minItems", "maxItems" })
                if (node[key] != null && (node[key].Type != JTokenType.Integer || node.Value<long>(key) < 0 || node.Value<long>(key) > int.MaxValue)) throw new ArgumentException("Invalid schema bound: " + key);
            foreach (var key in new[] { "minimum", "maximum" })
                if (node[key] != null && node[key].Type != JTokenType.Integer && node[key].Type != JTokenType.Float) throw new ArgumentException("Invalid numeric bound.");
            foreach (var pair in new[] { new[] { "minLength", "maxLength" }, new[] { "minItems", "maxItems" }, new[] { "minimum", "maximum" } })
                if (node[pair[0]] != null && node[pair[1]] != null && node.Value<decimal>(pair[0]) > node.Value<decimal>(pair[1])) throw new ArgumentException("Inverted schema bounds.");
            if (node["enum"] != null && (!(node["enum"] is JArray choices) || choices.Count == 0)) throw new ArgumentException("enum must be a nonempty array.");
            if (type != "object" && new[] { "properties", "required", "additionalProperties" }.Any(k => node[k] != null) ||
                type != "array" && new[] { "items", "minItems", "maxItems" }.Any(k => node[k] != null) ||
                type != "string" && new[] { "minLength", "maxLength" }.Any(k => node[k] != null) ||
                type != "integer" && type != "number" && new[] { "minimum", "maximum" }.Any(k => node[k] != null)) throw new ArgumentException("Schema keyword is incompatible with type.");
            if (type == "object")
            {
                if (!(node["properties"] is JObject properties) || node["additionalProperties"]?.Type != JTokenType.Boolean || node.Value<bool>("additionalProperties")) throw new ArgumentException("Objects require properties and additionalProperties:false.");
                foreach (var property in properties.Properties()) Check(property.Value, depth + 1);
                if (node["required"] != null)
                {
                    if (!(node["required"] is JArray required) || required.Any(t => t.Type != JTokenType.String || properties[(string)t] == null) || required.Values<string>().Distinct().Count() != required.Count) throw new ArgumentException("Invalid required properties.");
                }
            }
            if (type == "array") { if (node["items"] == null) throw new ArgumentException("Array items schema is required."); Check(node["items"], depth + 1); }
        }

        public static void Validate(JToken schema, JToken value, string path = "$")
        {
            var type = schema.Value<string>("type");
            var valid = value != null && (type == "object" ? value.Type == JTokenType.Object : type == "array" ? value.Type == JTokenType.Array :
                type == "string" ? value.Type == JTokenType.String : type == "integer" ? value.Type == JTokenType.Integer :
                type == "number" ? value.Type == JTokenType.Float || value.Type == JTokenType.Integer : type == "boolean" ? value.Type == JTokenType.Boolean : value.Type == JTokenType.Null);
            if (!valid) throw new ArgumentException(path + ": expected " + type);
            if (schema["enum"] is JArray choices && !choices.Any(item => JToken.DeepEquals(item, value))) throw new ArgumentException(path + ": enum mismatch.");
            if (value is JObject obj)
            {
                var properties = (JObject)schema["properties"];
                foreach (var required in (schema["required"] as JArray ?? new JArray()).Values<string>()) if (obj[required] == null) throw new ArgumentException(path + ": missing " + required);
                foreach (var property in obj.Properties())
                { if (properties[property.Name] == null) throw new ArgumentException(path + ": unknown " + property.Name); Validate(properties[property.Name], property.Value, path + "." + property.Name); }
            }
            if (value is JArray array)
            { Bound(schema, "minItems", "maxItems", array.Count, path); foreach (var item in array) Validate(schema["items"], item, path + "[]"); }
            if (value.Type == JTokenType.String) Bound(schema, "minLength", "maxLength", value.Value<string>().Length, path);
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) Bound(schema, "minimum", "maximum", value.Value<decimal>(), path);
        }
        private static void Bound(JToken schema, string min, string max, decimal value, string path)
        { if (schema[min] != null && value < schema.Value<decimal>(min) || schema[max] != null && value > schema.Value<decimal>(max)) throw new ArgumentException(path + ": bound exceeded."); }
    }
}
