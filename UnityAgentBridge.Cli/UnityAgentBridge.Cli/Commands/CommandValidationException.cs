namespace UnityAgentBridge.Cli.Commands;

internal sealed class CommandValidationException : Exception
{
    public CommandValidationException(string message)
        : base(message)
    {
    }
}
