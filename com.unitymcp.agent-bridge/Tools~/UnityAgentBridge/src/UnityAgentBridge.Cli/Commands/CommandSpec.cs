namespace UnityAgentBridge.Cli.Commands;

internal sealed record CommandSpec(string Tool, int TimeoutMs, string ArgsJson);

internal sealed record ParsedCommand(CommandSpec Spec, GlobalOptions Global);

internal sealed class GlobalOptions
{
    public string? ProjectPath { get; set; }

    public string? QueueRoot { get; set; }

    public string OutputFormat { get; set; } = "json";
}
