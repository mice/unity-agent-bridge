using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UnityMcp.AgentRuntime
{
    // JSON is captured as text: callers cannot mutate an admitted definition through a JSON DOM.
    public sealed class AgentTaskTemplateDefinition
    {
        public AgentTaskTemplateDefinition(string id, string displayName, string description, bool enabled,
            string executorId, string inputSchema, string outputSchema, string execution, string source,
            string version, string dedicatedEntrypoint = null)
        {
            Id = id; DisplayName = displayName; Description = description; Enabled = enabled;
            ExecutorId = executorId; InputSchema = inputSchema; OutputSchema = outputSchema;
            Execution = execution; Source = source; Version = version; DedicatedEntrypoint = dedicatedEntrypoint;
        }
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public string ExecutorId { get; }
        public string InputSchema { get; }
        public string OutputSchema { get; }
        public string Execution { get; }
        public string Source { get; }
        public string Version { get; }
        public string DedicatedEntrypoint { get; }
    }

    public sealed class AgentTaskTemplateAdmission
    {
        public AgentTaskTemplateAdmission(AgentTaskTemplateDefinition definition, string inputsJson, IAgentTaskTemplateExecutor executor)
        { Definition = definition; InputsJson = inputsJson; Executor = executor; }
        public AgentTaskTemplateDefinition Definition { get; }
        public string InputsJson { get; }
        public IAgentTaskTemplateExecutor Executor { get; }
    }

    public interface IAgentTaskTemplateExecutor
    {
        string Id { get; }
        string ConfigurationSchemaJson { get; }
        // Pure discovery/admission validation; must not launch a process or allocate a task.
        string Validate(AgentTaskTemplateDefinition definition);
        // Capture authority and runner settings before allocating/starting a task.
        IAgentRunnerFactory Prepare(AgentTaskTemplateAdmission admission);
    }

    public sealed class AgentTaskTemplateExecutorRegistration : IDisposable
    {
        internal AgentTaskTemplateExecutorRegistration(IAgentTaskTemplateExecutor executor, bool requiresUnityExecution) { Executor = executor; RequiresUnityExecution = requiresUnityExecution; IsActive = true; }
        public IAgentTaskTemplateExecutor Executor { get; }
        public bool RequiresUnityExecution { get; }
        public bool IsActive { get; private set; }
        public void Dispose()
        {
            if (!IsActive) return;
            IsActive = false;
            AgentTaskTemplateExecutors.Remove(this);
        }
    }

    public static class AgentTaskTemplateExecutors
    {
        private static readonly List<AgentTaskTemplateExecutorRegistration> Registrations = new List<AgentTaskTemplateExecutorRegistration>();
        public static event Action<AgentTaskTemplateExecutorRegistration> Retired;
        public static AgentTaskTemplateExecutorRegistration Register(IAgentTaskTemplateExecutor executor)
            => Register(executor, requiresUnityExecution: true);

        public static AgentTaskTemplateExecutorRegistration Register(IAgentTaskTemplateExecutor executor, bool requiresUnityExecution)
        {
            if (executor == null || string.IsNullOrWhiteSpace(executor.Id)) throw new ArgumentException("Executor identity is required.");
            var registration = new AgentTaskTemplateExecutorRegistration(executor, requiresUnityExecution);
            Registrations.Add(registration);
            return registration;
        }
        public static AgentTaskTemplateExecutorRegistration Resolve(string id)
        {
            var matches = Registrations.Where(item => item.IsActive && item.Executor.Id == id).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        internal static void Remove(AgentTaskTemplateExecutorRegistration registration)
        { Registrations.Remove(registration); Retired?.Invoke(registration); }
    }
}
