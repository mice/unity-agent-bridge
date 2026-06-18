using System;
using System.Text.RegularExpressions;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class ToolVersionParser
    {
        private static readonly Regex VersionPattern = new Regex(@"(?<!\d)(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.Compiled);

        public Version Parse(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            var match = VersionPattern.Match(rawVersion);
            if (!match.Success)
            {
                return null;
            }

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            var build = int.Parse(match.Groups[3].Value);

            if (match.Groups[4].Success)
            {
                var revision = int.Parse(match.Groups[4].Value);
                return new Version(major, minor, build, revision);
            }

            return new Version(major, minor, build);
        }

        public bool MeetsMinimumVersion(string rawVersion, Version minimumVersion)
        {
            if (minimumVersion == null)
            {
                throw new ArgumentNullException(nameof(minimumVersion));
            }

            var parsed = Parse(rawVersion);
            return parsed != null && parsed.CompareTo(minimumVersion) >= 0;
        }
    }
}
