using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace UnityMcp.AgentBridge
{
    internal sealed class CompileLifecycleStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly string _statePath;

        public CompileLifecycleStore(string projectRoot, string tempRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("projectRoot is required.", nameof(projectRoot));
            }

            if (string.IsNullOrWhiteSpace(tempRoot))
            {
                throw new ArgumentException("tempRoot is required.", nameof(tempRoot));
            }

            var queueRoot = Path.GetFullPath(Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar)));
            _statePath = Path.Combine(queueRoot, "status", "compile_lifecycle_state.json");
        }

        public string StatePath => _statePath;

        public CompileLifecycleState Read()
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<CompileLifecycleState>(File.ReadAllText(_statePath, Utf8NoBom));
            }
            catch
            {
                return null;
            }
        }

        public void Write(CompileLifecycleState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_statePath) ?? string.Empty);
            var tempPath = _statePath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(JsonConvert.SerializeObject(state, Formatting.None));
            }

            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
            }

            File.Move(tempPath, _statePath);
        }
    }
}
