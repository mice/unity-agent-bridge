namespace UnityAgentBridge.Cli.Commands;

internal static class CommandContract
{
    public static readonly string[] GoldenCommands =
    [
        "ping",
        "project_info",
        "compile",
        "console",
        "assetdatabase_search",
        "get_editor_state",
        "open_scene",
        "get_hierarchy",
        "read_report",
        "get_selection_info",
        "get_gameobject_component_info",
        "run-static",
        "diagnostic",
        "test-edit",
        "test-play",
        "self-test",
        "bridge-health",
        "bridge-submit-only",
        "bridge-wait-result",
        "mcp-echo"
    ];

    public static void WriteHelp(TextWriter writer, string executableName)
    {
        writer.WriteLine("Unity Agent Bridge CLI");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine($"  {executableName} ping [--project-path <path>] [--queue-root <path>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} project_info [--project-path <path>] [--queue-root <path>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} compile [--project-path <path>] [--queue-root <path>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} console [--type <error|warning|info>]... [--count <n>] [--filter <text>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} assetdatabase_search --query <value> [--folder <Assets/...>]... [--offset <n>] [--limit <n>] [--include-details] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} get_editor_state [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} open_scene --scene-path <Assets/...unity> [--mode <single|additive>] [--set-active <true|false>] [--save-modified-scenes <true|false>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} get_hierarchy [--locator <value>] [--max-depth <n>] [--limit <n>] [--include-components] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} read_report --report-path <Temp/AgentBridge/reports/...json> [--json-pointer </path>] [--offset <n>] [--limit <n>] [--max-bytes <n>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} get_selection_info [--include-details] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} get_gameobject_component_info [--locator <value>] [--component-name <value>] [--component-index <n>] [--property-mode <debug|serialized>] [--property-limit <n>] [--array-element-limit <n>] [--string-max-length <n>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} run-static <Type.Method> [--parameters <json>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} run-static <TypeName> <MethodName> [--parameters <json>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} diagnostic <diagnosticType> <targetPath> [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} test-edit [filter] [--test-name <value>]... [--assembly <value>]... [--category <value>]... [--group <value>]... [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} test-play [filter] [--test-name <value>]... [--assembly <value>]... [--category <value>]... [--group <value>]... [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} self-test [--no-editmode] [--no-diagnostic] [--stop-on-failure] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} bridge-health [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} bridge-submit-only <tool> [--args <json>] [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} bridge-wait-result <commandId> [--timeout-ms <n>]");
        writer.WriteLine($"  {executableName} mcp-echo [--payload <json>] [--timeout-ms <n>]");
        writer.WriteLine();
        writer.WriteLine("Global options:");
        writer.WriteLine("  --project-path <path>  Optional Unity project path.");
        writer.WriteLine("  --queue-root <path>    Relative queue root. Default: Temp/AgentBridge");
        writer.WriteLine("  --output <json|text>   Output format. Default: json");
    }
}
