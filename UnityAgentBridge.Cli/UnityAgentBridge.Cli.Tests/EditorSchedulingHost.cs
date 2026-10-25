// Fake Unity/process boundary only. EditorTaskScheduling and the journal/core are linked product sources.
using UnityMcp.AgentTaskCore;

namespace UnityEngine
{
    internal static class Debug { public static void LogWarning(object message) { } }
}
namespace UnityEditor.TestTools.TestRunner.Api
{
    internal sealed class TestRunnerApi
    {
        public static bool FakeRunActive;
        public static bool ThrowOnActivityRead;
        private static bool IsRunActive() => ThrowOnActivityRead ? throw new IOException("Activity unavailable") : FakeRunActive;
    }
}
namespace UnityEditor.TestTools.TestRunner.TestRun
{
    internal sealed class TestJobData
    {
        public string guid = "";
        public bool isRunning;
    }
    internal sealed class TestJobDataHolder
    {
        public static TestJobDataHolder instance { get; } = new();
        public readonly List<TestJobData> TestRuns = new();
    }
}
namespace UnityMcp.AgentBridge
{
    public sealed class AgentBridgeSettings
    {
        public bool enabled = true;
        public int schedulerWaitingCapacity = 32;
        public int pollIntervalMs = 200;
        public int maxToolDurationMs = 300000;
        public string tempRoot = "Temp/AgentBridge";
    }
    internal sealed class SettingsLoad { public AgentBridgeSettings Settings = new(); }
    internal static class AgentBridgeSettingsLoader { public static SettingsLoad Load() => new(); }
    internal static class AgentBridgeLocalPreferences { public static bool BridgeEnabled = true; }
    public sealed class UnityToolFacade
    {
        public Func<AgentCommand, ToolResult> Executor = _ => new ToolResult { success = true, status = ToolResultStatus.Success };
        public ToolResult ExecuteImmediate(AgentCommand command, object cancellation) => Executor(command);
        public ToolResult Execute(AgentCommand command, object cancellation) => EditorTaskScheduling.Execute(command, new AgentBridgeSettings(), () => ExecuteImmediate(command, cancellation), command.tool == "unity.ping" || command.tool == "unity.agent_task_control");
    }
    internal static class NoOpAgentCancellation { public static readonly object Instance = new(); }
    internal sealed class AgentCommandQueue(string project, string queueRoot)
    {
        internal static bool FailCompletions;
        public void Complete(string id, ToolResult result)
        {
            if (FailCompletions) throw new IOException("Injected original outbox failure.");
            var path = Path.Combine(project, queueRoot, "outbox", id + ".result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            result.commandId = id;
            if (!File.Exists(path)) File.WriteAllText(path, JsonUtil.SerializeResult(result));
        }
    }
    internal static class AgentTaskDurableResults
    {
        internal static readonly Dictionary<string, AgentTaskControlResponse> Results = new();
        public static string Root => Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath)!.FullName, "Library", "AgentBridge");
        public static string EditorSession = "S1";
        public static AgentTaskControlResponse Save(AgentTaskControlResponse response)
        {
            if (response.Snapshot?.IsTerminal == true)
            {
                var id = response.AgentTaskId ?? response.Snapshot.Id.Value;
                if (Results.TryGetValue(id, out var prior)) return prior;
                Results[id] = response;
            }
            return response;
        }
        public static AgentTaskControlResponse? Read(string id) => Results.GetValueOrDefault(id);
    }
    internal sealed class CompileLifecycleState { public int lastFinishedEpoch; }
    internal static class UnityCompileOperationManager
    {
        internal static Func<string, ToolResult?>? DeadlineCompletion;
        internal static ToolResult? CompleteOriginalDeadline(string commandId) => DeadlineCompletion?.Invoke(commandId);
    }
    internal sealed class CompileLifecycleStore(string project, string queue)
    {
        internal static int FinishedEpoch;
        public CompileLifecycleState Read() => new() { lastFinishedEpoch = FinishedEpoch };
    }
}
