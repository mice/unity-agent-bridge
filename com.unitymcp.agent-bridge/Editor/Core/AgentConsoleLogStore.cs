using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    [InitializeOnLoad]
    internal static class AgentConsoleLogStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<ConsoleLogEntry> Entries = new List<ConsoleLogEntry>();
        internal const int MaxEntriesPerType = 1000;
        private static long _sequence;

        static AgentConsoleLogStore()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        public static IReadOnlyList<ConsoleLogEntry> GetSnapshot(ConsoleLogQueryType queryType, int requestedCount)
        {
            lock (SyncRoot)
            {
                var filteredEntries = Entries
                    .Where(entry => MatchesQueryType(entry, queryType))
                    .OrderByDescending(entry => entry.TimestampUtcTicks)
                    .ThenByDescending(entry => entry.Sequence)
                    .ToArray();

                if (requestedCount == 0)
                {
                    return filteredEntries;
                }

                return filteredEntries
                    .Take(requestedCount)
                    .ToArray();
            }
        }

        public static IReadOnlyList<ConsoleLogEntry> GetCompilerEntries()
        {
            lock (SyncRoot)
            {
                return Entries
                    .Where(entry => entry.Type == LogType.Error || entry.Type == LogType.Assert || entry.Type == LogType.Exception)
                    .Where(entry => entry.Condition.IndexOf("error CS", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(entry => entry.TimestampUtcTicks)
                    .ThenByDescending(entry => entry.Sequence)
                    .ToArray();
            }
        }

        public static long GetLatestSequence()
        {
            lock (SyncRoot)
            {
                return Entries.Count == 0 ? 0L : Entries[Entries.Count - 1].Sequence;
            }
        }

        public static bool ContainsMessageSince(long sequenceExclusive, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            lock (SyncRoot)
            {
                foreach (var entry in Entries)
                {
                    if (entry.Sequence <= sequenceExclusive)
                    {
                        continue;
                    }

                    if ((entry.Condition ?? string.Empty).IndexOf(pattern, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static void ResetForTests()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
                _sequence = 0L;
            }
        }

        internal static void AppendTestEntry(string condition, string stackTrace, LogType type)
        {
            lock (SyncRoot)
            {
                AddEntry(condition, stackTrace, type);
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (SyncRoot)
            {
                AddEntry(condition, stackTrace, type);
            }
        }

        private static void AddEntry(string condition, string stackTrace, LogType type)
        {
            Entries.Add(new ConsoleLogEntry
            {
                Condition = condition ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty,
                Type = type,
                TimestampUtcTicks = DateTime.UtcNow.Ticks,
                Sequence = Interlocked.Increment(ref _sequence)
            });

            TrimBucket(type);
        }

        private static void TrimBucket(LogType type)
        {
            var queryType = Classify(type);
            var retained = 0;
            for (var index = Entries.Count - 1; index >= 0; index--)
            {
                if (Classify(Entries[index].Type) != queryType)
                {
                    continue;
                }

                retained++;
                if (retained > MaxEntriesPerType)
                {
                    Entries.RemoveAt(index);
                }
            }
        }

        private static bool MatchesQueryType(ConsoleLogEntry entry, ConsoleLogQueryType queryType)
        {
            return Classify(entry.Type) == queryType;
        }

        private static ConsoleLogQueryType Classify(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return ConsoleLogQueryType.Warning;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return ConsoleLogQueryType.Error;
                default:
                    return ConsoleLogQueryType.Info;
            }
        }
    }

    internal enum ConsoleLogQueryType
    {
        Error = 0,
        Warning = 1,
        Info = 2
    }

    [Serializable]
    internal sealed class ConsoleLogEntry
    {
        public string Condition;
        public string StackTrace;
        public LogType Type;
        public long TimestampUtcTicks;
        public long Sequence;
    }
}
