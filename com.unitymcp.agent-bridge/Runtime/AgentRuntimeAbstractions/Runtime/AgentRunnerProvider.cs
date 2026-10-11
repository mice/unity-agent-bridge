using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UnityMcp.AgentRuntime
{
    /// <summary>Host authority captured before an optional provider prepares a runner.</summary>
    public sealed class AgentRunnerPolicy
    {
        public AgentRunnerPolicy(string templateId, int timeoutMilliseconds, int maxTurns, int maxOutputBytes,
            IEnumerable<string> tools, IEnumerable<string> readFolders, IEnumerable<string> denyFolders)
        {
            if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentException("Template identity is required.", nameof(templateId));
            if (timeoutMilliseconds < 1 || maxTurns < 1 || maxOutputBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "Execution budgets must be positive.");
            TemplateId = templateId;
            TimeoutMilliseconds = timeoutMilliseconds;
            MaxTurns = maxTurns;
            MaxOutputBytes = maxOutputBytes;
            Tools = Capture(tools, nameof(tools));
            ReadFolders = Capture(readFolders, nameof(readFolders));
            DenyFolders = Capture(denyFolders, nameof(denyFolders));
        }

        public string TemplateId { get; }
        public int TimeoutMilliseconds { get; }
        public int MaxTurns { get; }
        public int MaxOutputBytes { get; }
        public ReadOnlyCollection<string> Tools { get; }
        public ReadOnlyCollection<string> ReadFolders { get; }
        public ReadOnlyCollection<string> DenyFolders { get; }

        private static ReadOnlyCollection<string> Capture(IEnumerable<string> values, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            var copy = values.ToArray();
            if (copy.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Policy values must be non-empty.", name);
            return Array.AsReadOnly(copy);
        }
    }

    public interface IAgentRunnerProvider
    {
        string Id { get; }
        IEnumerable<string> TemplateIds { get; }
        // Prepare validates and captures configuration; Create must return an independent runner per execution.
        IAgentRunnerFactory Prepare(AgentRunnerPolicy policy);
    }

    public sealed class AgentProviderException : Exception
    {
        public AgentProviderException(string code, string message) : base(message)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "AGENT_PROVIDER_FAILED" : code;
        }
        public string Code { get; }
    }

    /// <summary>Optional interfaces; existing tool-only plugins need not implement them.</summary>
    public interface IAgentPluginLifecycle
    {
        void Activate();
        void Retire();
    }

    public interface IAgentPluginConfiguration
    {
        void OpenConfiguration();
    }

    /// <summary>Registration identity is never transferred to a replacement provider.</summary>
    public sealed class AgentRunnerProviderRegistration : IDisposable
    {
        internal AgentRunnerProviderRegistration(IAgentRunnerProvider provider, string[] templateIds)
        {
            Provider = provider;
            TemplateIds = Array.AsReadOnly(templateIds);
            IsActive = true;
        }
        public IAgentRunnerProvider Provider { get; }
        public ReadOnlyCollection<string> TemplateIds { get; }
        public bool IsActive { get; internal set; }
        public void Dispose() => AgentRunnerProviderRegistry.Retire(this);
    }

    public static class AgentRunnerProviderRegistry
    {
        private static readonly Dictionary<string, AgentRunnerProviderRegistration> Providers =
            new Dictionary<string, AgentRunnerProviderRegistration>(StringComparer.Ordinal);

        public static event Action<AgentRunnerProviderRegistration> Retired;

        public static AgentRunnerProviderRegistration Register(IAgentRunnerProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrWhiteSpace(provider.Id)) throw new ArgumentException("Provider identity is required.", nameof(provider));
            var templates = provider.TemplateIds?.Distinct(StringComparer.Ordinal).ToArray();
            if (templates == null || templates.Length == 0 || templates.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Provider must declare non-empty template identities.", nameof(provider));
            if (Providers.TryGetValue(provider.Id, out var existing))
            {
                if (ReferenceEquals(existing.Provider, provider)) return existing;
                throw new InvalidOperationException("An active runner provider already owns identity: " + provider.Id);
            }
            var registration = new AgentRunnerProviderRegistration(provider, templates);
            Providers.Add(provider.Id, registration);
            return registration;
        }

        public static bool TryResolve(string templateId, out AgentRunnerProviderRegistration registration, out AgentRunError error)
        {
            registration = null;
            error = null;
            var candidates = Providers.Values.Where(item => item.IsActive && item.TemplateIds.Contains(templateId)).ToArray();
            if (candidates.Length == 1) { registration = candidates[0]; return true; }
            error = new AgentRunError(candidates.Length == 0 ? "AGENT_PROVIDER_UNAVAILABLE" : "AGENT_PROVIDER_AMBIGUOUS",
                candidates.Length == 0 ? "No enabled runner provider supports the verified template." : "Multiple enabled runner providers support the verified template.");
            return false;
        }

        internal static void Retire(AgentRunnerProviderRegistration registration)
        {
            if (!registration.IsActive) return;
            registration.IsActive = false;
            if (Providers.TryGetValue(registration.Provider.Id, out var current) && ReferenceEquals(current, registration))
                Providers.Remove(registration.Provider.Id);
            // Remove authority before notifying task owners. The registration object remains their stable owner.
            Retired?.Invoke(registration);
        }
    }
}
