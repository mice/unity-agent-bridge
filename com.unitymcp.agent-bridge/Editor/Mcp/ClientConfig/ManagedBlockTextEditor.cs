using System;
using System.Text;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class ManagedBlockTextEditor
    {
        internal const string BeginMarker = "# BEGIN UNITY AGENT BRIDGE MANAGED";
        internal const string EndMarker = "# END UNITY AGENT BRIDGE MANAGED";

        public string Apply(string originalText, string managedBlock)
        {
            if (managedBlock == null)
            {
                throw new ArgumentNullException(nameof(managedBlock));
            }

            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            var cleaned = Remove(normalized).TrimEnd();
            var block = BuildManagedBlock(managedBlock);

            if (string.IsNullOrEmpty(cleaned))
            {
                return block + Environment.NewLine;
            }

            var builder = new StringBuilder();
            builder.Append(cleaned);
            builder.Append(Environment.NewLine);
            builder.Append(Environment.NewLine);
            builder.Append(block);
            builder.Append(Environment.NewLine);
            return builder.ToString();
        }

        public string Remove(string originalText)
        {
            var normalized = NormalizeLineEndings(originalText ?? string.Empty);
            var beginIndex = normalized.IndexOf(BeginMarker, StringComparison.Ordinal);
            if (beginIndex < 0)
            {
                return normalized;
            }

            var endIndex = normalized.IndexOf(EndMarker, beginIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                return normalized;
            }

            endIndex += EndMarker.Length;
            if (endIndex < normalized.Length && normalized[endIndex] == '\n')
            {
                endIndex++;
            }

            var before = normalized.Substring(0, beginIndex).TrimEnd('\n');
            var after = normalized.Substring(endIndex).TrimStart('\n');
            if (string.IsNullOrEmpty(before))
            {
                return after;
            }

            if (string.IsNullOrEmpty(after))
            {
                return before;
            }

            return before + Environment.NewLine + Environment.NewLine + after;
        }

        private static string BuildManagedBlock(string managedBlock)
        {
            var blockBody = NormalizeLineEndings(managedBlock).Trim();
            return BeginMarker + Environment.NewLine + blockBody + Environment.NewLine + EndMarker;
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        }
    }
}
