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
        if (_healthClient.TryHandleLocalCommand(queuePaths, commandSpec, commandId, out var localResult))
        {
            return localResult;
        }

        var lifecycle = _healthClient.EvaluateLifecycle(queuePaths);
        if (lifecycle.ToolExecution == BridgeLifecycleStatus.BlockedBeforeDispatch)
        {
            return new ToolResultEnvelope(
                BridgeHealthClient.CreateLifecycleBlocked(commandId, commandSpec.Tool, commandSpec.Tool, lifecycle).ToString(Newtonsoft.Json.Formatting.None),
                "blocked",
                false);
        }

        var commandJson = AgentCommandEnvelope.Build(commandId, commandSpec.Tool, commandSpec.TimeoutMs, commandSpec.ArgsJson);
        _store.WriteInboxAtomic(queuePaths, commandId, commandJson);
        return await _store.WaitForResultAsync(queuePaths, commandId, commandSpec.TimeoutMs + 15000, cancellationToken);
    }
}
