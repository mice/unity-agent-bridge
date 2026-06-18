using Newtonsoft.Json.Linq;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    public sealed class UnityGetEditorStateTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.get_editor_state",
            Title = "Unity Get Editor State",
            Description = "Report Unity Editor runtime state, active scene, and loaded scenes without mutating project state.",
            DefaultTimeoutMs = 10000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = "{\"type\":\"object\",\"properties\":{\"timeoutMs\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":9007199254740991}},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!EditorBasicsJson.TryDeserializeArgs<UnityGetEditorStateArgs>(context.RawArgsJson, out _, out var failure))
            {
                return failure;
            }

            var snapshot = EditorBasicsEditorStateSnapshotBuilder.Build();
            var metrics = new EditorStateMetrics
            {
                editorState = snapshot,
                runtimeMode = snapshot.runtimeMode,
                isCompiling = snapshot.flags.isCompiling,
                isUpdating = snapshot.flags.isUpdating,
                isPlaying = snapshot.flags.isPlaying,
                isPlayingOrWillChangePlaymode = snapshot.flags.isPlayingOrWillChangePlaymode,
                activeScene = snapshot.activeScene,
                loadedScenes = snapshot.loadedScenes
            };

            var report = new JObject
            {
                ["schemaVersion"] = "1.0",
                ["payloadVersion"] = EditorBasicsContracts.EditorStateContractVersion,
                ["generatedAt"] = EditorBasicsContracts.CreateGeneratedAtUtc(),
                ["editorState"] = JToken.FromObject(snapshot)
            };

            return new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = "Editor state collected.",
                MetricsObjectJson = EditorBasicsJson.Serialize(metrics),
                ReportPath = EditorBasicsReportWriter.WriteReport(context.ProjectRoot, context.TempRoot, context.CommandId, "get_editor_state", report)
            };
        }
    }
}
