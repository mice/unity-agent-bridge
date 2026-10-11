using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.AgentTaskCore
{
    public sealed class AgentTaskManager : IAgentTaskManager
    {
        private readonly Dictionary<AgentTaskId, Record> records = new Dictionary<AgentTaskId, Record>();
        private readonly IAgentTaskClock clock;
        private readonly AgentTaskRetentionPolicy retention;
        private readonly Func<AgentTaskId> idFactory;

        public AgentTaskManager(IAgentTaskClock clock = null, AgentTaskRetentionPolicy retention = null,
            Func<AgentTaskId> idFactory = null)
        {
            this.clock = clock ?? new SystemAgentTaskClock();
            this.retention = retention ?? AgentTaskRetentionPolicy.Default;
            this.idFactory = idFactory ?? (() => new AgentTaskId(Guid.NewGuid().ToString("N")));
        }

        public AgentTaskId Register(IAgentTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            AgentTaskId id;
            do { id = idFactory(); } while (records.ContainsKey(id));
            records.Add(id, new Record(id, task, clock.UtcNow, this));
            return id;
        }

        public AgentTaskId RestoreRunning(AgentTaskId id, IAgentTask task, DateTimeOffset createdAt)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (records.ContainsKey(id)) throw new InvalidOperationException("Task ID is already registered: " + id.Value);
            records.Add(id, new Record(id, task, createdAt, this));
            Transition(records[id], AgentTaskState.Running);
            return id;
        }

        public AgentTaskSnapshot Get(AgentTaskId id)
        {
            Record record;
            return records.TryGetValue(id, out record) ? record.Snapshot : null;
        }

        public bool Start(AgentTaskId id)
        {
            var record = Require(id);
            Transition(record, AgentTaskState.Running);
            try { record.Task.Start(record.Context); }
            catch (Exception exception) { FailFromException(record, exception); }
            return true;
        }

        public bool Advance(AgentTaskId id)
        {
            var record = Require(id);
            if (record.State != AgentTaskState.Running) return false;
            try { record.Task.Advance(record.Context); }
            catch (Exception exception) { FailFromException(record, exception); }
            return true;
        }

        public bool Fail(AgentTaskId id, AgentTaskError error)
        {
            var record = Require(id);
            if (record.State != AgentTaskState.Running) return false;
            Fail(record, error);
            return true;
        }

        public IAgentTaskContext GetContext(AgentTaskId id) => Require(id).Context;

        public AgentTaskCancellationDisposition Cancel(AgentTaskId id)
        {
            Record record;
            if (!records.TryGetValue(id, out record)) return AgentTaskCancellationDisposition.NotFound;
            if (record.State == AgentTaskState.Cancelled) return AgentTaskCancellationDisposition.AlreadyCancelled;
            if (record.IsTerminal) return AgentTaskCancellationDisposition.AlreadyTerminal;
            if (!record.Task.CanCancel) return AgentTaskCancellationDisposition.Unsupported;
            record.CancellationRequested = true;
            if (record.State == AgentTaskState.Created)
            {
                CommitCancelled(record, AgentTaskResult.Cancelled("Cancelled before start."));
                return AgentTaskCancellationDisposition.Accepted;
            }
            try { record.Task.RequestCancellation(record.Context); }
            catch (Exception exception) { FailFromException(record, exception); }
            return AgentTaskCancellationDisposition.Accepted;
        }

        public int Cleanup()
        {
            var now = clock.UtcNow;
            var terminal = records.Values.Where(record => record.IsTerminal).ToList();
            var remove = new HashSet<AgentTaskId>(terminal
                .Where(record => record.TerminalAt.HasValue && now - record.TerminalAt.Value >= retention.TerminalLifetime)
                .Select(record => record.Id));
            foreach (var record in terminal.OrderBy(record => record.TerminalAt))
            {
                if (terminal.Count - remove.Count <= retention.MaximumTerminalRecords) break;
                remove.Add(record.Id);
            }
            foreach (var id in remove) records.Remove(id);
            return remove.Count;
        }

        private Record Require(AgentTaskId id)
        {
            Record record;
            if (!records.TryGetValue(id, out record)) throw new KeyNotFoundException("Unknown task: " + id);
            return record;
        }

        private void Complete(Record record, AgentTaskResult result)
        {
            if (record.State != AgentTaskState.Running) throw new InvalidOperationException("Only running tasks can complete.");
            Transition(record, AgentTaskState.Completed);
            record.Result = result ?? AgentTaskResult.Succeeded();
        }

        private void Fail(Record record, AgentTaskError error)
        {
            if (record.State != AgentTaskState.Running) throw new InvalidOperationException("Only running tasks can fail.");
            Transition(record, AgentTaskState.Failed);
            record.Error = error ?? new AgentTaskError("AGENT_TASK_FAILURE", "Task failed.");
        }

        private void CommitCancelled(Record record, AgentTaskResult result)
        {
            if (record.State != AgentTaskState.Created && record.State != AgentTaskState.Running)
                throw new InvalidOperationException("Only created or running tasks can be cancelled.");
            Transition(record, AgentTaskState.Cancelled);
            record.Result = result ?? AgentTaskResult.Cancelled();
        }

        private void Transition(Record record, AgentTaskState next)
        {
            if (!IsValidTransition(record.State, next))
                throw new InvalidOperationException("Invalid task transition: " + record.State + " -> " + next);
            record.State = next;
            if (record.IsTerminal) record.TerminalAt = clock.UtcNow;
        }

        private static bool IsValidTransition(AgentTaskState current, AgentTaskState next) =>
            (current == AgentTaskState.Created && (next == AgentTaskState.Running || next == AgentTaskState.Cancelled)) ||
            (current == AgentTaskState.Running && (next == AgentTaskState.Completed || next == AgentTaskState.Failed || next == AgentTaskState.Cancelled));

        private void FailFromException(Record record, Exception exception)
        {
            if (!record.IsTerminal) Fail(record, new AgentTaskError("AGENT_TASK_EXCEPTION", exception.Message, exception));
        }

        private sealed class Record
        {
            public Record(AgentTaskId id, IAgentTask task, DateTimeOffset createdAt, AgentTaskManager manager)
            {
                Id = id; Task = task; CreatedAt = createdAt; Manager = manager; State = AgentTaskState.Created;
                Context = new Context(this);
            }
            public readonly AgentTaskId Id;
            public readonly IAgentTask Task;
            public readonly DateTimeOffset CreatedAt;
            public readonly AgentTaskManager Manager;
            public readonly Context Context;
            public AgentTaskState State;
            public AgentTaskResult Result;
            public AgentTaskError Error;
            public bool CancellationRequested;
            public DateTimeOffset? TerminalAt;
            public bool IsTerminal => State == AgentTaskState.Completed || State == AgentTaskState.Failed || State == AgentTaskState.Cancelled;
            public AgentTaskSnapshot Snapshot => new AgentTaskSnapshot(Id, State, Result, Error, CancellationRequested, CreatedAt, TerminalAt);
        }

        private sealed class Context : IAgentTaskContext
        {
            private readonly Record record;
            public Context(Record record) { this.record = record; }
            public AgentTaskId Id => record.Id;
            public bool CancellationRequested => record.CancellationRequested;
            public void Complete(AgentTaskResult result) => CompleteCore(result);
            public void Fail(AgentTaskError error) => FailCore(error);
            public void Cancel(AgentTaskResult result = null) => CancelCore(result);
            private void CompleteCore(AgentTaskResult result) => record.Manager.Complete(record, result);
            private void FailCore(AgentTaskError error) => record.Manager.Fail(record, error);
            private void CancelCore(AgentTaskResult result) => record.Manager.CommitCancelled(record, result);
        }
    }
}
