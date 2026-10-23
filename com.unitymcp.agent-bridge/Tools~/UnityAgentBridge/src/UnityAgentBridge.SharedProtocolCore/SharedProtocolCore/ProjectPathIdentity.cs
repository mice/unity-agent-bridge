using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UnityMcp.AgentBridge
{
    public static class ProjectPathIdentity
    {
        public static string Canonicalize(string path)
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(Path.Combine(full, "Assets")) || !File.Exists(Path.Combine(full, "ProjectSettings", "ProjectVersion.txt"))) throw new DirectoryNotFoundException("Unity project not found: " + full);
            if (Path.DirectorySeparatorChar == '\\')
            {
                using (var handle = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
                {
                    if (handle.IsInvalid) throw new IOException("Unable to resolve project directory.");
                    var buffer = new StringBuilder(32768);
                    var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                    if (length == 0 || length >= buffer.Capacity) throw new IOException("Unable to resolve project alias.");
                    full = buffer.ToString();
                    if (full.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) full = @"\\" + full.Substring(8);
                    else if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full.Substring(4);
                }
            }
            else
            {
                var resolved = realpath(full, IntPtr.Zero);
                if (resolved == IntPtr.Zero) throw new IOException("Unable to resolve project alias.");
                try { full = Marshal.PtrToStringAnsi(resolved); } finally { free(resolved); }
            }
            var root = Path.GetPathRoot(full);
            return (full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full).Replace('\\', '/');
        }
        public static string GetProjectId(string path)
        {
            var canonical = Canonicalize(path);
            if (Path.DirectorySeparatorChar == '\\') canonical = canonical.ToUpperInvariant();
            using (var hash = SHA256.Create()) return "project-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr realpath(string path, IntPtr buffer);
        [DllImport("libc")] private static extern void free(IntPtr pointer);
    }
}
