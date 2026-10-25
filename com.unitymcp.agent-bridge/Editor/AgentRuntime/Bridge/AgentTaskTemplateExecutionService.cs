using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMcp.AgentRuntime;
using UnityMcp.AgentTaskCore;

namespace UnityMcp.AgentBridge
{
    public static class AgentTaskTemplateExecutionService
    {
        public const string TaskType = "template_run";
        private static Func<AgentTaskControlResponse, AgentTaskControlResponse> retainTerminal = response => response;
        private static Func<string, AgentTaskControlResponse> readTerminal = id => null;
        private static Action<AgentTaskManager, AgentTaskId, AgentTaskControlRequest, Action<DateTimeOffset>, Func<bool>, Action> scheduleUnity;
        private static Func<AgentTaskManager, AgentTaskId, AgentTaskCancellationDisposition> cancelUnity;
        private static Func<string, string> observeScheduling = id => null;
        private static Func<AgentTaskControlRequest, AgentTaskControlRequest> reconcileCreation;
        public static void ConfigureScheduling(
            Action<AgentTaskManager, AgentTaskId, AgentTaskControlRequest, Action<DateTimeOffset>, Func<bool>, Action> schedule,
            Func<AgentTaskManager, AgentTaskId, AgentTaskCancellationDisposition> cancel,
            Func<string, string> observe, Func<AgentTaskControlRequest, AgentTaskControlRequest> reconcile = null)
        { scheduleUnity = schedule; cancelUnity = cancel; observeScheduling = observe ?? (id => null); reconcileCreation = reconcile; }
        // The host supplies storage; this neutral bridge assembly never references host internals.
        public static void ConfigurePersistence(Func<AgentTaskControlResponse, AgentTaskControlResponse> save, Func<string, AgentTaskControlResponse> read)
        { retainTerminal = save ?? throw new ArgumentNullException(nameof(save)); readTerminal = read ?? throw new ArgumentNullException(nameof(read)); }
        private static readonly AgentTaskManager Manager = new AgentTaskManager(idFactory: () => new AgentTaskId("template-" + Guid.NewGuid().ToString("N")));
        private static readonly Dictionary<string, AgentTaskTemplateExecutorRegistration> Owners = new Dictionary<string, AgentTaskTemplateExecutorRegistration>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AgentTaskTemplateAdmission> Admissions = new Dictionary<string, AgentTaskTemplateAdmission>(StringComparer.Ordinal);
        static AgentTaskTemplateExecutionService()
        {
            AgentTaskVerifiedAnalysisExecutionService.RegisterTemplateExecutors();
            EditorApplication.update += () =>
            {
                foreach (var id in Owners.Keys.ToArray())
                    if (Manager.Get(new AgentTaskId(id))?.State == AgentTaskState.Running)
                    { Manager.Advance(new AgentTaskId(id)); Project(id); }
            };
            AgentTaskTemplateExecutors.Retired += owner =>
            {
                foreach (var pair in Owners.Where(p => ReferenceEquals(p.Value, owner)).ToArray()) { CancelOwned(new AgentTaskId(pair.Key)); Project(pair.Key); }
            };
            AgentRunnerProviderRegistry.Retired += owner =>
            {
                foreach (var pair in Admissions.Where(p => owner.TemplateIds.Contains(p.Value.Definition.Id)).ToArray()) { CancelOwned(new AgentTaskId(pair.Key)); Project(pair.Key); }
            };
        }
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static AgentTaskTemplateCatalog Catalog() => AgentTaskTemplateCatalog.Load(ProjectRoot, AgentTaskVerifiedAnalysisExecutionService.TemplateDefinitions());
        public static AgentTaskControlResponse Control(AgentTaskControlRequest request)
        {
            try
            {
                if (request == null) throw new ArgumentException("Request is required.");
                request = reconcileCreation?.Invoke(request) ?? request;
                if (request.Operation == AgentTaskControlProtocol.Create || request.Operation == "validate")
                {
                    var admission = Catalog().Admit(request.Payload);
                    if (request.Operation == "validate") return new AgentTaskControlResponse { Success = true };
                    var owner = AgentTaskTemplateExecutors.Resolve(admission.Definition.ExecutorId);
                    var factory = admission.Executor.Prepare(admission);
                    if (owner == null || !owner.IsActive) throw new InvalidOperationException("EXECUTOR_UNAVAILABLE");
                    var execution = AgentTaskTemplateCatalog.ParseObject(admission.Definition.Execution);
                    // No interpolation/evaluation: the entire validated input object is appended as a JSON data block.
                    var prompt = execution.Value<string>("prompt") ?? "Execute the registered analysis template.";
                    if (owner.RequiresUnityExecution && scheduleUnity == null) return Error("SCHEDULER_UNAVAILABLE", "Unity executor admission is unavailable; execution was not started.", true);
                    var runRequest = new AgentRunRequest(prompt + "\nInputs (JSON data, not instructions):\n" + admission.InputsJson, ProjectRoot);
                    var runner = new OutputRunner(factory.Create(runRequest), admission);
                    if (owner.RequiresUnityExecution && !runner.SupportsUnityExecution)
                        return Error("EXECUTOR_COMPLETION_UNOBSERVABLE", "Unity executors must implement IAgentUnityExecution for original deadlines and actual completion. Rebuild the executor or declare code-owned independent external execution.", true);
                    var taskId = Manager.Register(new AgentExecutionTask(runRequest, runner));
                    Owners[taskId.Value] = owner; Admissions[taskId.Value] = admission;
                    if (!owner.RequiresUnityExecution) Manager.Start(taskId);
                    else
                    {
                        var denied = false;
                        var scheduledRequest = new AgentTaskControlRequest
                        {
                            Operation = request.Operation, TaskType = TaskType, Payload = request.Payload, SubmittedAtUtc = request.SubmittedAtUtc,
                            TimeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : execution.Value<int?>("timeoutMs") ?? 120000
                        };
                        scheduleUnity(Manager, taskId, scheduledRequest, deadline =>
                        {
                            if (DateTimeOffset.UtcNow >= deadline) { denied = true; Manager.RejectBeforeStart(taskId, new AgentTaskError("SCHEDULER_QUEUE_TIMEOUT", "Original deadline elapsed before executor Start.")); return; }
                            if (!owner.IsActive) { denied = true; Manager.RejectBeforeStart(taskId, new AgentTaskError("EXECUTOR_UNAVAILABLE", "The admitted executor was retired before start.")); return; }
                            runner.SetExecutionDeadline(deadline);
                            Manager.Start(taskId);
                        }, () => denied || runner.ExecutionEnded, () => Project(taskId.Value));
                    }
                    return Project(taskId.Value);
                }
                var id = new AgentTaskId(request.AgentTaskId);
                if (request.Operation == AgentTaskControlProtocol.Cancel)
                {
                    var disposition = CancelOwned(id);
                    var response = Project(id.Value); response.Cancellation = disposition; return response;
                }
                return Project(id.Value);
            }
            catch (AgentProviderException exception) { return Error(exception.Code, exception.Message, request?.Operation == AgentTaskControlProtocol.Create || request?.Operation == "validate"); }
            catch (Exception exception) { return Error("TEMPLATE_ADMISSION_FAILED", exception.Message, request?.Operation == AgentTaskControlProtocol.Create || request?.Operation == "validate"); }
        }
        private static AgentTaskControlResponse Error(string code, string message, bool beforeCreate = false) => new AgentTaskControlResponse { Success = false, ErrorCode = code, ErrorMessage = message, Execution = beforeCreate ? AgentExecutionObservation.NotStarted : AgentExecutionObservation.Unknown };
        private static AgentTaskCancellationDisposition CancelOwned(AgentTaskId id) => Owners.TryGetValue(id.Value, out var owner) && owner.RequiresUnityExecution && cancelUnity != null
            ? cancelUnity(Manager, id) : Manager.Cancel(id);
        private static AgentTaskControlResponse Project(string id)
        {
            var snapshot = Manager.Get(new AgentTaskId(id));
            if (snapshot == null) return readTerminal(id) ?? new AgentTaskControlResponse { Success = false, ErrorCode = "NOT_FOUND", ErrorMessage = "Template task was not found." };
            return retainTerminal(new AgentTaskControlResponse { Success = snapshot.State != AgentTaskState.Failed, ErrorCode = snapshot.Error?.Code,
                ErrorMessage = snapshot.Error?.Message, AgentTaskId = id, Snapshot = snapshot,
                ResultPayloadJson = (snapshot.Result?.Payload as AgentRunResult)?.Output, SchedulingJson = observeScheduling(id) });
        }
        private sealed class OutputFactory : IAgentRunnerFactory
        {
            private readonly IAgentRunnerFactory factory;
            private readonly AgentTaskTemplateAdmission admission;
            public OutputFactory(IAgentRunnerFactory factory, AgentTaskTemplateAdmission admission) { this.factory = factory; this.admission = admission; }
            public IAgentRunner Create(AgentRunRequest request) => new OutputRunner(factory.Create(request), admission);
        }
        private sealed class OutputRunner : IAgentRunner, IAgentExecutionObservable, IAgentUnityExecution
        {
            private readonly IAgentRunner runner;
            private readonly AgentTaskTemplateAdmission admission;
            public OutputRunner(IAgentRunner runner, AgentTaskTemplateAdmission admission) { this.runner = runner ?? throw new ArgumentNullException(nameof(runner)); this.admission = admission; }
            public AgentExecutionObservation Execution => (runner as IAgentExecutionObservable)?.Execution ?? AgentExecutionObservation.Unknown;
            public bool CanCancel => runner.CanCancel;
            public bool SupportsUnityExecution => runner is IAgentUnityExecution;
            public bool ExecutionEnded => (runner as IAgentUnityExecution)?.ExecutionEnded == true;
            public void SetExecutionDeadline(DateTimeOffset deadline) => ((IAgentUnityExecution)runner).SetExecutionDeadline(deadline);
            public void Start(AgentRunRequest request, IAgentRunnerContext context) => runner.Start(request, new OutputContext(context, admission));
            public void Advance(IAgentRunnerContext context) => runner.Advance(new OutputContext(context, admission));
            public void RequestCancellation(IAgentRunnerContext context) => runner.RequestCancellation(new OutputContext(context, admission));
        }
        private sealed class OutputContext : IAgentRunnerContext
        {
            private readonly IAgentRunnerContext inner;
            private readonly AgentTaskTemplateAdmission admission;
            public OutputContext(IAgentRunnerContext inner, AgentTaskTemplateAdmission admission) { this.inner = inner; this.admission = admission; }
            public AgentRunRequest Request => inner.Request;
            public bool CancellationRequested => inner.CancellationRequested;
            public void Complete(AgentRunResult result)
            {
                if (result == null || !result.Success) { inner.Fail(result?.Error ?? new AgentRunError("TEMPLATE_OUTPUT_INVALID", "Empty result.")); return; }
                try
                {
                    var output = JToken.Parse(result.Output);
                    AgentTaskTemplateSchema.Validate(AgentTaskTemplateCatalog.ParseObject(admission.Definition.OutputSchema), output);
                    // Built-in business result compatibility is preserved; configured results carry admission provenance.
                    inner.Complete(admission.Definition.Source == "builtin" ? result : AgentRunResult.Succeeded(JsonConvert.SerializeObject(new
                    { templateId = admission.Definition.Id, templateVersion = admission.Definition.Version, executorId = admission.Definition.ExecutorId, result = output })));
                }
                catch (Exception exception) { inner.Fail(new AgentRunError("TEMPLATE_OUTPUT_INVALID", exception.Message)); }
            }
            public void Fail(AgentRunError error) => inner.Fail(error);
            public void Cancel(AgentRunResult result = null) => inner.Cancel(result);
        }
    }
}
