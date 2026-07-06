namespace UnityMcp.BuiltInPlugins.LuaTools
{
    internal static class LuaToolsSchemas
    {
        public const string Lint =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.lua.lint args\",\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"path\"],\"properties\":{\"path\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Project-relative Lua file or directory under Assets/ or Packages/.\"},\"checks\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"gc\"]},\"default\":[],\"description\":\"Optional lint families. Omitted or empty runs all supported checks.\"},\"failOn\":{\"type\":\"string\",\"enum\":[\"error\",\"warning\"],\"default\":\"error\"},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":300000,\"default\":30000},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":500,\"default\":50},\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0}}}";

        public const string Compile =
            "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"title\":\"unity.lua.compile args\",\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"path\":{\"type\":[\"string\",\"null\"],\"minLength\":1,\"description\":\"Optional project-relative Lua file or directory under Assets/ or Packages/. When omitted, configured Lua source roots are used.\"},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":300000,\"default\":30000},\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":500,\"default\":50},\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0}}}";
    }
}
