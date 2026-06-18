using System;

namespace UnityMcp.AgentBridge
{
    internal sealed class DeadlineAgentCancellation : IAgentCancellation
    {
        private readonly IAgentCancellation _inner;
        private readonly Func<DateTime> _utcNowProvider;
        private readonly DateTime _deadlineUtc;

        public DeadlineAgentCancellation(DateTime startedAtUtc, int effectiveTimeoutMs, IAgentCancellation inner, Func<DateTime> utcNowProvider)
        {
            _inner = inner;
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
            _deadlineUtc = startedAtUtc.AddMilliseconds(effectiveTimeoutMs);
        }

        public bool IsCancellationRequested => IsTimedOut || (_inner != null && _inner.IsCancellationRequested);

        public bool IsTimedOut => _utcNowProvider().ToUniversalTime() >= _deadlineUtc;

        public void ThrowIfCancellationRequested()
        {
            if (_inner != null)
            {
                _inner.ThrowIfCancellationRequested();
            }

            if (IsTimedOut)
            {
                throw new OperationCanceledException("The tool execution timed out.");
            }
        }
    }

    public sealed class NoOpAgentCancellation : IAgentCancellation
    {
        public static readonly NoOpAgentCancellation Instance = new NoOpAgentCancellation();

        private NoOpAgentCancellation()
        {
        }

        public bool IsCancellationRequested => false;

        public void ThrowIfCancellationRequested()
        {
        }
    }
}
