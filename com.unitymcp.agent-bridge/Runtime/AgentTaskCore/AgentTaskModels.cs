using System;

namespace UnityMcp.AgentTaskCore
{
    public enum AgentTaskState
    {
        Created,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum AgentTaskResultKind
    {
        Succeeded,
        DomainFailure,
        TimedOut,
        Cancelled
    }

    public enum AgentTaskCancellationDisposition
    {
        Accepted,
        AlreadyCancelled,
        AlreadyTerminal,
        Unsupported,
        NotFound
    }

    public readonly struct AgentTaskId : IEquatable<AgentTaskId>
    {
        public AgentTaskId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Task ID cannot be empty.", nameof(value));
            Value = value;
        }

        public string Value { get; }

        public bool Equals(AgentTaskId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AgentTaskId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(AgentTaskId left, AgentTaskId right) => left.Equals(right);
        public static bool operator !=(AgentTaskId left, AgentTaskId right) => !left.Equals(right);
    }

    public sealed class AgentTaskError
    {
        public AgentTaskError(string code, string message, Exception exception = null)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "AGENT_TASK_FAILURE" : code;
            Message = message ?? string.Empty;
            ExceptionType = exception?.GetType().FullName;
        }

        public string Code { get; }
        public string Message { get; }
        public string ExceptionType { get; }
    }

    public sealed class AgentTaskResult
    {
        private AgentTaskResult(AgentTaskResultKind kind, string reason, object payload)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
            Payload = payload;
        }

        public AgentTaskResultKind Kind { get; }
        public string Reason { get; }
        public object Payload { get; }

        public static AgentTaskResult Succeeded(object payload = null, string reason = null) =>
            new AgentTaskResult(AgentTaskResultKind.Succeeded, reason, payload);

        public static AgentTaskResult DomainFailure(string reason, object payload = null) =>
            new AgentTaskResult(AgentTaskResultKind.DomainFailure, reason, payload);

        public static AgentTaskResult TimedOut(string reason = null) =>
            new AgentTaskResult(AgentTaskResultKind.TimedOut, reason, null);

        public static AgentTaskResult Cancelled(string reason = null) =>
            new AgentTaskResult(AgentTaskResultKind.Cancelled, reason, null);
    }

    public sealed class AgentTaskSnapshot
    {
        internal AgentTaskSnapshot(AgentTaskId id, AgentTaskState state, AgentTaskResult result, AgentTaskError error,
            bool cancellationRequested, DateTimeOffset createdAt, DateTimeOffset? terminalAt)
        {
            Id = id;
            State = state;
            Result = result;
            Error = error;
            CancellationRequested = cancellationRequested;
            CreatedAt = createdAt;
            TerminalAt = terminalAt;
        }

        public AgentTaskId Id { get; }
        public AgentTaskState State { get; }
        public AgentTaskResult Result { get; }
        public AgentTaskError Error { get; }
        public bool CancellationRequested { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? TerminalAt { get; }
        public bool IsTerminal => State == AgentTaskState.Completed || State == AgentTaskState.Failed || State == AgentTaskState.Cancelled;
    }

    public interface IAgentTask
    {
        bool CanCancel { get; }
        void Start(IAgentTaskContext context);
        void Advance(IAgentTaskContext context);
        void RequestCancellation(IAgentTaskContext context);
    }

    public interface IAgentTaskManager
    {
        AgentTaskId Register(IAgentTask task);
        AgentTaskSnapshot Get(AgentTaskId id);
        bool Start(AgentTaskId id);
        bool Advance(AgentTaskId id);
        AgentTaskCancellationDisposition Cancel(AgentTaskId id);
        int Cleanup();
    }

    public interface IAgentTaskContext
    {
        AgentTaskId Id { get; }
        bool CancellationRequested { get; }
        void Complete(AgentTaskResult result);
        void Fail(AgentTaskError error);
        void Cancel(AgentTaskResult result = null);
    }

    public interface IAgentTaskClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public sealed class SystemAgentTaskClock : IAgentTaskClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public sealed class AgentTaskRetentionPolicy
    {
        public AgentTaskRetentionPolicy(TimeSpan terminalLifetime, int maximumTerminalRecords)
        {
            if (terminalLifetime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(terminalLifetime));
            if (maximumTerminalRecords < 0) throw new ArgumentOutOfRangeException(nameof(maximumTerminalRecords));
            TerminalLifetime = terminalLifetime;
            MaximumTerminalRecords = maximumTerminalRecords;
        }

        public TimeSpan TerminalLifetime { get; }
        public int MaximumTerminalRecords { get; }
        public static AgentTaskRetentionPolicy Default => new AgentTaskRetentionPolicy(TimeSpan.FromDays(7), 1000);
    }
}
