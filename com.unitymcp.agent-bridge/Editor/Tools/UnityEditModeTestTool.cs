using System;
using UnityEditor.TestTools.TestRunner.Api;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.run_editmode_tests")]
    public sealed class UnityEditModeTestTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.run_editmode_tests",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run Unity EditMode tests and report results.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.RunsUserCode,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.run_editmode_tests.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<UnityTestRunArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            return UnityTestOperationManager.StartOrResume(context.Command, context.Settings, TestMode.EditMode, args);
        }
    }

    [Serializable]
    public sealed class UnityTestRunArgs
    {
        public string filter;
        public string[] testNames;
        public string[] assemblyNames;
        public string[] categoryNames;
        public string[] groupNames;
        public int timeoutMs;
    }
}
