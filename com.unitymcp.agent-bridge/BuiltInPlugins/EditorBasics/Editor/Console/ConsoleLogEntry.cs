using System;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    public enum ConsoleLogQueryType
    {
        Error = 0,
        Warning = 1,
        Info = 2
    }

    [Serializable]
    public sealed class ConsoleLogEntry
    {
        public string Condition;
        public string StackTrace;
        public LogType Type;
        public long TimestampUtcTicks;
        public long Sequence;
    }
}
