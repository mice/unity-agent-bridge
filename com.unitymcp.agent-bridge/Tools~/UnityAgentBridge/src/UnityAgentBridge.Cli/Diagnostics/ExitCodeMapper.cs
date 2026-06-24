namespace UnityAgentBridge.Cli.Diagnostics;

internal static class ExitCodeMapper
{
    public static int Map(string status)
    {
        return status switch
        {
            "success" => 0,
            "failed" => 1,
            "timeout" => 2,
            "invalid_args" => 3,
            "unsupported" => 4,
            "blocked" => 5,
            "exception" => 6,
            "cancelled" => 7,
            _ => 6
        };
    }

    public static bool IsKnownStatus(string status)
    {
        return status is "success" or "failed" or "timeout" or "invalid_args" or "unsupported" or "blocked" or "exception" or "cancelled";
    }
}
