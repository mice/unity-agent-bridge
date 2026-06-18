namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class UnityQueriesSchemas
    {
        public const string AssetDatabaseSearch =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.assetdatabase_search args\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Unity AssetDatabase search filter, for example 't:Prefab Player'.\"},\"folders\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1},\"description\":\"Optional Unity project folders to search, each of which must resolve under Assets/.\"},\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":200,\"default\":20},\"includeDetails\":{\"type\":\"boolean\",\"default\":false,\"description\":\"When true, includes bounded importer, dependency, and sub-asset detail in the report payload.\"}}}";

        public const string GetHierarchy =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.get_hierarchy args\",\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"locator\":{\"type\":[\"string\",\"null\"],\"description\":\"Hierarchy target locator. Omitted, null, or empty string resolves to currentScene.\"},\"maxDepth\":{\"type\":\"integer\",\"minimum\":0,\"default\":4},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5000,\"default\":150},\"includeComponents\":{\"type\":\"boolean\",\"default\":false}}}";

        public const string GetGameObjectComponentInfo =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.get_gameobject_component_info args\",\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"locator\":{\"type\":[\"string\",\"null\"],\"minLength\":1,\"description\":\"Canonical GameObject locator string. Null or omitted means selection:active.\"},\"componentName\":{\"type\":[\"string\",\"null\"]},\"componentIndex\":{\"type\":[\"integer\",\"null\"],\"minimum\":0},\"propertyMode\":{\"type\":\"string\",\"enum\":[\"debug\",\"serialized\"],\"default\":\"debug\"},\"propertyLimit\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":1000,\"default\":200},\"arrayElementLimit\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":200,\"default\":20},\"stringMaxLength\":{\"type\":\"integer\",\"minimum\":16,\"maximum\":4000,\"default\":300}}}";

        public const string GetSelectionInfo =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.get_selection_info args\",\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"includeDetails\":{\"type\":\"boolean\",\"default\":false,\"description\":\"When true, emits full selection identity fields in the report payload.\"}}}";

        public const string ReadReport =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.read_report args\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"reportPath\"],\"properties\":{\"reportPath\":{\"type\":\"string\",\"minLength\":1},\"jsonPointer\":{\"type\":\"string\",\"default\":\"\"},\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":500,\"default\":100},\"maxBytes\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":262144,\"default\":65536}}}";
    }
}
