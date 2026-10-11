using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.AgentRuntime
{
    /// <summary>One host-owned provider instance per immutable installation identity.</summary>
    public sealed class AgentPluginLifetimeCache<T> where T : class
    {
        private sealed class Entry
        {
            public T Provider;
            public bool Active;
            public bool Seen;
        }
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Action<Exception> _retirementError;

        public AgentPluginLifetimeCache(Action<Exception> retirementError = null) => _retirementError = retirementError;

        public void BeginRefresh()
        {
            foreach (var entry in _entries.Values) entry.Seen = false;
        }

        public void RetainOnly(IEnumerable<string> identities)
        {
            var retained = new HashSet<string>(identities, StringComparer.Ordinal);
            foreach (var identity in _entries.Keys.Where(key => !retained.Contains(key)).ToArray())
            {
                var entry = _entries[identity];
                _entries.Remove(identity);
                Retire(entry);
            }
        }

        public T GetOrCreate(string identity, Func<T> factory)
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Installation identity is required.", nameof(identity));
            if (!_entries.TryGetValue(identity, out var entry))
            {
                entry = new Entry { Provider = factory() ?? throw new InvalidOperationException("Provider activation returned null.") };
                _entries.Add(identity, entry);
            }
            entry.Seen = true;
            return entry.Provider;
        }

        public void Activate(string identity)
        {
            var entry = _entries[identity];
            if (entry.Active) return;
            try
            {
                (entry.Provider as IAgentPluginLifecycle)?.Activate();
                entry.Active = true;
            }
            catch
            {
                // An activation hook may have acquired authority before throwing.
                Retire(entry, true);
                throw;
            }
        }

        public void Reject(string identity)
        {
            if (_entries.TryGetValue(identity, out var entry)) Retire(entry);
        }

        public void EndRefresh()
        {
            foreach (var identity in _entries.Where(item => !item.Value.Seen).Select(item => item.Key).ToArray())
            {
                var entry = _entries[identity];
                _entries.Remove(identity);
                Retire(entry);
            }
        }

        public T Find(Func<T, bool> predicate) => _entries.Values.Select(item => item.Provider).FirstOrDefault(predicate);

        private void Retire(Entry entry, bool force = false)
        {
            if (!entry.Active && !force) return;
            entry.Active = false;
            try { (entry.Provider as IAgentPluginLifecycle)?.Retire(); }
            catch (Exception exception) { _retirementError?.Invoke(exception); }
        }
    }
}
