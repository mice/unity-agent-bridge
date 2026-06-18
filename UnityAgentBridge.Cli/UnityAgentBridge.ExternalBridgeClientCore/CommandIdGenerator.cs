using System.Threading;

namespace UnityAgentBridge.ExternalBridgeClientCore;

public static class CommandIdGenerator
{
    private static int _sequence;

    public static string Next()
    {
        return $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Environment.ProcessId:D5}_{Interlocked.Increment(ref _sequence):D6}";
    }
}
