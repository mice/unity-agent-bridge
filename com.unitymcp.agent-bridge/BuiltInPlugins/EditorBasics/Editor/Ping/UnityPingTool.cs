using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    public sealed class UnityPingTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.ping",
            Title = "Unity Ping",
            Description = "Report Unity Editor reachability and basic runtime state.",
            DefaultTimeoutMs = 5000,
            AllowedRuntimeModes = UnityMcpToolRuntimeModes.EditAndPlay,
            SideEffect = UnityMcpToolSideEffect.ReadsProject,
            MayTriggerDomainReload = false
        };

        public UnityMcpSchemaDeclaration InputSchema { get; } = new UnityMcpSchemaDeclaration
        {
            Kind = UnityMcpSchemaKind.InlineJson,
            Value = "{\"type\":\"object\",\"properties\":{},\"$schema\":\"http://json-schema.org/draft-07/schema#\",\"additionalProperties\":false}"
        };

        public UnityMcpToolResult Execute(UnityMcpToolContext context, IUnityMcpCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!EditorBasicsJson.TryDeserializeArgs<UnityPingArgs>(context.RawArgsJson, out _, out var failure))
            {
                return failure;
            }

            var metrics = new UnityPingMetrics
            {
                unityVersion = Application.unityVersion,
                isCompiling = EditorApplication.isCompiling
            };

            return new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = "pong",
                MetricsObjectJson = EditorBasicsJson.Serialize(metrics),
                ReportPath = EditorBasicsReportWriter.WriteReport(context.ProjectRoot, context.TempRoot, context.CommandId, "ping", metrics)
            };
        }
    }
}
