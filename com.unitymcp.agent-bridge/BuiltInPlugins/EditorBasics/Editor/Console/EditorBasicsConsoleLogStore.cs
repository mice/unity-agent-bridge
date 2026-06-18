using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    [InitializeOnLoad]
    public static class EditorBasicsConsoleLogStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<ConsoleLogEntry> Entries = new List<ConsoleLogEntry>();
        public const int MaxEntriesPerType = 1000;
        private static long _sequence;

        static EditorBasicsConsoleLogStore()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        public static IReadOnlyList<ConsoleLogEntry> GetSnapshot(ConsoleLogQueryType queryType, int requestedCount, string filter = null)
        {
            lock (SyncRoot)
            {
                var filteredEntries = Entries
                    .Where(entry => MatchesQueryType(entry, queryType))
                    .Where(entry => MatchesFilter(entry, filter))
                    .OrderByDescending(entry => entry.TimestampUtcTicks)
                    .ThenByDescending(entry => entry.Sequence)
                    .ToArray();

                return requestedCount == 0 ? filteredEntries : filteredEntries.Take(requestedCount).ToArray();
            }
        }

        public static void ResetForTests()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
                _sequence = 0L;
            }
        }

        public static void AppendTestEntry(string condition, string stackTrace, LogType type)
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

        private static bool MatchesFilter(ConsoleLogEntry entry, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return (entry.Condition != null && entry.Condition.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (entry.StackTrace != null && entry.StackTrace.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
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
}
