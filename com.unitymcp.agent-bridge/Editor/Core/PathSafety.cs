using System;
using System.IO;

namespace UnityMcp.AgentBridge
{
    public static class PathSafety
    {
        public static string Normalize(string projectRoot, string inputPath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("projectRoot is required.", nameof(projectRoot));
            }

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("inputPath is required.", nameof(inputPath));
            }

            var sanitized = inputPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(sanitized))
            {
                throw new ArgumentException("Absolute paths are not allowed.", nameof(inputPath));
            }

            if (sanitized.Contains("../", StringComparison.Ordinal) ||
                sanitized.Contains("..\\", StringComparison.Ordinal) ||
                string.Equals(sanitized, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException("Parent traversal is not allowed.", nameof(inputPath));
            }

            if (!sanitized.StartsWith("Assets/", StringComparison.Ordinal) &&
                !sanitized.StartsWith("Packages/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Path must start with Assets/ or Packages/.", nameof(inputPath));
            }

            var fullProjectRoot = Path.GetFullPath(projectRoot);
            var candidateFullPath = Path.GetFullPath(Path.Combine(fullProjectRoot, sanitized.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidateFullPath.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Path resolves outside the project root.", nameof(inputPath));
            }

            RejectReparsePoints(fullProjectRoot, candidateFullPath);
            return sanitized;
        }

        private static void RejectReparsePoints(string fullProjectRoot, string candidateFullPath)
        {
            var rootDirectory = new DirectoryInfo(fullProjectRoot);
            var relativePath = Path.GetRelativePath(fullProjectRoot, candidateFullPath);
            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            var current = rootDirectory.FullName;
            for (var index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    break;
                }

                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException("Symbolic links are not allowed.", nameof(candidateFullPath));
                }
            }
        }
    }
}
