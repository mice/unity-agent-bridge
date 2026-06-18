using System;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.run_diagnostic")]
    public sealed class UnityDiagnosticTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.run_diagnostic",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Run a Unity asset or scene diagnostic against a target path.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.run_diagnostic.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!JsonUtil.TryDeserializeArgs<UnityDiagnosticArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            if (string.IsNullOrWhiteSpace(args.diagnosticType))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", "diagnosticType is required.");
            }

            if (string.IsNullOrWhiteSpace(args.targetPath))
            {
                return ToolResult.InvalidArgs("AGENTBRIDGE_REQUIRED_FIELD_MISSING", "targetPath is required.");
            }

            return DiagnosticRunner.Run(args, context, cancellation);
        }
    }

    [Serializable]
    public sealed class UnityDiagnosticArgs
    {
        public string diagnosticType;
        public string targetPath;
        public int timeoutMs;
    }
}
