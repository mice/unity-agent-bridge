namespace UnityAgentBridge.ExternalBridgeClientCore;

public sealed class ExternalBridgeClient
{
    private readonly CommandStore _store;
    private readonly BridgeHealthClient _healthClient;

    public ExternalBridgeClient()
        : this(new CommandStore(), new BridgeHealthClient())
    {
    }

    public ExternalBridgeClient(CommandStore store, BridgeHealthClient healthClient)
    {
        _store = store;
        _healthClient = healthClient;
    }

    public string? ConnectionId { get; init; }
    public Action<QueuePaths, string>? CommandDispatching { get; init; }

    public QueuePaths CreateQueuePaths(string? explicitProjectPath, string? queueRootOverride)
    {
        var projectPath = QueuePaths.ResolveProjectPath(explicitProjectPath);
        var queuePaths = new QueuePaths(projectPath, queueRootOverride ?? QueuePaths.DefaultQueueRoot);
        CommandStore.EnsureQueueDirectories(queuePaths);
        return queuePaths;
    }

    public string CreateCommandId()
    {
        return CommandIdGenerator.Next();
    }

    public async Task<ToolResultEnvelope> ExecuteAsync(QueuePaths queuePaths, string commandId, BridgeCommandSpec commandSpec, CancellationToken cancellationToken)
    {
        if (_healthClient.TryHandleLocalCommand(queuePaths, commandSpec, commandId, out var localResult, CommandDispatching, ConnectionId))
        {
            return localResult;
        }

        // Internal AgentTask control already has durable task/dispatch ownership.
        // Poll observations do not allocate user-visible command handles or history.
        var internalTaskControl = commandSpec.Tool == "unity.agent_task_control";
        var history = internalTaskControl ? null : new BridgeResultHistory(queuePaths, ConnectionId);
        var retained = history?.Read(commandId);
        if (retained is not null) return retained;
        var lifecycle = _healthClient.EvaluateLifecycle(queuePaths);
        if (lifecycle.ToolExecution == BridgeLifecycleStatus.BlockedBeforeDispatch)
        {
            return new ToolResultEnvelope(
                BridgeHealthClient.CreateLifecycleBlocked(commandId, commandSpec.Tool, commandSpec.Tool, lifecycle).ToString(Newtonsoft.Json.Formatting.None),
                "blocked",
                false);
        }

        var commandJson = AgentCommandEnvelope.Build(commandId, commandSpec.Tool, commandSpec.TimeoutMs, commandSpec.ArgsJson,
            queuePaths.TargetEditorSessionId is null ? 0 : 1, queuePaths.TargetProjectId, queuePaths.TargetEditorSessionId, ConnectionId);
        if (!internalTaskControl) CommandDispatching?.Invoke(queuePaths, commandId);
        _store.WriteInboxAtomic(queuePaths, commandId, commandJson);
        var result = await _store.WaitForResultAsync(queuePaths, commandId, commandSpec.TimeoutMs + 15000, cancellationToken);
        return history?.Save(commandId, result) ?? result;
    }
}
