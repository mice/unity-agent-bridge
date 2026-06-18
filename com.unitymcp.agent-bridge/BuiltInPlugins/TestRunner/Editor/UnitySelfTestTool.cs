using System;
using UnityMcp.AgentBridge;

namespace UnityMcp.BuiltInPlugins.TestRunner
{
    public sealed class UnitySelfTestTool : IAgentTool
    {
        private readonly AgentBridgeSelfTestRunner _runner;

        public UnitySelfTestTool(AgentBridgeSelfTestRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.agent_bridge_self_test",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run the Unity Agent Bridge self-test suite.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = true,
            ArgsSchemaPath = "Documentation~/schemas/unity.agent_bridge_self_test.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<SelfTestRunOptions>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            return _runner.StartOrResume(context.Command, args, cancellation);
        }
    }
}
