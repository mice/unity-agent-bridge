using System;
using System.Collections.Generic;
using System.Text;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class ProcessOutputBuffer
    {
        private readonly object _sync = new object();
        private readonly List<string> _stdoutLines = new List<string>();
        private readonly List<string> _stderrLines = new List<string>();
        private readonly int _maxLines;

        public ProcessOutputBuffer()
            : this(512)
        {
        }

        public ProcessOutputBuffer(int maxLines)
        {
            _maxLines = Math.Max(1, maxLines);
        }

        public void AppendStdout(string line)
        {
            Append(_stdoutLines, line);
        }

        public void AppendStderr(string line)
        {
            Append(_stderrLines, line);
        }

        public string GetStdout()
        {
            return Join(_stdoutLines);
        }

        public string GetStderr()
        {
            return Join(_stderrLines);
        }

        private void Append(List<string> target, string line)
        {
            lock (_sync)
            {
                target.Add(line ?? string.Empty);
                if (target.Count > _maxLines)
                {
                    target.RemoveAt(0);
                }
            }
        }

        private string Join(List<string> source)
        {
            lock (_sync)
            {
                var builder = new StringBuilder();
                for (var index = 0; index < source.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(source[index]);
                }

                return builder.ToString();
            }
        }
    }
}
