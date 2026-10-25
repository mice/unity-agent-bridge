using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>Bounded atomic journal storage. Missing/corrupt committed state never means idle.</summary>
    public sealed class EditorExecutionJournalStore : IAgentExecutionJournalStore
    {
        public const int MaximumJournalBytes = 16 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string journalPath;
        private readonly string pendingPath;
        private readonly string initializedPath;

        public EditorExecutionJournalStore(string journalPath)
        {
            if (string.IsNullOrWhiteSpace(journalPath)) throw new ArgumentException("A journal path is required.", nameof(journalPath));
            this.journalPath = Path.GetFullPath(journalPath);
            pendingPath = this.journalPath + ".writing";
            initializedPath = this.journalPath + ".initialized";
        }

        public AgentExecutionJournal Read()
        {
            if (!File.Exists(journalPath))
            {
                if (File.Exists(initializedPath) || File.Exists(pendingPath))
                    throw new IOException("Scheduler journal is missing after initialization or an interrupted write. Inspect the retained evidence; do not recreate an idle journal.");
                return null;
            }
            using (var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > MaximumJournalBytes) throw new InvalidDataException("Scheduler journal exceeds its size bound or is empty.");
                using (var reader = new StreamReader(stream, Utf8, false))
                using (var json = new JsonTextReader(reader) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
                {
                    var document = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (json.Read()) throw new InvalidDataException("Unexpected trailing scheduler journal content.");
                    return document.ToObject<AgentExecutionJournal>() ?? throw new InvalidDataException("Scheduler journal is null.");
                }
            }
        }

        public void Write(AgentExecutionJournal journal)
        {
            if (journal == null) throw new ArgumentNullException(nameof(journal));
            var bytes = Utf8.GetBytes(JsonConvert.SerializeObject(journal, Formatting.None));
            if (bytes.Length > MaximumJournalBytes) throw new InvalidDataException("Scheduler journal exceeds the 16 MiB storage bound.");
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath));
            using (var stream = new FileStream(pendingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(journalPath)) File.Replace(pendingPath, journalPath, null);
            else File.Move(pendingPath, journalPath);
            // Preserve evidence that a later missing primary is not a pristine installation.
            using (var marker = new FileStream(initializedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                marker.WriteByte(1);
                marker.Flush(true);
            }
        }
    }
}
