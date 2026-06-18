namespace UnityMcp.BuiltInPlugins.TestRunner
{
    internal static class TestRunnerSchemas
    {
        public const string RunEditModeTests = "{\"type\":\"object\",\"properties\":{\"filter\":{\"type\":\"string\",\"minLength\":1},\"testNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1}},\"assemblyNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1}},\"categoryNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1}},\"groupNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1}},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":9007199254740991}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}";

        public const string RunPlayModeTests = RunEditModeTests;

        public const string AgentBridgeSelfTest = "{\"type\":\"object\",\"properties\":{\"includeEditModeCase\":{\"type\":\"boolean\"},\"includeDiagnosticCase\":{\"type\":\"boolean\"},\"continueOnFailure\":{\"type\":\"boolean\"},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":9007199254740991}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}";
    }
}
