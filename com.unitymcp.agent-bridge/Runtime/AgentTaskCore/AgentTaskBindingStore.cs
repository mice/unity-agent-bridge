using System;
using System.Collections.Generic;
using System.IO;

namespace UnityMcp.AgentTaskCore
{
    public interface IAgentTaskBindingStore
    {
        void Save(AgentTaskBinding binding);
        AgentTaskBinding Load(string projectIdentity, string externalTaskId);
        int Cleanup(Func<AgentTaskBinding, bool> shouldRemove);
    }

    public sealed class FileAgentTaskBindingStore : IAgentTaskBindingStore
    {
        private readonly string root;
        public FileAgentTaskBindingStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Store root is required.", nameof(root));
            this.root = root;
            Directory.CreateDirectory(root);
        }

        public void Save(AgentTaskBinding binding)
        {
            Validate(binding);
            var path = PathFor(binding.ProjectIdentity, binding.ExternalTaskId);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, SerializeBinding(binding));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public AgentTaskBinding Load(string projectIdentity, string externalTaskId)
        {
            var path = PathFor(projectIdentity, externalTaskId);
            if (!File.Exists(path)) return null;
            return DeserializeBinding(File.ReadAllText(path));
        }

        public int Cleanup(Func<AgentTaskBinding, bool> shouldRemove)
        {
            if (shouldRemove == null) throw new ArgumentNullException(nameof(shouldRemove));
            var removed = 0;
            foreach (var path in Directory.GetFiles(root, "*.binding"))
            {
                AgentTaskBinding binding;
                try { binding = DeserializeBinding(File.ReadAllText(path)); }
                catch { continue; }
                if (shouldRemove(binding)) { File.Delete(path); removed++; }
            }
            return removed;
        }

        private string PathFor(string projectIdentity, string externalTaskId)
        {
            ValidatePart(projectIdentity, nameof(projectIdentity));
            ValidatePart(externalTaskId, nameof(externalTaskId));
            return Path.Combine(root, Hash(projectIdentity + "\n" + externalTaskId) + ".binding");
        }

        private static void Validate(AgentTaskBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (binding.Version != AgentTaskControlProtocol.CurrentVersion) throw new InvalidDataException("Unsupported binding version.");
            ValidatePart(binding.ProjectIdentity, nameof(binding.ProjectIdentity));
            ValidatePart(binding.ExternalTaskId, nameof(binding.ExternalTaskId));
            ValidatePart(binding.AgentTaskId, nameof(binding.AgentTaskId));
        }

        private static void ValidatePart(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid binding value.", name);
        }

        private static string Hash(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var character in value) hash = (hash ^ character) * 16777619u;
                return hash.ToString("x8");
            }
        }

        private string SerializeBinding(AgentTaskBinding binding) => string.Join("\n", binding.Version.ToString(), binding.ProjectIdentity, binding.ExternalTaskId, binding.AgentTaskId, binding.CommandId ?? string.Empty);
        private static AgentTaskBinding DeserializeBinding(string value)
        {
            var fields = value.Split(new[] { '\n' }, StringSplitOptions.None);
            if (fields.Length < 5) throw new InvalidDataException("Invalid binding record.");
            int version;
            if (!int.TryParse(fields[0], out version)) throw new InvalidDataException("Invalid binding version.");
            var binding = new AgentTaskBinding { Version = version, ProjectIdentity = fields[1], ExternalTaskId = fields[2], AgentTaskId = fields[3], CommandId = fields[4] };
            Validate(binding);
            return binding;
        }
    }
}
