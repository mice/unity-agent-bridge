using System.Globalization;

namespace UnityAgentBridge.Cli.Commands;

internal sealed class TokenReader
{
    private readonly string[] _args;
    private int _index;

    public TokenReader(string[] args, int startIndex)
    {
        _args = args;
        _index = startIndex;
    }

    public bool HasMore => _index < _args.Length;

    public string Peek()
    {
        if (!HasMore)
        {
            throw new CommandValidationException("Unexpected end of arguments.");
        }

        return _args[_index];
    }

    public bool PeekStartsWithDash()
    {
        return HasMore && Peek().StartsWith("-", StringComparison.Ordinal);
    }

    public bool TryConsumeFlag(string optionName)
    {
        if (HasMore && string.Equals(Peek(), optionName, StringComparison.Ordinal))
        {
            _index++;
            return true;
        }

        return false;
    }

    public bool TryConsumeStringOption(string optionName, out string value)
    {
        if (TryConsumeFlag(optionName))
        {
            value = ConsumeRequiredValue(optionName);
            return true;
        }

        value = string.Empty;
        return false;
    }

    public bool TryConsumeIntOption(string optionName, out int value)
    {
        if (TryConsumeStringOption(optionName, out var rawValue))
        {
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new CommandValidationException($"{optionName} must be an integer.");
            }

            return true;
        }

        value = default;
        return false;
    }

    public bool TryConsumeGlobalOption(GlobalOptions global)
    {
        if (TryConsumeStringOption("--project-path", out var projectPath))
        {
            global.ProjectPath = projectPath;
            return true;
        }

        if (TryConsumeStringOption("--queue-root", out var queueRoot))
        {
            global.QueueRoot = queueRoot;
            return true;
        }

        if (TryConsumeStringOption("--output", out var outputFormat))
        {
            if (!string.Equals(outputFormat, "json", StringComparison.Ordinal) &&
                !string.Equals(outputFormat, "text", StringComparison.Ordinal))
            {
                throw new CommandValidationException("--output must be one of: json, text.");
            }

            global.OutputFormat = outputFormat;
            return true;
        }

        return false;
    }

    public string ConsumeRequiredValue(string label)
    {
        if (!HasMore)
        {
            throw new CommandValidationException($"Missing value for {label}.");
        }

        var value = _args[_index];
        _index++;
        return value;
    }

    public void EnsureFullyConsumed()
    {
        if (HasMore)
        {
            throw new CommandValidationException($"Unexpected argument '{Peek()}'.");
        }
    }
}
