using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    internal static class DiagnosticRunner
    {
        internal static readonly string[] SupportedDiagnosticTypes =
        {
            "fx_prefab",
            "scene",
            "prefab",
            "texture_import",
            "shader_variant",
            "material_instance",
            "vat_mesh",
            "bakeroot"
        };

        public static ToolResult Run(UnityDiagnosticArgs args, AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return BuildFailureResult(
                    context,
                    ToolResultStatus.Exception,
                    "AGENTBRIDGE_PROJECT_ROOT_UNAVAILABLE",
                    "Project root could not be resolved.",
                    BuildReport(args, null, supported: false, "project_root_unavailable", null));
            }

            string normalizedPath;
            try
            {
                normalizedPath = PathSafety.Normalize(projectRoot, args.targetPath);
            }
            catch (ArgumentException exception)
            {
                return BuildFailureResult(
                    context,
                    ToolResultStatus.InvalidArgs,
                    "AGENTBRIDGE_PATH_UNSAFE",
                    exception.Message,
                    BuildReport(args, null, supported: false, "path_rejected", null));
            }

            var kind = args.diagnosticType ?? string.Empty;
            if (!SupportedDiagnosticTypes.Contains(kind, StringComparer.Ordinal))
            {
                return BuildFailureResult(
                    context,
                    ToolResultStatus.InvalidArgs,
                    "AGENTBRIDGE_DIAGNOSTIC_TYPE_INVALID",
                    $"Unsupported diagnosticType '{kind}'.",
                    BuildReport(args, normalizedPath, supported: false, "diagnostic_type_invalid", null));
            }

            if (string.Equals(kind, "scene", StringComparison.Ordinal))
            {
                return RunSceneDiagnostic(normalizedPath, context, cancellation);
            }

            return BuildFailureResult(
                context,
                ToolResultStatus.Unsupported,
                "AGENTBRIDGE_DIAGNOSTIC_NOT_INTEGRATED",
                $"Diagnostic type '{kind}' is not integrated in this project.",
                BuildReport(args, normalizedPath, supported: false, "diagnostic_not_integrated", "Future integration point: Editor/Rules and project-specific Diagnostic subsystem."));
        }

        private static ToolResult RunSceneDiagnostic(string normalizedPath, AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(normalizedPath);
            if (sceneAsset == null)
            {
                return BuildFailureResult(
                    context,
                    ToolResultStatus.InvalidArgs,
                    "AGENTBRIDGE_DIAGNOSTIC_TARGET_NOT_FOUND",
                    $"Scene asset '{normalizedPath}' could not be loaded.",
                    BuildReport(new UnityDiagnosticArgs { diagnosticType = "scene", targetPath = normalizedPath }, normalizedPath, supported: true, "scene_target_not_found", null));
            }

            var absolutePath = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
            var fileInfo = new FileInfo(absolutePath);
            var dependencies = AssetDatabase.GetDependencies(normalizedPath, true)
                .Where(path => !string.Equals(path, normalizedPath, StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var metrics = new UnityDiagnosticMetrics
            {
                diagnosticType = "scene",
                targetPath = normalizedPath,
                supported = true,
                assetGuid = AssetDatabase.AssetPathToGUID(normalizedPath),
                assetType = nameof(SceneAsset),
                exists = fileInfo.Exists,
                fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0L,
                dependencyCount = dependencies.Length,
                dependencySample = dependencies.Take(5).ToArray(),
                integrationPoint = "Built-in scene metadata diagnostic."
            };

            var result = new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = $"Scene diagnostic collected for '{normalizedPath}'.",
                metricsObjectJson = JsonUtil.SerializeObject(metrics)
            };
            result.reportPath = AgentBridgeReportWriter.WriteReport(context.Settings, context.Command.commandId, "diagnostic", metrics);
            return result;
        }

        private static ToolResult BuildFailureResult(
            AgentToolContext context,
            string status,
            string code,
            string message,
            UnityDiagnosticMetrics report)
        {
            ToolResult result;
            if (string.Equals(status, ToolResultStatus.Unsupported, StringComparison.Ordinal))
            {
                result = ToolResult.Unsupported(code, message);
            }
            else if (string.Equals(status, ToolResultStatus.InvalidArgs, StringComparison.Ordinal))
            {
                result = ToolResult.InvalidArgs(code, message);
            }
            else
            {
                result = new ToolResult
                {
                    success = false,
                    status = status,
                    summary = message
                };
                result.errors.Add(new ToolError
                {
                    code = code,
                    message = message
                });
            }

            result.metricsObjectJson = JsonUtil.SerializeObject(report);
            result.reportPath = AgentBridgeReportWriter.WriteReport(context.Settings, context.Command.commandId, "diagnostic", report);
            return result;
        }

        private static UnityDiagnosticMetrics BuildReport(UnityDiagnosticArgs args, string normalizedPath, bool supported, string integrationPoint, string note)
        {
            return new UnityDiagnosticMetrics
            {
                diagnosticType = args?.diagnosticType ?? string.Empty,
                targetPath = normalizedPath ?? args?.targetPath ?? string.Empty,
                supported = supported,
                assetGuid = string.Empty,
                assetType = string.Empty,
                exists = false,
                fileSizeBytes = 0L,
                dependencyCount = 0,
                dependencySample = Array.Empty<string>(),
                integrationPoint = integrationPoint,
                note = note ?? string.Empty
            };
        }
    }

    [Serializable]
    public sealed class UnityDiagnosticMetrics
    {
        public string diagnosticType;
        public string targetPath;
        public bool supported;
        public string assetGuid;
        public string assetType;
        public bool exists;
        public long fileSizeBytes;
        public int dependencyCount;
        public string[] dependencySample;
        public string integrationPoint;
        public string note;
    }
}
