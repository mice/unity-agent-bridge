#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnityMcp.AgentTaskCore
{
    public enum AgentExecutionPhase { Queued, Dispatching, Running, Recovering, Released }

    [Serializable]
    public sealed class AgentScheduledRequest
    {
        public string RequestId { get; set; }
        public string CommandId { get; set; }
        public string AgentTaskId { get; set; }
        public string ProjectId { get; set; }
        public string EditorSessionId { get; set; }
        public string ConnectionId { get; set; }
        public string Tool { get; set; }
        public string Payload { get; set; }
        public string OriginKind { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
        public DateTimeOffset Deadline { get; set; }

        internal AgentScheduledRequest Copy() => (AgentScheduledRequest)MemberwiseClone();
    }

    [Serializable]
    public sealed class AgentScheduledExecution
    {
        public AgentScheduledRequest Request { get; set; }
        public long Sequence { get; set; }
        public AgentExecutionPhase Phase { get; set; }
        public string OwnershipToken { get; set; }
        public string OperationCorrelation { get; set; }
        public string LinkedTaskId { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public bool ExecutionEnded { get; set; }
        public string EndEvidence { get; set; }
        public string Outcome { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public string BlockingReason { get; set; }
        public DateTimeOffset? TerminalAt { get; set; }
        public bool DeliveryAcknowledged { get; set; }
        public string PendingResultJson { get; set; }

        public bool OwnsExecution => Phase == AgentExecutionPhase.Dispatching ||
            Phase == AgentExecutionPhase.Running || Phase == AgentExecutionPhase.Recovering;

        internal AgentScheduledExecution Copy()
        {
            var copy = (AgentScheduledExecution)MemberwiseClone();
            copy.Request = Request.Copy();
            return copy;
        }
    }

    [Serializable]
    public sealed class AgentExecutionJournal
    {
        public int Version { get; set; } = 1;
        public string ProjectId { get; set; }
        public long NextSequence { get; set; } = 1;
        public string RecoveryBarrier { get; set; }
        public string RecoveryBarrierSessionId { get; set; }
        public string RecoveryResolution { get; set; }
        public List<AgentScheduledExecution> Entries { get; set; } = new List<AgentScheduledExecution>();

        internal AgentExecutionJournal Copy() => new AgentExecutionJournal
        {
            Version = Version, ProjectId = ProjectId, NextSequence = NextSequence, RecoveryBarrier = RecoveryBarrier,
            RecoveryBarrierSessionId = RecoveryBarrierSessionId, RecoveryResolution = RecoveryResolution,
            Entries = Entries.Select(entry => entry.Copy()).ToList()
        };
    }

    public interface IAgentExecutionJournalStore
    {
        // Read returns null only when no journal has ever been committed. Corruption MUST throw.
        AgentExecutionJournal Read();
        // A successful return means the entire replacement is durably committed.
        void Write(AgentExecutionJournal journal);
    }

    public sealed class AgentExecutionSchedulerOptions
    {
        public int WaitingCapacity { get; set; } = 32;
        public int MaximumRecords { get; set; } = 2048;
        public int MaximumPayloadBytes { get; set; } = 1024 * 1024;
        public int MaximumRetainedTerminalRecords { get; set; } = 1000;
        public TimeSpan TerminalRetention { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan MaximumRequestLifetime { get; set; } = TimeSpan.FromMinutes(5);

        internal AgentExecutionSchedulerOptions CopyAndValidate()
        {
            var copy = (AgentExecutionSchedulerOptions)MemberwiseClone();
            if (WaitingCapacity < 1 || WaitingCapacity > 256) throw new ArgumentOutOfRangeException(nameof(WaitingCapacity));
            if (MaximumRecords <= WaitingCapacity || MaximumRecords > 4096) throw new ArgumentOutOfRangeException(nameof(MaximumRecords));
            if (MaximumPayloadBytes < 1 || MaximumPayloadBytes > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumPayloadBytes));
            if (MaximumRetainedTerminalRecords < 0 || MaximumRetainedTerminalRecords > MaximumRecords) throw new ArgumentOutOfRangeException(nameof(MaximumRetainedTerminalRecords));
            if (TerminalRetention < TimeSpan.Zero || TerminalRetention > TimeSpan.FromDays(7)) throw new ArgumentOutOfRangeException(nameof(TerminalRetention));
            if (MaximumRequestLifetime <= TimeSpan.Zero || MaximumRequestLifetime > TimeSpan.FromMilliseconds(int.MaxValue)) throw new ArgumentOutOfRangeException(nameof(MaximumRequestLifetime));
            return copy;
        }
    }

    public sealed class AgentExecutionAdmission
    {
        public bool Accepted { get; internal set; }
        public bool Existing { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Message { get; internal set; }
        public AgentScheduledExecution Execution { get; internal set; }
    }

    /// <summary>
    /// Protocol-independent, durable single-slot arbitration. It never invokes an executor.
    /// Hosts claim before Start and release only with independent actual-completion evidence.
    /// Logical outcomes, execution occupancy and result delivery have separate lifetimes.
    /// </summary>
    public sealed class AgentExecutionScheduler
    {
        private readonly object sync = new object();
        private readonly string projectId;
        private readonly string editorSessionId;
        private readonly IAgentExecutionJournalStore store;
        private readonly IAgentTaskClock clock;
        private readonly AgentExecutionSchedulerOptions options;
        private AgentExecutionJournal journal;
        private string recoveryError;

        public AgentExecutionScheduler(string projectId, string editorSessionId,
            IAgentExecutionJournalStore store, IAgentTaskClock clock = null, AgentExecutionSchedulerOptions options = null)
        {
            this.projectId = RequireIdentity(projectId, nameof(projectId));
            this.editorSessionId = RequireIdentity(editorSessionId, nameof(editorSessionId));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.clock = clock ?? new SystemAgentTaskClock();
            this.options = (options ?? new AgentExecutionSchedulerOptions()).CopyAndValidate();
            Reload();
        }

        public string RecoveryError { get { lock (sync) return recoveryError ?? journal.RecoveryBarrier; } }
        public int WaitingCapacity => options.WaitingCapacity;
        public string RecoveryBarrierSessionId { get { lock (sync) return journal.RecoveryBarrierSessionId; } }
        public void ReconfigureWaitingCapacity(int capacity)
        {
            if (capacity < 1 || capacity > 256) throw new ArgumentOutOfRangeException(nameof(capacity));
            lock (sync) options.WaitingCapacity = capacity;
        }
        public void ReconfigureRequestLifetime(TimeSpan maximum)
        {
            if (maximum <= TimeSpan.Zero || maximum > TimeSpan.FromMilliseconds(int.MaxValue)) throw new ArgumentOutOfRangeException(nameof(maximum));
            lock (sync) options.MaximumRequestLifetime = maximum;
        }
        public int WaitingCount { get { lock (sync) return journal.Entries.Count(entry => entry.Phase == AgentExecutionPhase.Queued); } }
        public AgentScheduledExecution Active { get { lock (sync) return journal.Entries.FirstOrDefault(entry => entry.OwnsExecution)?.Copy(); } }

        public AgentScheduledExecution Find(string requestId)
        {
            lock (sync) return journal.Entries.FirstOrDefault(entry => entry.Request.RequestId == requestId)?.Copy();
        }

        public AgentScheduledExecution FindTask(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return null;
            lock (sync) return journal.Entries.FirstOrDefault(entry => entry.Request.AgentTaskId == taskId || entry.LinkedTaskId == taskId)?.Copy();
        }

        public bool AssociateTask(string requestId, string token, string taskId)
        {
            lock (sync)
            {
                if (!ValidIdentity(taskId)) return false;
                var entry = Owned(requestId, token);
                if (entry == null || entry.LinkedTaskId != null && entry.LinkedTaskId != taskId) return false;
                if (entry.Request.AgentTaskId != null && entry.Request.AgentTaskId != taskId) return false;
                if (journal.Entries.Any(other => other.Request.RequestId != requestId && (other.Request.AgentTaskId == taskId || other.LinkedTaskId == taskId))) return false;
                if (entry.LinkedTaskId == taskId) return true;
                return Change(requestId, changed => changed.LinkedTaskId = taskId);
            }
        }

        public IReadOnlyList<AgentScheduledExecution> ReadEntries()
        {
            lock (sync) return journal.Entries.OrderBy(entry => entry.Sequence).Select(entry => entry.Copy()).ToArray();
        }

        public IReadOnlyList<AgentScheduledExecution> ReadPendingEntries()
        {
            lock (sync) return journal.Entries.Where(entry => entry.Phase != AgentExecutionPhase.Released || !entry.DeliveryAcknowledged)
                .OrderBy(entry => entry.Sequence).Select(entry => entry.Copy()).ToArray();
        }

        public bool Reload()
        {
            lock (sync)
            {
                // Never replace failed/corrupt recovery with an apparently empty healthy slot.
                try
                {
                    var loaded = store.Read();
                    if (loaded == null && journal != null) throw new InvalidOperationException("A previously committed scheduler journal is missing.");
                    loaded = loaded ?? new AgentExecutionJournal { ProjectId = projectId };
                    Validate(loaded);
                    var restored = loaded.Copy();
                    foreach (var entry in restored.Entries)
                    {
                        if (entry.OwnsExecution)
                        {
                            entry.Phase = AgentExecutionPhase.Recovering;
                            entry.BlockingReason = "Reconcile the original operation; dispatch is not replayable.";
                        }
                        else if (entry.Phase == AgentExecutionPhase.Queued && entry.Request.EditorSessionId != editorSessionId)
                            FinishUnstarted(entry, "Interrupted", "EDITOR_SESSION_ENDED", "Queued work is not transferred to a new Editor session.");
                    }
                    store.Write(restored.Copy());
                    journal = restored;
                    recoveryError = null;
                    return true;
                }
                catch (Exception exception)
                {
                    recoveryError = "SCHEDULER_RECOVERY_UNAVAILABLE: " + exception.Message;
                    if (journal == null) journal = new AgentExecutionJournal { ProjectId = projectId };
                    return false;
                }
            }
        }

        public AgentExecutionAdmission Admit(AgentScheduledRequest request)
        {
            lock (sync)
            {
                if (RecoveryError != null) return Rejected("SCHEDULER_RECOVERY_UNAVAILABLE", RecoveryError);
                if (!IsValid(request) || request.EditorSessionId != editorSessionId || request.SubmittedAt > clock.UtcNow)
                    return Rejected("SCHEDULER_REQUEST_INVALID", "Request identity, target, payload or finite original deadline is invalid.");
                var existing = journal.Entries.FirstOrDefault(entry => entry.Request.RequestId == request.RequestId);
                if (existing != null)
                    return SameRequest(existing.Request, request)
                        ? new AgentExecutionAdmission { Accepted = true, Existing = true, Execution = existing.Copy() }
                        : Rejected("SCHEDULER_OWNERSHIP_CONFLICT", "The dispatch identity already belongs to another immutable request.");
                if (request.Deadline <= clock.UtcNow) return Rejected("SCHEDULER_QUEUE_TIMEOUT", "The original request deadline elapsed before admission.");
                var replacement = journal.Copy();
                Prune(replacement);
                if (replacement.Entries.Count(entry => entry.Phase == AgentExecutionPhase.Queued) >= options.WaitingCapacity)
                    return Rejected("SCHEDULER_QUEUE_FULL", "The Editor waiting queue is full; observe existing work before submitting a new request.");
                if (replacement.Entries.Count >= options.MaximumRecords || replacement.NextSequence == long.MaxValue)
                    return Rejected("SCHEDULER_STORAGE_FULL", "Scheduler tracking is full; pending delivery or recovery must be resolved before new admission.");
                var admitted = new AgentScheduledExecution
                {
                    Request = request.Copy(), Sequence = replacement.NextSequence++, Phase = AgentExecutionPhase.Queued,
                    BlockingReason = "Waiting for the Editor execution slot."
                };
                replacement.Entries.Add(admitted);
                if (!Commit(replacement)) return Rejected("SCHEDULER_PERSIST_FAILED", recoveryError);
                return new AgentExecutionAdmission { Accepted = true, Execution = admitted.Copy() };
            }
        }

        public int ExpireWaiting()
        {
            lock (sync)
            {
                if (recoveryError != null) return 0;
                var expired = journal.Entries.Where(entry => entry.Phase == AgentExecutionPhase.Queued && entry.Request.Deadline <= clock.UtcNow).ToArray();
                if (expired.Length == 0) return 0;
                var replacement = journal.Copy();
                foreach (var entry in replacement.Entries.Where(entry => expired.Any(item => item.Sequence == entry.Sequence)))
                    FinishUnstarted(entry, "TimedOut", "SCHEDULER_QUEUE_TIMEOUT", "The original deadline elapsed while waiting; execution was never started.");
                return Commit(replacement) ? expired.Length : 0;
            }
        }

        public bool TryClaimNext(string blockingReason, out AgentScheduledExecution execution)
        {
            lock (sync)
            {
                execution = null;
                if (RecoveryError != null) return false;
                ExpireWaiting();
                if (recoveryError != null || journal.Entries.Any(entry => entry.OwnsExecution)) return false;
                var head = journal.Entries.Where(entry => entry.Phase == AgentExecutionPhase.Queued).OrderBy(entry => entry.Sequence).FirstOrDefault();
                if (head == null) return false;
                if (!string.IsNullOrEmpty(blockingReason))
                {
                    if (head.BlockingReason != blockingReason)
                        Change(head.Request.RequestId, entry => entry.BlockingReason = blockingReason);
                    return false;
                }
                var token = Guid.NewGuid().ToString("N");
                if (!Change(head.Request.RequestId, entry =>
                {
                    entry.Phase = AgentExecutionPhase.Dispatching;
                    entry.OwnershipToken = token;
                    entry.StartedAt = clock.UtcNow;
                    entry.BlockingReason = null;
                })) return false;
                execution = Find(head.Request.RequestId);
                return true;
            }
        }

        public bool MarkRunning(string requestId, string token, string operationCorrelation)
        {
            lock (sync)
            {
                var entry = Owned(requestId, token);
                if (entry == null || entry.Phase == AgentExecutionPhase.Recovering) return false;
                return Change(requestId, changed => { changed.Phase = AgentExecutionPhase.Running; changed.OperationCorrelation = operationCorrelation; });
            }
        }

        public bool RequireRecovery(string reason)
        {
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4096) throw new ArgumentException("A bounded recovery reason is required.", nameof(reason));
                if (journal.RecoveryBarrier != null) return true;
                var replacement = journal.Copy();
                replacement.RecoveryBarrier = reason;
                replacement.RecoveryBarrierSessionId = editorSessionId;
                return Commit(replacement);
            }
        }

        public bool ConfirmUntrackedSessionExit(string originalSession, string evidence)
        {
            lock (sync)
            {
                if (originalSession == editorSessionId || originalSession != journal.RecoveryBarrierSessionId ||
                    string.IsNullOrWhiteSpace(evidence) || evidence.Length > 4096) return false;
                var replacement = journal.Copy();
                replacement.RecoveryResolution = replacement.RecoveryBarrier + "; " + evidence;
                replacement.RecoveryBarrier = null;
                replacement.RecoveryBarrierSessionId = null;
                return Commit(replacement);
            }
        }

        public bool ResumeObservation(string requestId, string token, string operationCorrelation)
        {
            lock (sync)
            {
                var entry = Owned(requestId, token);
                if (entry == null || entry.Phase != AgentExecutionPhase.Recovering || string.IsNullOrEmpty(operationCorrelation) ||
                    entry.OperationCorrelation != operationCorrelation) return false;
                return Change(requestId, changed => { changed.Phase = AgentExecutionPhase.Running; changed.BlockingReason = null; });
            }
        }

        public AgentTaskCancellationDisposition CancelWaiting(string requestId)
        {
            lock (sync)
            {
                var entry = journal.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
                if (entry == null) return AgentTaskCancellationDisposition.NotFound;
                if (entry.Outcome == "Cancelled") return AgentTaskCancellationDisposition.AlreadyCancelled;
                if (entry.Phase == AgentExecutionPhase.Queued)
                {
                    if (!Change(requestId, changed => FinishUnstarted(changed, "Cancelled", "SCHEDULER_QUEUE_CANCELLED", "Cancelled before execution started.")))
                        throw new InvalidOperationException(recoveryError);
                    return AgentTaskCancellationDisposition.Accepted;
                }
                if (entry.Outcome != null) return AgentTaskCancellationDisposition.AlreadyTerminal;
                return AgentTaskCancellationDisposition.Unsupported;
            }
        }

        public bool RejectWaiting(string requestId, string errorCode, string message)
        {
            lock (sync)
            {
                var entry = journal.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
                return entry?.Phase == AgentExecutionPhase.Queued && Change(requestId, changed => FinishUnstarted(changed, "Failed", errorCode, message));
            }
        }

        public bool RecordOutcome(string requestId, string outcome, string errorCode = null, string message = null)
        {
            lock (sync)
            {
                var entry = journal.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
                if (entry == null || entry.Outcome != null || entry.Phase == AgentExecutionPhase.Queued || !ValidOutcome(outcome)) return false;
                return Change(requestId, changed =>
                {
                    changed.Outcome = outcome; changed.ErrorCode = errorCode; changed.Message = message; changed.TerminalAt = clock.UtcNow;
                    if (changed.OwnsExecution) changed.BlockingReason = "Logical outcome recorded; actual Unity completion must still be verified.";
                });
            }
        }

        public bool ConfirmExecutionEnded(string requestId, string token, string evidence)
        {
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 4096 || Owned(requestId, token) == null) return false;
                return Change(requestId, entry =>
                {
                    entry.ExecutionEnded = true; entry.EndEvidence = evidence; entry.Phase = AgentExecutionPhase.Released; entry.BlockingReason = null;
                });
            }
        }

        public bool AcknowledgeDelivery(string requestId)
        {
            lock (sync)
            {
                var entry = journal.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
                if (entry?.DeliveryAcknowledged == true) return true;
                return entry?.Outcome != null && Change(requestId, changed => { changed.DeliveryAcknowledged = true; changed.PendingResultJson = null; });
            }
        }

        public bool SavePendingDelivery(string requestId, string resultJson)
        {
            lock (sync)
            {
                if (string.IsNullOrEmpty(resultJson) || Encoding.UTF8.GetByteCount(resultJson) > options.MaximumPayloadBytes)
                    throw new ArgumentException("Pending delivery must contain bounded original result bytes.", nameof(resultJson));
                var entry = journal.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
                if (entry == null || entry.DeliveryAcknowledged) return false;
                if (entry.PendingResultJson != null) return entry.PendingResultJson == resultJson;
                return Change(requestId, changed => changed.PendingResultJson = resultJson);
            }
        }

        public int Cleanup()
        {
            lock (sync)
            {
                if (recoveryError != null) return 0;
                var replacement = journal.Copy();
                Prune(replacement);
                var count = journal.Entries.Count - replacement.Entries.Count;
                return count > 0 && Commit(replacement) ? count : 0;
            }
        }

        private AgentScheduledExecution Owned(string requestId, string token) => recoveryError != null || string.IsNullOrEmpty(token) ? null :
            journal.Entries.FirstOrDefault(entry => entry.Request.RequestId == requestId && entry.OwnsExecution && entry.OwnershipToken == token);

        private bool Change(string requestId, Action<AgentScheduledExecution> change)
        {
            if (recoveryError != null) return false;
            var replacement = journal.Copy();
            var entry = replacement.Entries.FirstOrDefault(item => item.Request.RequestId == requestId);
            if (entry == null) return false;
            change(entry);
            return Commit(replacement);
        }

        private bool Commit(AgentExecutionJournal replacement)
        {
            try
            {
                Validate(replacement);
                store.Write(replacement.Copy());
                journal = replacement;
                return true;
            }
            catch (Exception exception)
            {
                recoveryError = "SCHEDULER_PERSIST_FAILED: " + exception.Message;
                return false;
            }
        }

        private void FinishUnstarted(AgentScheduledExecution entry, string outcome, string code, string message)
        {
            entry.Phase = AgentExecutionPhase.Released; entry.ExecutionEnded = true;
            entry.EndEvidence = "Never crossed the persisted dispatch boundary.";
            entry.Outcome = outcome; entry.ErrorCode = code; entry.Message = message;
            entry.TerminalAt = clock.UtcNow; entry.BlockingReason = null;
        }

        private void Prune(AgentExecutionJournal replacement)
        {
            var eligible = replacement.Entries.Where(entry => entry.Phase == AgentExecutionPhase.Released && entry.ExecutionEnded &&
                entry.Outcome != null && entry.DeliveryAcknowledged && entry.TerminalAt.HasValue).OrderBy(entry => entry.TerminalAt).ToArray();
            var overflow = Math.Max(0, eligible.Length - options.MaximumRetainedTerminalRecords);
            var removed = new HashSet<long>(eligible.Where((entry, index) => index < overflow || clock.UtcNow - entry.TerminalAt.Value >= options.TerminalRetention).Select(entry => entry.Sequence));
            replacement.Entries.RemoveAll(entry => removed.Contains(entry.Sequence));
        }

        private bool IsValid(AgentScheduledRequest request, bool persisted = false) => request != null &&
            ValidIdentity(request.RequestId) && ValidIdentity(request.CommandId) &&
            request.ProjectId == projectId && ValidIdentity(request.EditorSessionId) &&
            !string.IsNullOrWhiteSpace(request.Tool) && request.Tool.Length <= 256 &&
            (request.AgentTaskId == null || ValidIdentity(request.AgentTaskId)) &&
            (request.ConnectionId == null || ValidIdentity(request.ConnectionId)) &&
            request.SubmittedAt != default(DateTimeOffset) && request.Deadline > request.SubmittedAt &&
            request.Deadline - request.SubmittedAt <= (persisted ? TimeSpan.FromMilliseconds(int.MaxValue) : options.MaximumRequestLifetime) &&
            Encoding.UTF8.GetByteCount(request.Payload ?? string.Empty) <= options.MaximumPayloadBytes;

        private void Validate(AgentExecutionJournal value)
        {
            if (value == null || value.Version != 1 || value.ProjectId != projectId || value.Entries == null || value.NextSequence < 1 || value.Entries.Count > options.MaximumRecords)
                throw new InvalidOperationException("Invalid, unsupported or cross-project scheduler journal.");
            if (value.RecoveryBarrier != null && (value.RecoveryBarrier.Length > 4096 || !ValidIdentity(value.RecoveryBarrierSessionId)))
                throw new InvalidOperationException("Untracked recovery barrier is invalid.");
            if (value.Entries.Any(entry => entry == null || !IsValid(entry.Request, persisted: true) || entry.Sequence < 1 || entry.Sequence >= value.NextSequence ||
                !Enum.IsDefined(typeof(AgentExecutionPhase), entry.Phase) ||
                entry.OwnsExecution && (string.IsNullOrEmpty(entry.OwnershipToken) || !entry.StartedAt.HasValue || entry.ExecutionEnded) ||
                entry.Phase == AgentExecutionPhase.Queued && (entry.StartedAt.HasValue || entry.Outcome != null || entry.ExecutionEnded) ||
                entry.Phase == AgentExecutionPhase.Released && (!entry.ExecutionEnded || string.IsNullOrWhiteSpace(entry.EndEvidence)) ||
                entry.Outcome != null && (!ValidOutcome(entry.Outcome) || !entry.TerminalAt.HasValue) ||
                entry.DeliveryAcknowledged && entry.Outcome == null ||
                entry.PendingResultJson != null && Encoding.UTF8.GetByteCount(entry.PendingResultJson) > options.MaximumPayloadBytes))
                throw new InvalidOperationException("Invalid scheduler entry or execution ownership.");
            if (value.Entries.Any(entry => entry.LinkedTaskId != null && !ValidIdentity(entry.LinkedTaskId)))
                throw new InvalidOperationException("Invalid linked task identity.");
            if (value.Entries.Select(entry => entry.Sequence).Distinct().Count() != value.Entries.Count ||
                value.Entries.Select(entry => entry.Request.RequestId).Distinct(StringComparer.Ordinal).Count() != value.Entries.Count ||
                value.Entries.Select(entry => entry.Request.CommandId).Distinct(StringComparer.Ordinal).Count() != value.Entries.Count ||
                value.Entries.Count(entry => entry.OwnsExecution) > 1)
                throw new InvalidOperationException("Duplicate scheduler identities or overlapping execution owners.");
            var taskIds = value.Entries.SelectMany(entry => new[] { entry.Request.AgentTaskId, entry.LinkedTaskId }
                .Where(id => id != null).Distinct(StringComparer.Ordinal)).ToArray();
            if (taskIds.Distinct(StringComparer.Ordinal).Count() != taskIds.Length)
                throw new InvalidOperationException("One task identity cannot belong to multiple scheduler records.");
        }

        private static bool SameRequest(AgentScheduledRequest left, AgentScheduledRequest right) => left.RequestId == right.RequestId &&
            left.CommandId == right.CommandId && left.AgentTaskId == right.AgentTaskId && left.ProjectId == right.ProjectId &&
            left.EditorSessionId == right.EditorSessionId && left.ConnectionId == right.ConnectionId && left.Tool == right.Tool &&
            left.Payload == right.Payload && left.SubmittedAt == right.SubmittedAt && left.Deadline == right.Deadline && left.OriginKind == right.OriginKind;
        private static bool ValidOutcome(string value) => value == "Completed" || value == "Failed" || value == "Cancelled" || value == "TimedOut" || value == "Interrupted" || value == "Unknown";
        private static bool ValidIdentity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
        private static string RequireIdentity(string value, string name) => ValidIdentity(value) ? value : throw new ArgumentException("A bounded identity is required.", name);
        private static AgentExecutionAdmission Rejected(string code, string message) => new AgentExecutionAdmission { ErrorCode = code, Message = message };
    }
}
