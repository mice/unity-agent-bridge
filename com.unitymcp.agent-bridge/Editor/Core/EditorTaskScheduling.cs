using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    /// <summary>One Editor-owned actor for transport commands and native task starts.</summary>
    public static class EditorTaskScheduling
    {
        private sealed class SavedCommand
        {
            public AgentCommand Command;
            public string QueueRoot;
            public string TaskType;
        }

        private sealed class Binding
        {
            public SavedCommand Saved;
            public Action<int> Start;
            public AgentTaskManager Manager;
            public AgentTaskId TaskId;
            public Action PersistTerminal;
            public Func<string> EndEvidence;
            public ToolResult InitialResult;
            public bool Returned;
            public bool WasDeferred;
            public bool RejectedBeforeExecution;
        }

        private static readonly Dictionary<string, Binding> Bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        private static AgentExecutionScheduler scheduler;
        private static AgentBridgeSettings settings;
        private static UnityToolFacade facade;
        private static string projectRoot;
        private static string projectId;
        private static string currentOwner;
        private static AgentCommand sourceCommand;
        private static string sourceQueueRoot;
        private static bool ticking;
        private static double nextCleanup;
        private static double nextTick;
        private static string configurationError;
        private static IAgentTaskClock clock = new SystemAgentTaskClock();
        private static Func<string, string> resolveProjectId = ProjectPathIdentity.GetProjectId;

        public static AgentCommand SourceCommand => sourceCommand;

        // Compiler callbacks can arrive after logical timeout removed the
        // service-local operation. Retain the admitted queue for epoch evidence.
        internal static string ActiveCompileQueueRoot
        {
            get
            {
                var active = scheduler?.Active;
                return active != null && active.Request.EditorSessionId == AgentTaskDurableResults.EditorSession &&
                    (active.OperationCorrelation ?? string.Empty).StartsWith("compile:", StringComparison.Ordinal)
                    ? ReadSaved(active).QueueRoot : null;
            }
        }

        public static void Configure(UnityToolFacade toolFacade, AgentBridgeSettings bridgeSettings)
        {
            facade = toolFacade ?? throw new ArgumentNullException(nameof(toolFacade));
            settings = bridgeSettings ?? throw new ArgumentNullException(nameof(bridgeSettings));
            try
            {
                Ensure();
                scheduler.ReconfigureWaitingCapacity(settings.schedulerWaitingCapacity);
                scheduler.ReconfigureRequestLifetime(TimeSpan.FromMilliseconds(settings.maxToolDurationMs));
                configurationError = null;
                RestoreOrdinaryBindings();
                RestoreUnavailableTemplates();
            }
            catch (Exception exception) { configurationError = "SCHEDULER_CONFIGURATION_INVALID: " + exception.Message; throw; }
        }

        private static void Ensure()
        {
            if (scheduler != null) return;
            projectRoot = Directory.GetParent(Application.dataPath).FullName;
            projectId = resolveProjectId(projectRoot);
            settings = settings != null ? settings : AgentBridgeSettingsLoader.Load().Settings;
            var options = new AgentExecutionSchedulerOptions
            {
                WaitingCapacity = settings.schedulerWaitingCapacity,
                MaximumRequestLifetime = TimeSpan.FromMilliseconds(settings.maxToolDurationMs)
            };
            var journalPath = Path.Combine(AgentTaskDurableResults.Root, "scheduler", "execution.json");
            var fresh = !File.Exists(journalPath) && !File.Exists(journalPath + ".initialized");
            scheduler = new AgentExecutionScheduler(projectId, AgentTaskDurableResults.EditorSession,
                new EditorExecutionJournalStore(journalPath), clock, options);
            if (fresh)
            {
                foreach (var category in new[] { "compile-operations", "agent-task-compile", "agent-task-editmode", "agent-task-playmode" })
                {
                    var legacy = Path.Combine(AgentTaskDurableResults.Root, category);
                    var pattern = category == "compile-operations" ? "*.compile.data" : "*.json";
                    if (Directory.Exists(legacy) && Directory.EnumerateFiles(legacy, pattern).Any())
                    {
                        scheduler.RequireRecovery("Legacy operation evidence predates scheduler admission. Inspect the original operation or restart this Editor under authorization; work will not be replayed.");
                        break;
                    }
                }
            }
            EditorApplication.update += Tick;
        }

        public static T WithSource<T>(AgentCommand command, Func<T> action, string queueRoot = null)
        {
            var previous = sourceCommand;
            var previousRoot = sourceQueueRoot;
            sourceCommand = command;
            sourceQueueRoot = queueRoot ?? previousRoot;
            try { return action(); }
            finally { sourceCommand = previous; sourceQueueRoot = previousRoot; }
        }

        public static string GetQueueRoot(string fallback) => currentOwner != null && Bindings.TryGetValue(currentOwner, out var owner)
            ? owner.Saved.QueueRoot : sourceQueueRoot ?? fallback;

        public static bool IsLifecycleObservation(AgentCommand command)
        {
            if (command?.tool != "unity.agent_task_control") return false;
            try
            {
                var request = new AgentTaskControlCodec().DeserializeRequest(command.rawArgsJson ?? "{}");
                return request.Operation == AgentTaskControlProtocol.Query || request.Operation == AgentTaskControlProtocol.Result || request.Operation == AgentTaskControlProtocol.Cancel;
            }
            catch { return false; }
        }

        public static ToolResult Execute(AgentCommand command, AgentBridgeSettings bridgeSettings, Func<ToolResult> execute, bool concurrentObservation = false, bool useCurrentFacade = false)
        {
            if (command != null && projectRoot != null && (command.projectId == null || command.projectId == projectId) &&
                (command.editorSessionId == null || command.editorSessionId == AgentTaskDurableResults.EditorSession))
            {
                var prior = ReadTerminal(new SavedCommand { Command = command, QueueRoot = bridgeSettings.tempRoot });
                if (prior != null)
                    return prior.commandId == command.commandId && prior.tool == command.tool ? prior : Failure(command, "SCHEDULER_OWNERSHIP_CONFLICT", "Original terminal output belongs to another immutable command.");
            }
            if (command == null || concurrentObservation) return WithSource(command, execute, bridgeSettings?.tempRoot);
            Ensure();
            if (configurationError != null) return Failure(command, "SCHEDULER_CONFIGURATION_INVALID", configurationError);
            if (currentOwner != null && Bindings.TryGetValue(currentOwner, out var owner) && owner.Saved.Command.commandId == command.commandId)
                return WithSource(command, execute);
            var existing = scheduler.Find(command.commandId);
            if (existing == null)
            {
                var saved = new SavedCommand { Command = Copy(command), QueueRoot = bridgeSettings.tempRoot };
                var request = MakeRequest(command.commandId, null, saved, null);
                var admission = scheduler.Admit(request);
                if (!admission.Accepted) return Failure(command, admission.ErrorCode, admission.Message);
                BindOrdinary(admission.Execution.Request.RequestId, saved, useCurrentFacade ? null : (Func<ToolResult>)(() => { command.timeoutMs = saved.Command.timeoutMs; return execute(); }));
            }
            else
            {
                var duplicate = MakeRequest(command.commandId, null, new SavedCommand { Command = Copy(command), QueueRoot = bridgeSettings.tempRoot }, null);
                if (duplicate.OriginKind == "legacy-first-receipt") { duplicate.SubmittedAt = existing.Request.SubmittedAt; duplicate.Deadline = existing.Request.Deadline; }
                var admission = scheduler.Admit(duplicate);
                if (!admission.Accepted) return Failure(command, admission.ErrorCode, admission.Message);
                if (existing.Outcome != null) return ReadTerminal(ReadSaved(existing)) ?? Failure(command, existing.ErrorCode ?? "SCHEDULER_RESULT_PENDING", existing.Message ?? "Original terminal delivery is pending.", existing.Outcome);
                if (!Bindings.ContainsKey(existing.Request.RequestId) && existing.Phase == AgentExecutionPhase.Queued)
                    BindOrdinary(existing.Request.RequestId, ReadSaved(existing), useCurrentFacade ? null : (Func<ToolResult>)(() => { command.timeoutMs = Remaining(existing); return execute(); }));
            }
            return Pending(command, Observation(command.commandId));
        }

        private static void BindOrdinary(string id, SavedCommand saved, Func<ToolResult> initialExecute = null)
        {
            var binding = new Binding { Saved = saved };
            binding.Start = remaining =>
            {
                saved.Command.timeoutMs = remaining;
                binding.InitialResult = WithSource(saved.Command, () => initialExecute != null ? initialExecute() : facade.ExecuteImmediate(saved.Command, NoOpAgentCancellation.Instance), saved.QueueRoot);
                if (binding.InitialResult != null) { binding.InitialResult.commandId = saved.Command.commandId; binding.InitialResult.tool = saved.Command.tool; }
                binding.Returned = true;
                binding.WasDeferred = IsDeferred(binding.InitialResult);
                binding.RejectedBeforeExecution = binding.InitialResult != null && (binding.InitialResult.status == ToolResultStatus.Blocked || binding.InitialResult.status == ToolResultStatus.InvalidArgs || binding.InitialResult.status == ToolResultStatus.Unsupported);
                if (!binding.WasDeferred)
                {
                    var durable = TryDeliverOriginal(binding) || scheduler.SavePendingDelivery(id, JsonUtil.SerializeResult(binding.InitialResult));
                    var active = scheduler.Active;
                    if (durable && active != null && binding.Manager == null)
                        scheduler.MarkRunning(id, active.OwnershipToken, "returned-sync");
                }
            };
            Bindings[id] = binding;
        }

        public static AgentCommand TaskCommand(string commandId, string tool, int timeoutMs, string payload, string submittedAt = null)
        {
            var source = sourceCommand;
            return new AgentCommand
            {
                schemaVersion = JsonUtil.CurrentSchemaVersion, commandId = commandId, tool = tool, timeoutMs = timeoutMs,
                rawArgsJson = payload ?? "{}", createdAt = !string.IsNullOrEmpty(submittedAt) ? submittedAt : source?.createdAt,
                connectionId = source?.connectionId, projectId = source?.projectId, editorSessionId = source?.editorSessionId
            };
        }

        public static void ScheduleTask(AgentTaskManager manager, AgentTaskId id, AgentCommand command,
            string taskType, Action<int> start, Func<string> endEvidence, Action persistTerminal = null)
        {
            Ensure();
            if (configurationError != null)
            {
                manager.RejectBeforeStart(id, new AgentTaskError("SCHEDULER_CONFIGURATION_INVALID", configurationError));
                persistTerminal?.Invoke();
                return;
            }
            if (currentOwner != null && Bindings.TryGetValue(currentOwner, out var owner))
            {
                if (owner.Manager != null)
                {
                    manager.RejectBeforeStart(id, new AgentTaskError("SCHEDULER_NESTED_START", "An execution owner cannot start a second independent Unity task."));
                    return;
                }
                var owning = scheduler.Find(currentOwner);
                if (!scheduler.AssociateTask(currentOwner, owning.OwnershipToken, id.Value))
                {
                    manager.RejectBeforeStart(id, new AgentTaskError("SCHEDULER_PERSIST_FAILED", "Could not bind the original task before Start."));
                    return;
                }
                owner.Manager = manager; owner.TaskId = id; owner.EndEvidence = endEvidence; owner.PersistTerminal = persistTerminal;
                start(Remaining(scheduler.Find(currentOwner)));
                return;
            }
            var saved = new SavedCommand { Command = Copy(command), QueueRoot = GetQueueRoot(settings.tempRoot), TaskType = taskType };
            var request = MakeRequest(id.Value, id.Value, saved, sourceCommand?.commandId);
            var existing = scheduler.FindTask(id.Value) ?? scheduler.Find(command.commandId);
            if (existing == null && manager.Get(id)?.State != AgentTaskState.Created)
            {
                scheduler.RequireRecovery("An untracked legacy operation requires reconciliation: " + command.commandId);
                return;
            }
            if (existing == null)
            {
                var admission = scheduler.Admit(request);
                if (!admission.Accepted)
                {
                    manager.RejectBeforeStart(id, new AgentTaskError(admission.ErrorCode, admission.Message));
                    persistTerminal?.Invoke();
                    return;
                }
            }
            var key = existing?.Request.RequestId ?? id.Value;
            if (existing != null && existing.Request.AgentTaskId == null)
            {
                saved = ReadSaved(existing);
                if (existing.OwnsExecution) scheduler.AssociateTask(key, existing.OwnershipToken, id.Value);
            }
            Bindings[key] = new Binding { Saved = saved, Manager = manager, TaskId = id, Start = start, EndEvidence = endEvidence, PersistTerminal = persistTerminal };
        }

        public static AgentScheduledExecution FindTask(string id)
        {
            Ensure();
            return scheduler.Find(id) ?? scheduler.FindTask(id);
        }

        public static void ScheduleTemplateTask(AgentTaskManager manager, AgentTaskId id, AgentTaskControlRequest request,
            Action<DateTimeOffset> start, Func<bool> ended, Action persistTerminal)
        {
            var command = TaskCommand("template-dispatch-" + id.Value, "unity.agent_task_template.run", request.TimeoutMs, request.Payload, request.SubmittedAtUtc);
            ScheduleTask(manager, id, command, "template_run", remaining => start(scheduler.Active.Request.Deadline),
                () => ended() ? "The admitted Unity executor positively reports actual completion." : null, persistTerminal);
        }

        public static AgentTaskCancellationDisposition CancelTask(AgentTaskManager manager, AgentTaskId id)
        {
            Ensure();
            var entry = scheduler.FindTask(id.Value);
            if (entry?.Phase == AgentExecutionPhase.Queued)
            {
                var disposition = scheduler.CancelWaiting(entry.Request.RequestId);
                if (disposition == AgentTaskCancellationDisposition.Accepted) manager.Cancel(id);
                return disposition;
            }
            return manager.Cancel(id);
        }

        public static AgentTaskControlResponse CancelCommand(string commandId)
        {
            Ensure();
            var entry = scheduler.Find(commandId);
            if (entry?.Request.AgentTaskId != null) return new AgentTaskControlResponse { Success = false, ErrorCode = "TASK_ID_REQUIRED", ErrorMessage = "Cancel a native task using its original task identity." };
            var disposition = scheduler.CancelWaiting(commandId);
            return new AgentTaskControlResponse
            {
                Success = disposition != AgentTaskCancellationDisposition.NotFound, Cancellation = disposition,
                ErrorCode = disposition == AgentTaskCancellationDisposition.NotFound ? "NOT_FOUND" : null,
                ErrorMessage = disposition == AgentTaskCancellationDisposition.Unsupported ? "The ordinary operation has started; cancellation cannot prove termination." : null,
                SchedulingJson = Observation(commandId)
            };
        }

        public static AgentTaskControlResponse Observe(AgentTaskControlResponse response)
        {
            if (response != null && !string.IsNullOrEmpty(response.AgentTaskId)) response.SchedulingJson = Observation(response.AgentTaskId);
            return response;
        }

        public static AgentTaskControlRequest ReconcileCreation(AgentTaskControlRequest request)
        {
            if (request?.Operation != AgentTaskControlProtocol.Create || sourceCommand == null) return request;
            Ensure();
            var prior = scheduler.ReadEntries().FirstOrDefault(entry => entry.Request.AgentTaskId != null && entry.Request.CommandId == sourceCommand.commandId);
            if (prior == null) return request;
            var saved = ReadSaved(prior);
            var normalized = request.TaskType == AgentTaskVerifiedAnalysisExecutionService.TaskType || request.TaskType == AgentTaskVerifiedAnalysisExecutionService.AssetTaskType
                ? AgentTaskVerifiedAnalysisExecutionService.NormalizeCreate(request) : request;
            if (saved.TaskType != normalized.TaskType || saved.Command.rawArgsJson != (normalized.Payload ?? "{}"))
                throw new InvalidOperationException("SCHEDULER_OWNERSHIP_CONFLICT: Dispatch identity already belongs to different task inputs.");
            return new AgentTaskControlRequest
            {
                Version = request.Version, Operation = AgentTaskControlProtocol.Query, ProjectIdentity = request.ProjectIdentity,
                AgentTaskId = prior.Request.AgentTaskId, TaskType = request.TaskType
            };
        }

        public static ToolResult RecoverCommand(AgentCommand command)
        {
            Ensure();
            if (scheduler.Find(command.commandId) != null || IsLifecycleObservation(command))
                return facade.Execute(command, NoOpAgentCancellation.Instance);
            if (command.tool == "unity.agent_task_control" || command.tool == "unity.agent_task_template.run")
            {
                var known = scheduler.ReadEntries().Any(entry => entry.Request.CommandId == command.commandId);
                if (known) return facade.Execute(command, NoOpAgentCancellation.Instance);
            }
            if (command.tool == "unity.agent_bridge_self_test")
                return Failure(command, "SCHEDULER_COORDINATOR_INTERRUPTED", "The self-test coordinator was interrupted. Its already-admitted child operations keep their original identity; the suite was not restarted.", "Interrupted");
            scheduler.RequireRecovery("Untracked legacy processing command; verify actual completion before dispatch or restart the original Editor under authorization: " + command.commandId);
            return Failure(command, "AGENT_TASK_OUTCOME_UNKNOWN", "An old processing command has no scheduler start evidence. Its operation was not replayed.", "Unknown");
        }

        public static string Observation(string requestId = null)
        {
            Ensure();
            var entry = requestId == null ? null : scheduler.Find(requestId) ?? scheduler.FindTask(requestId);
            var active = scheduler.Active;
            var queue = scheduler.ReadPendingEntries().Where(item => item.Phase == AgentExecutionPhase.Queued).OrderBy(item => item.Sequence).ToArray();
            var observedConflict = active == null ? BusyReason() : null;
            return JsonConvert.SerializeObject(new
            {
                schemaVersion = 1, projectId, editorSessionId = AgentTaskDurableResults.EditorSession,
                stage = scheduler.RecoveryError != null ? "blocked" : entry?.Phase.ToString().ToLowerInvariant() ?? (requestId != null ? "unavailable" : active != null ? active.Phase.ToString().ToLowerInvariant() : queue.Length > 0 ? "queued" : observedConflict != null ? "blocked" : "idle"),
                queuePosition = entry?.Phase == AgentExecutionPhase.Queued ? Array.FindIndex(queue, item => item.Sequence == entry.Sequence) + 1 : 0,
                admissionSequence = entry?.Sequence, waitingCount = queue.Length, waitingCapacity = scheduler.WaitingCapacity,
                blockingReason = configurationError ?? scheduler.RecoveryError ?? entry?.BlockingReason ?? active?.BlockingReason ?? queue.FirstOrDefault()?.BlockingReason ?? observedConflict,
                observedEditorConflict = observedConflict,
                deadline = entry?.Request.Deadline, connectionId = entry?.Request.ConnectionId,
                deadlineOrigin = entry?.Request.OriginKind, submittedAt = entry?.Request.SubmittedAt,
                commandId = entry?.Request.CommandId, agentTaskId = entry?.Request.AgentTaskId ?? entry?.LinkedTaskId,
                activeCommandId = active?.Request.CommandId, operationCorrelation = active?.OperationCorrelation,
                executionOccupied = active != null || scheduler.RecoveryError != null, logicalOutcome = entry?.Outcome,
                deliveryPending = entry?.Outcome != null && !entry.DeliveryAcknowledged
            });
        }

        public static void SetOperationCorrelation(string commandId, string correlation)
        {
            Ensure();
            var active = scheduler.Active;
            if (active != null && Bindings.TryGetValue(active.Request.RequestId, out var binding) && binding.Saved.Command.commandId == commandId)
                scheduler.MarkRunning(active.Request.RequestId, active.OwnershipToken, correlation);
        }

        public static void NotifyExecutionEnded(string commandId, string evidence)
        {
            Ensure();
            var active = scheduler.Active;
            if (active == null) return;
            var saved = Bindings.TryGetValue(active.Request.RequestId, out var binding) ? binding.Saved : ReadSaved(active);
            if (saved.Command.commandId == commandId) scheduler.ConfirmExecutionEnded(active.Request.RequestId, active.OwnershipToken, evidence);
        }

        public static void NotifyTestStarted(string commandId, string runGuid)
        {
            SetOperationCorrelation(commandId, "test-started:" + (runGuid ?? commandId));
        }

        public static bool IsExecuting(string commandId)
        {
            return currentOwner != null && Bindings.TryGetValue(currentOwner, out var binding) && binding.Saved.Command.commandId == commandId;
        }

        public static bool StartTask(AgentTaskManager manager, AgentTaskId id)
        {
            var active = scheduler?.Active;
            if (active == null || currentOwner != active.Request.RequestId) throw new InvalidOperationException("Task Start requires current Editor execution ownership.");
            if (active.Request.Deadline <= clock.UtcNow)
            {
                manager.RejectBeforeStart(id, new AgentTaskError("SCHEDULER_QUEUE_TIMEOUT", "The original deadline elapsed before driver startup."));
                scheduler.RecordOutcome(active.Request.RequestId, "TimedOut", "SCHEDULER_QUEUE_TIMEOUT", "Deadline elapsed before driver startup.");
                scheduler.ConfirmExecutionEnded(active.Request.RequestId, active.OwnershipToken, "Driver Start was never invoked after deadline verification.");
                return false;
            }
            manager.Start(id);
            return true;
        }

        private static void Tick()
        {
            if (ticking || scheduler == null) return;
            if (EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + Math.Max(1, settings?.pollIntervalMs ?? 200) / 1000.0;
            ticking = true;
            try
            {
                scheduler.ExpireWaiting();
                foreach (var entry in scheduler.ReadPendingEntries())
                {
                    if (!Bindings.TryGetValue(entry.Request.RequestId, out var binding)) continue;
                    try { ObserveBinding(entry, binding); }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                    { Debug.LogWarning("SCHEDULER_DELIVERY_PENDING: " + entry.Request.RequestId + ": " + exception.Message); }
                }
                ReconcileActiveEvidence();
                var barrierSession = scheduler.RecoveryBarrierSessionId;
                if (barrierSession != null && OriginalEditorExited(barrierSession))
                    scheduler.ConfirmUntrackedSessionExit(barrierSession, "Verified original Editor PID/start-time exit; old work is not replayed.");
                var active = scheduler.Active;
                if (active != null && OriginalEditorExited(active.Request.EditorSessionId))
                {
                    scheduler.RecordOutcome(active.Request.RequestId, "Interrupted", "EDITOR_SESSION_ENDED", "The original Editor process exited; execution is not transferred.");
                    scheduler.ConfirmExecutionEnded(active.Request.RequestId, active.OwnershipToken, "Verified original Editor PID/start-time exit.");
                }
                if (configurationError != null || facade == null || settings == null || !settings.enabled || !AgentBridgeLocalPreferences.BridgeEnabled) return;
                var head = scheduler.ReadPendingEntries().FirstOrDefault(entry => entry.Phase == AgentExecutionPhase.Queued);
                var reason = BusyReason();
                if (head != null && !Bindings.ContainsKey(head.Request.RequestId)) reason = "Execution binding unavailable; original operation must be reconciled without replay.";
                if (scheduler.TryClaimNext(reason, out var claim))
                {
                    var binding = Bindings[claim.Request.RequestId];
                    scheduler.MarkRunning(claim.Request.RequestId, claim.OwnershipToken, binding.Saved.Command.commandId);
                    if (scheduler.RecoveryError != null) return;
                    currentOwner = claim.Request.RequestId;
                    try { WithSource(binding.Saved.Command, () => { binding.Start(Remaining(claim)); return true; }, binding.Saved.QueueRoot); }
                    catch (Exception exception)
                    {
                        scheduler.RecordOutcome(claim.Request.RequestId, "Unknown", "SCHEDULER_START_UNCERTAIN", exception.Message);
                        Debug.LogWarning("SCHEDULER_START_UNCERTAIN: " + exception.Message);
                    }
                    finally { currentOwner = null; }
                }
                if (EditorApplication.timeSinceStartup >= nextCleanup)
                {
                    nextCleanup = EditorApplication.timeSinceStartup + 30;
                    scheduler.Cleanup();
                    foreach (var key in Bindings.Keys.Where(key => scheduler.Find(key) == null).ToArray()) Bindings.Remove(key);
                }
            }
            catch (Exception exception) { Debug.LogWarning("SCHEDULER_TICK_FAILED: " + exception.Message); }
            finally { ticking = false; }
        }

        private static void ObserveBinding(AgentScheduledExecution entry, Binding binding)
        {
            if (entry.Phase == AgentExecutionPhase.Queued) return;
            if (!entry.StartedAt.HasValue && entry.Outcome != null)
            {
                if (entry.DeliveryAcknowledged) return;
                if (binding.Manager != null)
                {
                    var snapshot = binding.Manager.Get(binding.TaskId);
                    if (snapshot?.State == AgentTaskState.Created)
                    {
                        if (entry.Outcome == "Cancelled") binding.Manager.Cancel(binding.TaskId);
                        else binding.Manager.RejectBeforeStart(binding.TaskId, new AgentTaskError(entry.ErrorCode, entry.Message));
                    }
                    PersistTask(binding);
                }
                else
                {
                    binding.InitialResult = Failure(binding.Saved.Command, entry.ErrorCode, entry.Message, entry.Outcome);
                    if (!TryDeliverOriginal(binding)) return;
                }
                if (scheduler.AcknowledgeDelivery(entry.Request.RequestId)) Bindings.Remove(entry.Request.RequestId);
                return;
            }
            if (entry.OwnsExecution)
            {
                var evidence = binding.EndEvidence?.Invoke();
                if (binding.RejectedBeforeExecution) evidence = "Facade preflight rejected the request before executor Start.";
                if (evidence == null && binding.Returned && !binding.WasDeferred && binding.Manager == null &&
                    binding.Saved.Command.tool != "unity.compile" && binding.Saved.Command.tool != "unity.run_playmode_tests" && binding.Saved.Command.tool != "unity.run_editmode_tests" && BusyReason() == null)
                    evidence = "The synchronous executor invocation returned.";
                if (evidence != null) scheduler.ConfirmExecutionEnded(entry.Request.RequestId, entry.OwnershipToken, evidence);
            }
            // The original deadline also bounds the compile operation, but its typed
            // timeout owns diagnostic/report adaptation. Do not commit a generic
            // scheduler error first merely because this update callback ran earlier.
            if (entry.Outcome == null && entry.Request.Deadline <= clock.UtcNow && binding.Saved.Command.tool == "unity.compile")
            {
                var compileResult = UnityCompileOperationManager.CompleteOriginalDeadline(binding.Saved.Command.commandId);
                if (compileResult != null && binding.Manager?.Get(binding.TaskId)?.State == AgentTaskState.Running)
                    binding.Manager.Advance(binding.TaskId);
            }
            var snapshotNow = binding.Manager?.Get(binding.TaskId);
            if (binding.Manager == null && binding.Returned && !binding.WasDeferred) TryDeliverOriginal(binding);
            var terminal = binding.InitialResult;
            if (terminal == null || IsDeferred(terminal)) terminal = ReadTerminal(binding.Saved);
            if (entry.Outcome == null)
            {
                if (snapshotNow?.IsTerminal == true)
                    scheduler.RecordOutcome(entry.Request.RequestId, SnapshotOutcome(snapshotNow), snapshotNow.Error?.Code, snapshotNow.Error?.Message ?? snapshotNow.Result?.Reason);
                else if (terminal != null && !IsDeferred(terminal)) scheduler.RecordOutcome(entry.Request.RequestId, ResultOutcome(terminal), terminal.errors?.FirstOrDefault()?.code, terminal.summary);
                else if (entry.Request.Deadline <= clock.UtcNow)
                {
                    if (snapshotNow?.State == AgentTaskState.Running) binding.Manager.Fail(binding.TaskId, new AgentTaskError("AGENT_TASK_TIMEOUT", "The original request deadline elapsed."));
                    scheduler.RecordOutcome(entry.Request.RequestId, "TimedOut", "AGENT_TASK_TIMEOUT", "The original request deadline elapsed; actual Unity completion is still tracked.");
                    if (binding.Manager == null) Queue(binding.Saved).Complete(binding.Saved.Command.commandId, Failure(binding.Saved.Command, "AGENT_TASK_TIMEOUT", "The original request deadline elapsed.", "TimedOut"));
                }
            }
            var current = scheduler.Find(entry.Request.RequestId);
            if (current?.Outcome != null && !current.DeliveryAcknowledged)
            {
                if (binding.Manager != null)
                {
                    PersistTask(binding);
                    if (entry.Request.AgentTaskId == null && ReadTerminal(binding.Saved) == null) return;
                }
                else if (ReadTerminal(binding.Saved) == null) return;
                scheduler.AcknowledgeDelivery(entry.Request.RequestId);
            }
            current = scheduler.Find(entry.Request.RequestId);
            if (current?.Phase == AgentExecutionPhase.Released && current.DeliveryAcknowledged) Bindings.Remove(entry.Request.RequestId);
        }

        private static void PersistTask(Binding binding)
        {
            if (binding.PersistTerminal != null) { binding.PersistTerminal(); return; }
            var snapshot = binding.Manager.Get(binding.TaskId);
            if (snapshot?.IsTerminal == true) AgentTaskDurableResults.Save(new AgentTaskControlResponse
            {
                Success = true, AgentTaskId = binding.TaskId.Value, Snapshot = snapshot,
                ErrorCode = snapshot.Error?.Code, ErrorMessage = snapshot.Error?.Message,
                ResultPayloadJson = snapshot.Result?.Payload == null ? null : JsonUtil.SerializeObject(snapshot.Result.Payload)
            });
        }

        private static AgentScheduledRequest MakeRequest(string id, string taskId, SavedCommand saved, string dispatchCommandId)
        {
            // Unity can mutate the loaded settings object without replacing the
            // facade (including during reload initialization). Refresh limits for
            // this admission without changing any persisted request's deadline.
            scheduler.ReconfigureWaitingCapacity(settings.schedulerWaitingCapacity);
            scheduler.ReconfigureRequestLifetime(TimeSpan.FromMilliseconds(settings.maxToolDurationMs));
            var command = saved.Command;
            var now = clock.UtcNow;
            var parsed = DateTimeOffset.TryParse(command.createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var origin);
            if (!parsed) origin = now;
            var budget = Math.Min(command.timeoutMs, settings.maxToolDurationMs);
            return new AgentScheduledRequest
            {
                RequestId = id, CommandId = dispatchCommandId ?? command.commandId, AgentTaskId = taskId,
                ProjectId = command.projectId ?? projectId, EditorSessionId = command.editorSessionId ?? AgentTaskDurableResults.EditorSession,
                ConnectionId = command.connectionId, Tool = command.tool, Payload = JsonConvert.SerializeObject(saved),
                SubmittedAt = origin, Deadline = origin.AddMilliseconds(budget), OriginKind = parsed ? "original-submission" : "legacy-first-receipt"
            };
        }

        private static void RestoreOrdinaryBindings()
        {
            foreach (var entry in scheduler.ReadEntries())
            {
                if (entry.Request.AgentTaskId != null || Bindings.ContainsKey(entry.Request.RequestId) || entry.Phase == AgentExecutionPhase.Released && entry.DeliveryAcknowledged) continue;
                var saved = ReadSaved(entry);
                BindOrdinary(entry.Request.RequestId, saved);
                if (entry.PendingResultJson != null)
                {
                    var binding = Bindings[entry.Request.RequestId];
                    binding.InitialResult = ParseTerminal(entry.PendingResultJson);
                    binding.Returned = true;
                    binding.RejectedBeforeExecution = binding.InitialResult.status == ToolResultStatus.Blocked || binding.InitialResult.status == ToolResultStatus.InvalidArgs || binding.InitialResult.status == ToolResultStatus.Unsupported;
                }
                else if (entry.OperationCorrelation == "returned-sync")
                {
                    var binding = Bindings[entry.Request.RequestId];
                    binding.InitialResult = ReadTerminal(saved);
                    binding.Returned = binding.InitialResult != null;
                }
                // Only Queued entries may Start. Active recovery never invokes this delegate.
            }
        }

        private static SavedCommand ReadSaved(AgentScheduledExecution entry) => JsonConvert.DeserializeObject<SavedCommand>(entry.Request.Payload) ?? throw new InvalidDataException("Scheduler command correlation is unavailable.");
        private static void RestoreUnavailableTemplates()
        {
            foreach (var entry in scheduler.ReadEntries())
            {
                if (entry.Request.AgentTaskId == null || Bindings.ContainsKey(entry.Request.RequestId) || entry.DeliveryAcknowledged) continue;
                var saved = ReadSaved(entry);
                var retained = AgentTaskDurableResults.Read(entry.Request.AgentTaskId);
                if (retained?.Snapshot?.IsTerminal == true && retained.ErrorCode != "AGENT_TASK_RECORD_CORRUPT" && retained.ErrorCode != "AGENT_TASK_RECORD_INCOMPATIBLE")
                {
                    if (entry.Phase == AgentExecutionPhase.Queued)
                        scheduler.RejectWaiting(entry.Request.RequestId, "AGENT_TASK_RECOVERED_TERMINAL", "The original durable task is already terminal; its driver will not be started.");
                    if (entry.Outcome == null) scheduler.RecordOutcome(entry.Request.RequestId, SnapshotOutcome(retained.Snapshot), retained.ErrorCode, retained.ErrorMessage);
                    scheduler.AcknowledgeDelivery(entry.Request.RequestId);
                    continue;
                }
                // Opaque prepared executor authority cannot be recreated from current project config.
                // Preserve the original identity and report the unsupported recovery, never Prepare/Start again.
                var manager = new AgentTaskManager();
                var id = new AgentTaskId(entry.Request.AgentTaskId);
                if (!entry.StartedAt.HasValue)
                {
                    manager.RestoreCreated(id, new UnavailableExecutor(), entry.Request.SubmittedAt);
                    if (entry.Phase == AgentExecutionPhase.Queued) scheduler.RejectWaiting(entry.Request.RequestId,
                        saved.TaskType == "template_run" ? "TEMPLATE_RECOVERY_UNAVAILABLE" : "AGENT_TASK_RECOVERY_UNAVAILABLE",
                        "Original prepared driver authority is unavailable after Domain Reload; work was not started or replayed.");
                }
                else
                {
                    manager.RestoreRunning(id, new UnavailableExecutor(), entry.Request.SubmittedAt);
                    manager.Fail(id, new AgentTaskError("AGENT_TASK_RECOVERY_STALE", "Original executor cannot be reattached; actual execution ownership is retained until verified stopped."));
                }
                Bindings[entry.Request.RequestId] = new Binding { Saved = saved, Manager = manager, TaskId = id };
            }
        }
        private sealed class UnavailableExecutor : IAgentTask
        {
            public bool CanCancel => false;
            public void Start(IAgentTaskContext context) => throw new InvalidOperationException("An unavailable executor cannot be replayed.");
            public void Advance(IAgentTaskContext context) { }
            public void RequestCancellation(IAgentTaskContext context) { }
        }
        private static AgentCommand Copy(AgentCommand command) => JsonConvert.DeserializeObject<AgentCommand>(JsonConvert.SerializeObject(command));
        private static AgentCommandQueue Queue(SavedCommand saved) => new AgentCommandQueue(projectRoot, saved.QueueRoot);
        private static int Remaining(AgentScheduledExecution entry) => Math.Max(1, (int)Math.Min(int.MaxValue, (entry.Request.Deadline - clock.UtcNow).TotalMilliseconds));
        private static bool IsDeferred(ToolResult result) => result != null && (result.status == ToolResultStatus.Pending || result.status == ToolResultStatus.Running || result.status == ToolResultStatus.Resuming);
        private static string SnapshotOutcome(AgentTaskSnapshot value) => value.State == AgentTaskState.Cancelled ? "Cancelled" : value.Result?.Kind == AgentTaskResultKind.TimedOut || value.Error?.Code == "AGENT_TASK_TIMEOUT" ? "TimedOut" : value.State == AgentTaskState.Failed ? "Failed" : "Completed";
        private static string ResultOutcome(ToolResult value) => value.status == ToolResultStatus.Timeout ? "TimedOut" : value.status == ToolResultStatus.Cancelled ? "Cancelled" : value.success ? "Completed" : "Failed";
        private static ToolResult ReadTerminal(SavedCommand saved)
        {
            var path = Path.Combine(projectRoot, saved.QueueRoot, "outbox", saved.Command.commandId + ".result.json");
            if (!File.Exists(path)) return null;
            return ParseTerminal(File.ReadAllText(path));
        }
        private static ToolResult ParseTerminal(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                var document = Newtonsoft.Json.Linq.JObject.Load(reader);
                var result = document.ToObject<ToolResult>();
                if (result != null && document["metrics"] != null) result.metricsObjectJson = document["metrics"].ToString(Formatting.None);
                return result;
            }
        }
        private static bool TryDeliverOriginal(Binding binding)
        {
            try { Queue(binding.Saved).Complete(binding.Saved.Command.commandId, binding.InitialResult); return true; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { Debug.LogWarning("SCHEDULER_DELIVERY_PENDING: " + binding.Saved.Command.commandId + ": " + exception.Message); return false; }
        }
        private static ToolResult Pending(AgentCommand command, string observation) => new ToolResult { commandId = command.commandId, tool = command.tool, status = ToolResultStatus.Pending, summary = "Waiting for scheduled Editor execution.", metricsObjectJson = "{\"scheduling\":" + observation + "}" };
        private static ToolResult Failure(AgentCommand command, string code, string message, string outcome = null)
        {
            var result = ToolResult.InvalidArgs(code, message);
            result.commandId = command.commandId; result.tool = command.tool;
            result.status = outcome == "Cancelled" ? ToolResultStatus.Cancelled : outcome == "TimedOut" || code == "SCHEDULER_QUEUE_TIMEOUT" ? ToolResultStatus.Timeout : outcome == "Interrupted" || outcome == "Unknown" ? ToolResultStatus.Exception : ToolResultStatus.Blocked;
            return result;
        }

        private static string BusyReason()
        {
            if (EditorApplication.isCompiling) return "Editor compilation is active.";
            if (EditorApplication.isUpdating) return "Editor importing is active.";
            if (EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode) return "Editor is transitioning Play Mode.";
            if (!TryReadTestActivity(out var testActive)) return "Test Runner activity is unavailable or unreadable; inspect Editor before dispatch.";
            if (testActive) return "Unity Test Runner is active.";
            return null;
        }

        private static bool TryReadTestActivity(out bool active)
        {
            active = false;
            try
            {
                // In Test Framework 1.1.33 IsRunActive reads legacy RunData only.
                // TestRunnerApi.Execute schedules persisted TestJobData independently.
                var method = typeof(TestRunnerApi).GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null || !(method.Invoke(null, Array.Empty<object>()) is bool legacyActive)) return false;
                var holderType = typeof(TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder");
                var instance = holderType?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
                var runs = holderType?.GetField("TestRuns")?.GetValue(instance) as System.Collections.IEnumerable;
                if (runs == null) return false;
                active = legacyActive;
                foreach (var run in runs)
                {
                    if (run == null || !(run.GetType().GetField("isRunning")?.GetValue(run) is bool jobActive)) return false;
                    active |= jobActive;
                }
                return true;
            }
            catch { return false; }
        }

        private static void ReconcileActiveEvidence()
        {
            var active = scheduler.Active;
            if (active == null || active.Request.EditorSessionId != AgentTaskDurableResults.EditorSession) return;
            var correlation = active.OperationCorrelation ?? string.Empty;
            if (correlation.StartsWith("compile:", StringComparison.Ordinal) && int.TryParse(correlation.Substring(8), out var epoch) && epoch > 0)
            {
                var saved = ReadSaved(active);
                var lifecycle = new CompileLifecycleStore(projectRoot, saved.QueueRoot).Read();
                if (lifecycle != null && lifecycle.lastFinishedEpoch >= epoch && !EditorApplication.isCompiling)
                    scheduler.ConfirmExecutionEnded(active.Request.RequestId, active.OwnershipToken, "Persisted compilationFinished evidence for original epoch " + epoch);
            }
            else if (correlation.StartsWith("test-started:", StringComparison.Ordinal))
            {
                if (TryReadTestActivity(out var running) && !running)
                    scheduler.ConfirmExecutionEnded(active.Request.RequestId, active.OwnershipToken, "Original RunStarted persisted; both API job holder and legacy Test Runner state positively report no active runs.");
            }
        }

        private static bool OriginalEditorExited(string session)
        {
            if (session == AgentTaskDurableResults.EditorSession) return false;
            var parts = session?.Split(':');
            if (parts?.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var ticks)) return false;
            try { using (var process = System.Diagnostics.Process.GetProcessById(pid)) return process.HasExited || process.StartTime.ToUniversalTime().Ticks != ticks; }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }

#if !UNITY_EDITOR
        // The .NET contract suite links this exact actor against a fake Editor boundary.
        internal static void ResetForContractTests(IAgentTaskClock testClock)
        {
            EditorApplication.update -= Tick;
            scheduler = null; settings = null; facade = null; currentOwner = null; sourceCommand = null; sourceQueueRoot = null;
            projectRoot = null; projectId = null; ticking = false; nextCleanup = 0; nextTick = 0; configurationError = null;
            Bindings.Clear();
            clock = testClock ?? new SystemAgentTaskClock();
            resolveProjectId = _ => "contract-project";
        }
#endif
    }
}
