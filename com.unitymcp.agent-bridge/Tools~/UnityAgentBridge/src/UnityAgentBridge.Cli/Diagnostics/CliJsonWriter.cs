using Newtonsoft.Json.Linq;

namespace UnityAgentBridge.Cli.Diagnostics;

internal static class CliJsonWriter
{
    public static TextWriter Stdout { get; set; } = Console.Out;

    public static TextWriter Stderr { get; set; } = Console.Error;

    public static void WriteStdout(string rawJson)
    {
        Stdout.WriteLine(rawJson);
    }

    public static void WriteResult(string rawJson, string outputFormat)
    {
        if (string.Equals(outputFormat, "text", StringComparison.Ordinal))
        {
            WriteText(rawJson);
            return;
        }

        WriteStdout(rawJson);
    }

    public static void WriteDiagnostic(string message)
    {
        Stderr.WriteLine(message);
    }

    private static void WriteText(string rawJson)
    {
        var payload = JObject.Parse(rawJson);
        var status = payload.Value<string>("status") ?? string.Empty;
        var summary = payload.Value<string>("summary") ?? string.Empty;
        var tool = payload.Value<string>("tool") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(tool))
        {
            Stdout.WriteLine($"{tool}: {status}");
        }
        else
        {
            Stdout.WriteLine(status);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            Stdout.WriteLine(summary);
        }
    }
}
