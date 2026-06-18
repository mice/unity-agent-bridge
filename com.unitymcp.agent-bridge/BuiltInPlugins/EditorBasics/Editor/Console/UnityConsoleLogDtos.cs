using System;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    [Serializable]
    public sealed class UnityConsoleLogArgs
    {
        public string[] types = { "error" };
        public int count = 100;
    }

    [Serializable]
    public sealed class UnityConsoleLogMetrics
    {
        public string[] requestedTypes;
        public int requestedCountPerType;
        public UnityConsoleLogBucket[] results;
    }

    [Serializable]
    public sealed class UnityConsoleLogBucket
    {
        public string type;
        public int returnedCount;
        public UnityConsoleLogEntry[] entries;
    }

    [Serializable]
    public sealed class UnityConsoleLogEntry
    {
        public string condition;
        public string stackTrace;
        public string type;
        public string timestamp;
    }
}
