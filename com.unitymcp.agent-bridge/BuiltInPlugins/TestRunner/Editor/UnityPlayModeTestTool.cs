using UnityEditor.TestTools.TestRunner.Api;
using UnityMcp.AgentBridge;

namespace UnityMcp.BuiltInPlugins.TestRunner
{
    public sealed class UnityPlayModeTestTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.run_playmode_tests",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run Unity PlayMode tests and report results.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = true,
            ArgsSchemaPath = "Documentation~/schemas/unity.run_playmode_tests.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            return UnityTestOperationManager.StartOrResume(context.Command, context.Settings, TestMode.PlayMode, args);
        }
    }
}
