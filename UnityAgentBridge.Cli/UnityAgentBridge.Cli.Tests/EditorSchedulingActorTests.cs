using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using UnityMcp.AgentBridge;
using UnityMcp.AgentTaskCore;
using UnityEditor.TestTools.TestRunner.Api;
using UnityMcp.AgentRuntime;

namespace UnityAgentBridge.Cli.Tests;

[TestClass, DoNotParallelize]
public sealed class EditorSchedulingActorTests
{
    private string root = null!;
    private string previousDataPath = null!;
    private FakeClock clock = null!;
    private AgentBridgeSettings settings = null!;
    private UnityToolFacade facade = null!;
    private readonly List<IDisposable> registrations = new();

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), nameof(EditorSchedulingActorTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content")); // Non-Unity record fixture; no project manifest/cache.
        previousDataPath = UnityEngine.Application.dataPath;
        UnityEngine.Application.dataPath = Path.Combine(root, "content");
        clock = new FakeClock();
        settings = new AgentBridgeSettings();
        facade = new UnityToolFacade();
        AgentTaskDurableResults.EditorSession = "S1";
        AgentBridgeLocalPreferences.BridgeEnabled = true;
        TestRunnerApi.FakeRunActive = false;
        TestRunnerApi.ThrowOnActivityRead = false;
        UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder.instance.TestRuns.Clear();
        UnityEditor.EditorApplication.isCompiling = UnityEditor.EditorApplication.isUpdating = UnityEditor.EditorApplication.isPlaying = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode = false;
        CompileLifecycleStore.FinishedEpoch = 0;
        UnityCompileOperationManager.DeadlineCompletion = null;
        AgentCommandQueue.FailCompletions = false;
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        AgentTaskTemplateExecutionService.ConfigureScheduling(EditorTaskScheduling.ScheduleTemplateTask, EditorTaskScheduling.CancelTask, id => EditorTaskScheduling.Observation(id));
        AgentTaskTemplateExecutionService.ConfigurePersistence(AgentTaskDurableResults.Save, id => AgentTaskDurableResults.Read(id)!);
        AgentTaskDurableResults.Results.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var registration in registrations) registration.Dispose();
        registrations.Clear();
        AgentTaskTemplateExecutionService.ConfigureScheduling(null!, null!, null!);
        AgentTaskTemplateExecutionService.ConfigurePersistence(response => response, _ => null!);
        AgentTaskDurableResults.Results.Clear();
        EditorTaskScheduling.ResetForContractTests(null!);
        UnityEngine.Application.dataPath = previousDataPath;
        TestRunnerApi.FakeRunActive = false;
        TestRunnerApi.ThrowOnActivityRead = false;
        UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder.instance.TestRuns.Clear();
        AgentCommandQueue.FailCompletions = false;
        AgentTaskDurableResults.EditorSession = "S1";
        UnityCompileOperationManager.DeadlineCompletion = null;
        Assert.IsTrue(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), nameof(EditorSchedulingActorTests))) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Directory.Delete(root, true);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_340.md
    [TestMethod, TestCategory("AGBM_340")]
    public void NativeTestAndOrdinaryCompile_ShareOneActualExecutionGate()
    {
        var operation = AdmitNative("native-test", "C1");
        Assert.AreEqual(AgentTaskState.Created, operation.Manager.Get(operation.Id).State);
        var compile = Command("compile", "unity.compile", "C2");
        var compileStarts = 0;
        var queued = EditorTaskScheduling.Execute(compile, settings, () => { compileStarts++; return Success(); });
        Assert.AreEqual(ToolResultStatus.Pending, queued.status);
        Tick();
        Assert.AreEqual(1, operation.Driver.Starts);
        Assert.AreEqual(0, compileStarts);
        operation.Manager.Fail(operation.Id, new AgentTaskError("AGENT_TASK_TIMEOUT", "Logical timeout before runner end."));
        Tick();
        Assert.AreEqual(0, compileStarts, "Logical failure cannot release a physical execution owner.");
        Assert.IsTrue(JObject.Parse(EditorTaskScheduling.Observation()).Value<bool>("executionOccupied"));
        operation.Ended = true;
        Tick();
        Assert.AreEqual(1, compileStarts);
        Assert.AreEqual("C1", JObject.Parse(EditorTaskScheduling.Observation(operation.Id.Value)).Value<string>("connectionId"));
        Assert.AreEqual("C2", JObject.Parse(EditorTaskScheduling.Observation("compile")).Value<string>("connectionId"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_341.md
    [TestMethod, TestCategory("AGBM_341")]
    public void QueuedNativeCancellation_IsDurableAndNeverInvokesDriver()
    {
        var operation = AdmitNative("cancel-before-start");
        Assert.AreEqual(AgentTaskCancellationDisposition.Accepted, EditorTaskScheduling.CancelTask(operation.Manager, operation.Id));
        Tick();
        Assert.AreEqual(0, operation.Driver.Starts);
        Assert.AreEqual(AgentTaskState.Cancelled, operation.Manager.Get(operation.Id).State);
        Assert.AreEqual("Cancelled", EditorTaskScheduling.FindTask(operation.Id.Value).Outcome);
        Assert.IsTrue(EditorTaskScheduling.FindTask(operation.Id.Value).DeliveryAcknowledged);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_342.md
    [TestMethod, TestCategory("AGBM_342")]
    public void OriginalDeadline_ExpiresQueuedNativeAndOrdinaryRequests()
    {
        var native = AdmitNative("expires-native", milliseconds: 100);
        var starts = 0;
        var command = Command("expires-ordinary", "unity.custom", milliseconds: 100);
        EditorTaskScheduling.Execute(command, settings, () => { starts++; return Success(); });
        clock.Advance(100);
        Tick();
        Assert.AreEqual(0, native.Driver.Starts);
        Assert.AreEqual(0, starts);
        Assert.AreEqual(AgentTaskState.Failed, native.Manager.Get(native.Id).State);
        Assert.AreEqual("SCHEDULER_QUEUE_TIMEOUT", native.Manager.Get(native.Id).Error.Code);
        Assert.AreEqual("timeout", ReadResult(command.commandId).Value<string>("status"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_343.md
    [TestMethod, TestCategory("AGBM_343")]
    public void WaitingConsumesBudgetBeforeDriverStart()
    {
        var native = AdmitNative("remaining-budget", milliseconds: 1000);
        clock.Advance(600);
        Tick();
        Assert.AreEqual(400, native.RemainingBudget);
        Assert.AreEqual(1, native.Driver.Starts);
        clock.Advance(400);
        Tick();
        Assert.AreEqual("AGENT_TASK_TIMEOUT", native.Manager.Get(native.Id).Error.Code);
        Assert.IsTrue(EditorTaskScheduling.FindTask(native.Id.Value).OwnsExecution);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_344.md
    [TestMethod, TestCategory("AGBM_344")]
    public void OrdinaryBudget_IsReducedBeforeCallingCapturedExecutor()
    {
        var command = Command("remaining-ordinary", "unity.custom", milliseconds: 1000);
        var observedBudget = 0;
        EditorTaskScheduling.Execute(command, settings, () => { observedBudget = command.timeoutMs; return Success(); });
        clock.Advance(600);
        Tick();
        Assert.AreEqual(400, observedBudget);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_345.md
    [TestMethod, TestCategory("AGBM_345")]
    public void ManualActivity_BlocksExecutionButSafeQueriesStillReturn()
    {
        var native = AdmitNative("wait-for-person");
        TestRunnerApi.FakeRunActive = true;
        Tick();
        Assert.AreEqual(0, native.Driver.Starts);
        StringAssert.Contains(JObject.Parse(EditorTaskScheduling.Observation(native.Id.Value)).Value<string>("blockingReason"), "Test Runner");
        var queryCount = 0;
        var result = EditorTaskScheduling.Execute(Command("query", "unity.ping"), settings, () => { queryCount++; return Success(); }, concurrentObservation: true);
        Assert.IsTrue(result.success);
        Assert.AreEqual(1, queryCount);
        Assert.AreEqual(0, native.Driver.Starts);
        TestRunnerApi.FakeRunActive = false;
        Tick();
        Assert.AreEqual(1, native.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_346.md
    [TestMethod, TestCategory("AGBM_346")]
    public void UnreadableActivity_FailsClosedUntilAnExplicitIdleObservation()
    {
        var native = AdmitNative("unreadable-activity");
        TestRunnerApi.ThrowOnActivityRead = true;
        Tick();
        Assert.AreEqual(0, native.Driver.Starts);
        TestRunnerApi.ThrowOnActivityRead = false;
        Tick();
        Assert.AreEqual(1, native.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_347.md
    [TestMethod, TestCategory("AGBM_347")]
    public void UnknownReadOnlyClaim_CannotBypassTheExecutionOwner()
    {
        var native = AdmitNative("owner");
        Tick();
        var starts = 0;
        var unknown = Command("claimed-safe", "unity.unknown");
        unknown.rawArgsJson = "{\"readOnly\":true}";
        EditorTaskScheduling.Execute(unknown, settings, () => { starts++; return Success(); });
        Tick();
        Assert.AreEqual(0, starts);
        Assert.AreEqual(1, native.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_348.md
    [TestMethod, TestCategory("AGBM_348")]
    public void NativeQueueFull_RejectsAllocatedTaskWithoutAnOrphanOrDriverStart()
    {
        settings.schedulerWaitingCapacity = 1;
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        var first = AdmitNative("first");
        var rejected = AdmitNative("rejected");
        Assert.AreEqual(AgentTaskState.Failed, rejected.Manager.Get(rejected.Id).State);
        Assert.AreEqual("SCHEDULER_QUEUE_FULL", rejected.Manager.Get(rejected.Id).Error.Code);
        Assert.AreEqual(0, rejected.Driver.Starts);
        Assert.AreEqual(AgentTaskState.Created, first.Manager.Get(first.Id).State);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_349.md
    [TestMethod, TestCategory("AGBM_349")]
    public void DuplicateCreate_ReconcilesOriginalTaskWithoutRegisteringAnother()
    {
        var operation = AdmitNative("original-native");
        var create = new AgentTaskControlRequest { Operation = "create", TaskType = "run_editmode_tests", Payload = "{}" };
        var duplicate = EditorTaskScheduling.WithSource(operation.Source, () => EditorTaskScheduling.ReconcileCreation(create));
        Assert.AreEqual(AgentTaskControlProtocol.Query, duplicate.Operation);
        Assert.AreEqual(operation.Id.Value, duplicate.AgentTaskId);
        create.Payload = "{\"changed\":true}";
        Assert.ThrowsException<InvalidOperationException>(() => EditorTaskScheduling.WithSource(operation.Source, () => EditorTaskScheduling.ReconcileCreation(create)));
        Tick(); Tick();
        Assert.AreEqual(1, operation.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_350.md
    [TestMethod, TestCategory("AGBM_350")]
    public void ReconfigureAndBridgeDisable_DoNotEraseActiveOwnership()
    {
        var first = AdmitNative("owner");
        Tick();
        var next = AdmitNative("next");
        settings.enabled = false;
        EditorTaskScheduling.Configure(new UnityToolFacade(), settings);
        Tick();
        Assert.AreEqual(0, next.Driver.Starts);
        settings.enabled = true;
        Tick();
        Assert.AreEqual(0, next.Driver.Starts);
        first.Ended = true;
        Tick();
        Assert.AreEqual(1, next.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_351.md
    [TestMethod, TestCategory("AGBM_351")]
    public void Reload_RestoresCreatedIdentityAndDoesNotResetTheDeadline()
    {
        var command = Command("reload-wait", "unity.custom", milliseconds: 1000);
        EditorTaskScheduling.Execute(command, settings, Success);
        var originalDeadline = EditorTaskScheduling.FindTask(command.commandId).Request.Deadline;
        clock.Advance(700);
        EditorTaskScheduling.ResetForContractTests(clock);
        var starts = 0;
        var remaining = 0;
        facade.Executor = request => { starts++; remaining = request.timeoutMs; return Success(); };
        EditorTaskScheduling.Configure(facade, settings);
        Assert.AreEqual(originalDeadline, EditorTaskScheduling.FindTask(command.commandId).Request.Deadline);
        Tick();
        Assert.AreEqual(1, starts);
        Assert.AreEqual(300, remaining);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_352.md
    [TestMethod, TestCategory("AGBM_352")]
    public void ReloadAfterDispatch_DoesNotReplayAnUncertainStart()
    {
        var command = Command("reload-active", "unity.unknown-async");
        var starts = 0;
        EditorTaskScheduling.Execute(command, settings, () => { starts++; return new ToolResult { status = ToolResultStatus.Pending }; });
        Tick();
        Assert.AreEqual(1, starts);
        EditorTaskScheduling.ResetForContractTests(clock);
        facade.Executor = _ => { starts++; return Success(); };
        EditorTaskScheduling.Configure(facade, settings);
        Tick();
        Assert.AreEqual(1, starts);
        Assert.IsTrue(JObject.Parse(EditorTaskScheduling.Observation()).Value<bool>("executionOccupied"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_353.md
    [TestMethod, TestCategory("AGBM_353")]
    public void CompileRecovery_UsesOriginalEpochFinishEvenAfterLogicalTimeout()
    {
        var command = Command("compile-epoch", "unity.compile");
        EditorTaskScheduling.Execute(command, settings, () =>
        {
            EditorTaskScheduling.SetOperationCorrelation(command.commandId, "compile:7");
            return new ToolResult { status = ToolResultStatus.Pending };
        });
        Tick();
        CompileLifecycleStore.FinishedEpoch = 6;
        Tick();
        Assert.IsTrue(EditorTaskScheduling.FindTask(command.commandId).OwnsExecution);
        CompileLifecycleStore.FinishedEpoch = 7;
        Tick();
        Assert.IsTrue(EditorTaskScheduling.FindTask(command.commandId).ExecutionEnded);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_354.md
    [TestMethod, TestCategory("AGBM_354")]
    public void LegacyProcessingWithoutEvidence_IsDiagnosedAndNeverExecuted()
    {
        var starts = 0;
        facade.Executor = _ => { starts++; return Success(); };
        var result = EditorTaskScheduling.RecoverCommand(Command("untracked-processing", "unity.unknown"));
        Assert.AreEqual("AGENT_TASK_OUTCOME_UNKNOWN", result.errors.Single().code);
        Assert.AreEqual(0, starts);
        var queued = AdmitNative("must-not-overlap-legacy");
        Assert.AreEqual(AgentTaskState.Failed, queued.Manager.Get(queued.Id).State);
        Tick();
        Assert.AreEqual(0, queued.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_355.md
    [TestMethod, TestCategory("AGBM_355")]
    public void FailedOutbox_ReloadRetriesOriginalPayloadWithoutRepeatingExecutor()
    {
        var command = Command("pending-output", "unity.custom");
        var starts = 0;
        AgentCommandQueue.FailCompletions = true;
        EditorTaskScheduling.Execute(command, settings, () =>
        {
            starts++;
            return new ToolResult { success = true, status = ToolResultStatus.Success, metricsObjectJson = "{\"answer\":42}" };
        });
        Tick(); Tick();
        var pending = EditorTaskScheduling.FindTask(command.commandId);
        Assert.AreEqual("Completed", pending.Outcome);
        Assert.IsTrue(pending.ExecutionEnded, "Outbox delivery must not retain a finished physical execution slot.");
        Assert.IsFalse(pending.DeliveryAcknowledged);
        Assert.IsNotNull(pending.PendingResultJson);
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        AgentCommandQueue.FailCompletions = false;
        Tick();
        Assert.AreEqual(1, starts);
        Assert.AreEqual(42, ReadResult(command.commandId)["metrics"]!.Value<int>("answer"));
        Assert.IsTrue(EditorTaskScheduling.FindTask(command.commandId).DeliveryAcknowledged);
        Assert.IsNull(EditorTaskScheduling.FindTask(command.commandId).PendingResultJson);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_356.md
    [TestMethod, TestCategory("AGBM_356")]
    public void DeadlineElapsedDuringStartupPersistence_DoesNotInvokeDriver()
    {
        var operation = AdmitNative("deadline-during-prepare", milliseconds: 100);
        operation.BeforeStart = () => clock.Advance(101);
        Tick();
        Assert.AreEqual(0, operation.Driver.Starts);
        Assert.AreEqual(AgentTaskState.Failed, operation.Manager.Get(operation.Id).State);
        Assert.AreEqual("SCHEDULER_QUEUE_TIMEOUT", operation.Manager.Get(operation.Id).Error.Code);
        Assert.IsTrue(EditorTaskScheduling.FindTask(operation.Id.Value).ExecutionEnded);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_357.md
    [TestMethod, TestCategory("AGBM_357")]
    public void ExternalTemplate_CanRunWhileUnitySlotIsOccupied()
    {
        var active = AdmitNative("unity-owner");
        Tick();
        var executor = new TemplateExecutor();
        ConfigureTemplate(executor, requiresUnity: false);
        var result = RunTemplate("external-template");
        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(1, executor.Runner.Starts);
        Assert.AreEqual(active.Id.Value, EditorTaskScheduling.FindTask(active.Id.Value).Request.RequestId);
        Assert.IsNull(EditorTaskScheduling.FindTask(result.AgentTaskId));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_358.md
    [TestMethod, TestCategory("AGBM_358")]
    public void UnityTemplate_IsQueuedWithCapturedInputsAndOriginalDeadline()
    {
        var executor = new TemplateExecutor();
        ConfigureTemplate(executor, requiresUnity: true);
        var result = RunTemplate("unity-template");
        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(AgentTaskState.Created, result.Snapshot.State);
        Assert.AreEqual(0, executor.Runner.Starts);
        clock.Advance(400);
        Tick();
        Assert.AreEqual(1, executor.Runner.Starts);
        Assert.AreEqual(clock.UtcNow.AddMilliseconds(600), executor.Runner.Deadline);
        StringAssert.Contains(executor.Runner.Prompt, "original");
        Assert.AreEqual(1, executor.Prepares, "Dispatch must use captured authority rather than Prepare again.");
        var observation = AgentTaskTemplateExecutionService.Control(new AgentTaskControlRequest { Operation = "query", AgentTaskId = result.AgentTaskId });
        Assert.AreEqual(AgentTaskState.Running, observation.Snapshot.State);
        Assert.AreEqual(1, executor.Runner.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_359.md
    [TestMethod, TestCategory("AGBM_359")]
    public void UnityTemplateRetirement_CancelsQueuedWorkBeforeStartup()
    {
        var executor = new TemplateExecutor();
        var registration = ConfigureTemplate(executor, requiresUnity: true);
        var result = RunTemplate("retired-template");
        registration.Dispose();
        Tick();
        Assert.AreEqual(0, executor.Runner.Starts);
        Assert.AreEqual("Cancelled", EditorTaskScheduling.FindTask(result.AgentTaskId).Outcome);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_360.md
    [TestMethod, TestCategory("AGBM_360")]
    public void OpaquePreparedTemplateAuthority_IsNotRecreatedAfterReload()
    {
        var executor = new TemplateExecutor();
        ConfigureTemplate(executor, requiresUnity: true);
        var result = RunTemplate("reload-template");
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        Tick();
        Assert.AreEqual(0, executor.Runner.Starts);
        Assert.AreEqual(1, executor.Prepares);
        Assert.AreEqual("TEMPLATE_RECOVERY_UNAVAILABLE", EditorTaskScheduling.FindTask(result.AgentTaskId).ErrorCode);
        Assert.AreEqual(AgentTaskState.Failed, AgentTaskDurableResults.Results[result.AgentTaskId].Snapshot.State);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_361.md
    [TestMethod, TestCategory("AGBM_361")]
    public void UnknownExecutor_DefaultsToUnityAdmissionAndNeedsCompletionSupport()
    {
        var executor = new UnobservableExecutor();
        var registration = AgentTaskTemplateExecutors.Register(executor);
        registrations.Add(registration);
        SaveTemplate(executor.Id);
        Assert.IsTrue(registration.RequiresUnityExecution);
        var result = RunTemplate("unsupported-completion");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("EXECUTOR_COMPLETION_UNOBSERVABLE", result.ErrorCode);
        Assert.IsNull(result.AgentTaskId);
        Assert.AreEqual(0, executor.Runner.Starts);
    }

    private AgentTaskTemplateExecutorRegistration ConfigureTemplate(TemplateExecutor executor, bool requiresUnity)
    {
        var registration = AgentTaskTemplateExecutors.Register(executor, requiresUnity);
        registrations.Add(registration);
        SaveTemplate(executor.Id);
        return registration;
    }
    private void SaveTemplate(string executorId)
    {
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings")); // Configuration data only, not a Unity validation project.
        File.WriteAllText(Path.Combine(root, "ProjectSettings/AgentTaskTemplates.json"), new JObject
        {
            ["schemaVersion"] = 1,
            ["templates"] = new JArray(new JObject
            {
                ["id"] = "project.scheduling", ["displayName"] = "Scheduling fixture", ["description"] = "Contract fixture",
                ["enabled"] = true, ["executorId"] = executorId,
                ["inputSchema"] = JObject.Parse("{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"message\"],\"additionalProperties\":false}"), ["outputSchema"] = JObject.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"),
                ["execution"] = new JObject { ["prompt"] = "Captured configuration", ["timeoutMs"] = 1000 }
            })
        }.ToString());
    }
    private AgentTaskControlResponse RunTemplate(string dispatch)
    {
        var request = new AgentTaskControlRequest { Operation = "create", TaskType = "template_run", TimeoutMs = 1000,
            Payload = "{\"templateId\":\"project.scheduling\",\"inputs\":{\"message\":\"original\"}}" };
        return EditorTaskScheduling.WithSource(Command(dispatch, "unity.agent_task_control", milliseconds: 1000), () => AgentTaskTemplateExecutionService.Control(request));
    }

    private sealed class TemplateExecutor : IAgentTaskTemplateExecutor
    {
        public string Id => "fixture.unity.scheduling";
        public string ConfigurationSchemaJson => "{\"type\":\"object\",\"properties\":{\"prompt\":{\"type\":\"string\",\"minLength\":1},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000}},\"required\":[\"prompt\",\"timeoutMs\"],\"additionalProperties\":false}";
        public UnityRunner Runner = new();
        public int Prepares;
        public string Validate(AgentTaskTemplateDefinition definition) => null!;
        public IAgentRunnerFactory Prepare(AgentTaskTemplateAdmission admission) { Prepares++; return new Factory(Runner); }
    }
    private sealed class UnobservableExecutor : IAgentTaskTemplateExecutor
    {
        public string Id => "fixture.unobservable.scheduling";
        public string ConfigurationSchemaJson => "{\"type\":\"object\",\"properties\":{\"prompt\":{\"type\":\"string\",\"minLength\":1},\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000}},\"required\":[\"prompt\",\"timeoutMs\"],\"additionalProperties\":false}";
        public PlainRunner Runner = new();
        public string Validate(AgentTaskTemplateDefinition definition) => null!;
        public IAgentRunnerFactory Prepare(AgentTaskTemplateAdmission admission) => new Factory(Runner);
    }
    private sealed class Factory(IAgentRunner runner) : IAgentRunnerFactory { public IAgentRunner Create(AgentRunRequest request) => runner; }
    private class PlainRunner : IAgentRunner
    {
        public int Starts;
        public string Prompt = "";
        public bool CanCancel => false;
        public void Start(AgentRunRequest request, IAgentRunnerContext context) { Starts++; Prompt = request.Task; }
        public void Advance(IAgentRunnerContext context) { }
        public void RequestCancellation(IAgentRunnerContext context) { }
    }
    private sealed class UnityRunner : PlainRunner, IAgentUnityExecution
    {
        public bool ExecutionEnded { get; set; }
        public DateTimeOffset Deadline;
        public void SetExecutionDeadline(DateTimeOffset deadline) => Deadline = deadline;
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_362.md
    [TestMethod, TestCategory("AGBM_362")]
    public void IndependentManagersWithoutGate_ReproduceConflictingActualStarts()
    {
        var test = new Driver();
        var compile = new Driver();
        var first = new AgentTaskManager(clock: clock);
        var second = new AgentTaskManager(clock: clock);
        first.Start(first.Register(test));
        second.Start(second.Register(compile));
        Assert.AreEqual(1, test.Starts);
        Assert.AreEqual(1, compile.Starts);
        // Characterizes the old service-local policy; the shared-gate test proves its correction.
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_363.md
    [TestMethod, TestCategory("AGBM_363")]
    public void QueuedOrdinaryCancellation_NeverInvokesCapturedExecutor()
    {
        var starts = 0;
        var command = Command("cancel-command", "unity.custom");
        EditorTaskScheduling.Execute(command, settings, () => { starts++; return Success(); });
        Assert.AreEqual(AgentTaskCancellationDisposition.Accepted, EditorTaskScheduling.CancelCommand(command.commandId).Cancellation);
        Tick();
        Assert.AreEqual(0, starts);
        Assert.AreEqual("cancelled", ReadResult(command.commandId).Value<string>("status"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_364.md
    [TestMethod, TestCategory("AGBM_364")]
    public void OrdinaryNestedTask_ReloadReattachesOriginalIdentityWithoutStartingAgain()
    {
        var manager = new AgentTaskManager(clock: clock);
        var driver = new Driver();
        var id = manager.Register(driver);
        var command = Command("outer-linked", "unity.custom");
        var inner = Command("inner-linked", "unity.run_editmode_tests");
        EditorTaskScheduling.Execute(command, settings, () =>
        {
            EditorTaskScheduling.ScheduleTask(manager, id, inner, "run_editmode_tests",
                _ => EditorTaskScheduling.StartTask(manager, id), () => null!);
            return new ToolResult { status = ToolResultStatus.Pending };
        });
        Tick();
        Assert.AreEqual(1, driver.Starts);
        Assert.AreEqual(command.commandId, EditorTaskScheduling.FindTask(id.Value).Request.CommandId);
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        EditorTaskScheduling.ScheduleTask(manager, id, inner, "run_editmode_tests",
            _ => Assert.Fail("Recovery must not repeat Start."), () => null!);
        Tick();
        Assert.AreEqual(1, driver.Starts);
        Assert.AreEqual(id.Value, EditorTaskScheduling.FindTask(command.commandId).LinkedTaskId);
        Assert.IsTrue(EditorTaskScheduling.FindTask(id.Value).OwnsExecution);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_365.md
    [TestMethod, TestCategory("AGBM_365")]
    public void QueuedOrdinaryDispatch_UsesLatestFacadeAndOriginalQueueRoot()
    {
        var command = Command("changed-settings", "unity.custom");
        var originalStarts = 0;
        EditorTaskScheduling.Execute(command, settings, () => { originalStarts++; return Success(); }, useCurrentFacade: true);
        var replacement = new UnityToolFacade();
        var updated = new AgentBridgeSettings { tempRoot = "Temp/ReplacementQueue" };
        var starts = 0;
        replacement.Executor = _ =>
        {
            starts++;
            Assert.AreEqual("Temp/AgentBridge", EditorTaskScheduling.GetQueueRoot(updated.tempRoot));
            return Success();
        };
        EditorTaskScheduling.Configure(replacement, updated);
        Tick(); Tick();
        Assert.AreEqual(0, originalStarts);
        Assert.AreEqual(1, starts);
        Assert.AreEqual("success", ReadResult(command.commandId).Value<string>("status"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_366.md
    [TestMethod, TestCategory("AGBM_366")]
    public void FreshJournal_DetectsLegacyCompileDataBeforeAnyNewStart()
    {
        EditorTaskScheduling.ResetForContractTests(clock);
        var journal = Path.Combine(root, "Library/AgentBridge/scheduler/execution.json");
        if (File.Exists(journal)) File.Delete(journal);
        if (File.Exists(journal + ".initialized")) File.Delete(journal + ".initialized");
        var directory = Path.Combine(root, "Library/AgentBridge/compile-operations");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "old.compile.data"), "{}");
        EditorTaskScheduling.Configure(facade, settings);
        var operation = AdmitNative("after-legacy");
        Tick();
        Assert.AreEqual(0, operation.Driver.Starts);
        Assert.AreEqual(AgentTaskState.Failed, operation.Manager.Get(operation.Id).State);
        Assert.AreEqual("blocked", JObject.Parse(EditorTaskScheduling.Observation()).Value<string>("stage"));
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_368.md
    [TestMethod, TestCategory("AGBM_368")]
    public void ApiTestJobWithIdleLegacyFlag_RetainsOwnershipUntilActualJobEnd()
    {
        var owner = AdmitNative("api-job-owner");
        Tick();
        var jobs = UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder.instance.TestRuns;
        jobs.Add(new UnityEditor.TestTools.TestRunner.TestRun.TestJobData { guid = "api-only-run", isRunning = true });
        EditorTaskScheduling.NotifyTestStarted("private-api-job-owner", "api-only-run");
        var waiting = AdmitNative("behind-api-job");
        Tick();
        Assert.IsFalse(TestRunnerApi.FakeRunActive, "The legacy RunData flag remains false for this API runner.");
        Assert.IsTrue(EditorTaskScheduling.FindTask(owner.Id.Value).OwnsExecution);
        Assert.AreEqual(0, waiting.Driver.Starts);
        jobs.Clear();
        Tick();
        Assert.IsTrue(EditorTaskScheduling.FindTask(owner.Id.Value).ExecutionEnded);
        Assert.AreEqual(1, waiting.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_369.md
    [TestMethod, TestCategory("AGBM_369")]
    public void ManualApiTestJobWithIdleLegacyFlag_BlocksFirstDispatch()
    {
        var jobs = UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder.instance.TestRuns;
        jobs.Add(new UnityEditor.TestTools.TestRunner.TestRun.TestJobData { guid = "manual-api-run", isRunning = true });
        var waiting = AdmitNative("behind-manual-api-job");
        Tick();
        Assert.AreEqual(0, waiting.Driver.Starts);
        var health = JObject.Parse(EditorTaskScheduling.Observation());
        Assert.AreEqual("queued", health.Value<string>("stage"));
        StringAssert.Contains(health.Value<string>("blockingReason"), "Test Runner");
        jobs.Clear();
        Tick();
        Assert.AreEqual(1, waiting.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_375.md
    [TestMethod, TestCategory("AGBM_375")]
    public void OrdinaryCompileDeadline_PreservesTypedDiagnosticsAndPhysicalOwnership()
    {
        var command = Command("typed-compile-timeout", "unity.compile", milliseconds: 500);
        var typed = new ToolResult { status = ToolResultStatus.Timeout, success = false,
            metricsObjectJson = "{\"contractVersion\":\"compile.v1\",\"diagnosticsComplete\":false,\"errorCount\":0}" };
        UnityCompileOperationManager.DeadlineCompletion = id =>
        {
            Assert.AreEqual(command.commandId, id);
            new AgentCommandQueue(root, settings.tempRoot).Complete(id, typed);
            return typed;
        };
        EditorTaskScheduling.Execute(command, settings, () => new ToolResult { status = ToolResultStatus.Running });
        Tick();
        UnityEditor.EditorApplication.isCompiling = true;
        var waiting = AdmitNative("behind-typed-compile");
        clock.Advance(501);
        Tick();
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(typed.metricsObjectJson), ReadResult(command.commandId)["metrics"]));
        Assert.IsTrue(EditorTaskScheduling.FindTask(command.commandId).OwnsExecution);
        Assert.AreEqual(0, waiting.Driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_376.md
    [TestMethod, TestCategory("AGBM_376")]
    public void NativeCompileDeadline_AdvancesOriginalDriverBeforeGenericFailure()
    {
        var command = Command("native-typed-timeout", "unity.compile", milliseconds: 500);
        var manager = new AgentTaskManager(clock: clock);
        var driver = new TypedTimeoutDriver();
        var id = manager.Register(driver);
        EditorTaskScheduling.ScheduleTask(manager, id, command, "compile", _ => EditorTaskScheduling.StartTask(manager, id), () => null!);
        UnityCompileOperationManager.DeadlineCompletion = commandId =>
        {
            Assert.AreEqual(command.commandId, commandId);
            driver.DeadlineReached = true;
            return new ToolResult { status = ToolResultStatus.Timeout };
        };
        Tick();
        UnityEditor.EditorApplication.isCompiling = true;
        clock.Advance(501);
        Tick();
        var snapshot = manager.Get(id);
        Assert.AreEqual(AgentTaskState.Completed, snapshot.State);
        Assert.AreEqual(AgentTaskResultKind.TimedOut, snapshot.Result.Kind);
        Assert.AreEqual("typed compile diagnostics", snapshot.Result.Payload);
        Assert.IsTrue(EditorTaskScheduling.FindTask(id.Value).OwnsExecution);
        Assert.AreEqual(1, driver.Starts);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_377.md
    [TestMethod, TestCategory("AGBM_377")]
    public void MutableSettingsLifetime_AppliesToNewAdmissionWithoutChangingOldDeadline()
    {
        settings.maxToolDurationMs = 500;
        EditorTaskScheduling.Configure(facade, settings);
        UnityEditor.EditorApplication.isUpdating = true;
        var old = Command("old-lifetime", "unity.compile", milliseconds: 10000);
        EditorTaskScheduling.Execute(old, settings, Success);
        var before = EditorTaskScheduling.FindTask(old.commandId);
        settings.maxToolDurationMs = 10000;
        var current = Command("new-lifetime", "unity.compile", milliseconds: 10000);
        var response = EditorTaskScheduling.Execute(current, settings, Success);
        Assert.AreEqual(ToolResultStatus.Pending, response.status);
        Assert.AreEqual(clock.UtcNow.AddMilliseconds(10000), EditorTaskScheduling.FindTask(current.commandId).Request.Deadline);
        Assert.AreEqual(before.Request.Deadline, EditorTaskScheduling.FindTask(old.commandId).Request.Deadline);
    }

    // TestRecord: com.unitymcp.agent-bridge/Documentation~/test_records/AGBM_378.md
    [TestMethod, TestCategory("AGBM_378")]
    public void TimedOutCompileCallback_KeepsOriginalQueueAcrossSettingsAndReload()
    {
        settings.tempRoot = "Temp/original-compile";
        EditorTaskScheduling.Configure(facade, settings);
        var command = Command("original-compile-root", "unity.compile", milliseconds: 500);
        EditorTaskScheduling.Execute(command, settings, () => new ToolResult { status = ToolResultStatus.Running });
        Tick();
        EditorTaskScheduling.SetOperationCorrelation(command.commandId, "compile:7");
        UnityEditor.EditorApplication.isCompiling = true;
        settings.tempRoot = "Temp/new-settings";
        clock.Advance(501);
        Tick();
        Assert.AreEqual("TimedOut", EditorTaskScheduling.FindTask(command.commandId).Outcome);
        Assert.AreEqual("Temp/original-compile", EditorTaskScheduling.ActiveCompileQueueRoot);
        EditorTaskScheduling.ResetForContractTests(clock);
        EditorTaskScheduling.Configure(facade, settings);
        Assert.AreEqual("Temp/original-compile", EditorTaskScheduling.ActiveCompileQueueRoot);
        CompileLifecycleStore.FinishedEpoch = 7;
        UnityEditor.EditorApplication.isCompiling = false;
        Tick();
        Assert.IsNull(EditorTaskScheduling.ActiveCompileQueueRoot);
        Assert.IsTrue(EditorTaskScheduling.FindTask(command.commandId).ExecutionEnded);
    }

    private sealed class TypedTimeoutDriver : IAgentTask
    {
        public bool DeadlineReached;
        public int Starts;
        public bool CanCancel => false;
        public void Start(IAgentTaskContext context) => Starts++;
        public void Advance(IAgentTaskContext context)
        {
            if (DeadlineReached) context.Complete(AgentTaskResult.TimedOut("Original deadline", "typed compile diagnostics"));
        }
        public void RequestCancellation(IAgentTaskContext context) => throw new NotSupportedException();
    }

    private Operation AdmitNative(string id, string connection = "C1", int milliseconds = 10000)
    {
        var operation = new Operation { Manager = new AgentTaskManager(clock: clock, idFactory: () => new AgentTaskId(id)), Driver = new Driver() };
        operation.Id = operation.Manager.Register(operation.Driver);
        operation.Source = Command("dispatch-" + id, "unity.agent_task_control", connection, milliseconds);
        EditorTaskScheduling.WithSource(operation.Source, () =>
        {
            var command = EditorTaskScheduling.TaskCommand("private-" + id, "unity.run_editmode_tests", milliseconds, "{}");
            EditorTaskScheduling.ScheduleTask(operation.Manager, operation.Id, command, "run_editmode_tests", remaining => { operation.RemainingBudget = remaining; operation.BeforeStart?.Invoke(); EditorTaskScheduling.StartTask(operation.Manager, operation.Id); }, () => operation.Ended ? "Original fake runner actually ended." : null);
            return true;
        });
        return operation;
    }

    private AgentCommand Command(string id, string tool, string connection = "C1", int milliseconds = 10000) => new()
    {
        commandId = id, tool = tool, timeoutMs = milliseconds, createdAt = clock.UtcNow.ToString("O"),
        connectionId = connection, editorSessionId = AgentTaskDurableResults.EditorSession,
        projectId = "contract-project", rawArgsJson = "{}"
    };
    private JObject ReadResult(string id) => JObject.Parse(File.ReadAllText(Path.Combine(root, "Temp/AgentBridge/outbox", id + ".result.json")));
    private static ToolResult Success() => new() { success = true, status = ToolResultStatus.Success };
    private static void Tick()
    {
        UnityEditor.EditorApplication.timeSinceStartup += 0.21;
        UnityEditor.EditorApplication.Tick();
    }

    private sealed class Operation
    {
        public AgentTaskManager Manager = null!;
        public Driver Driver = null!;
        public AgentTaskId Id;
        public AgentCommand Source = null!;
        public bool Ended;
        public int RemainingBudget;
        public Action? BeforeStart;
    }
    private sealed class Driver : IAgentTask
    {
        public int Starts;
        public bool CanCancel => false;
        public void Start(IAgentTaskContext context) => Starts++;
        public void Advance(IAgentTaskContext context) { }
        public void RequestCancellation(IAgentTaskContext context) => throw new NotSupportedException();
    }
    private sealed class FakeClock : IAgentTaskClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        public void Advance(int milliseconds) => UtcNow = UtcNow.AddMilliseconds(milliseconds);
    }
}
