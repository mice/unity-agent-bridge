using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.ProjectInfo
{
    [UnityMcpPlugin("com.unitymcp.builtin.project-info", "1.0.0")]
    public sealed class ProjectInfoProvider : IUnityMcpToolProvider
    {
        public IEnumerable<IUnityMcpTool> GetTools(UnityMcpPluginContext context)
        {
            return new IUnityMcpTool[]
            {
                new GetProjectInfoTool()
            };
        }
    }

    public sealed class GetProjectInfoTool : IUnityMcpTool
    {
        public UnityMcpToolDescriptor Descriptor { get; } = new UnityMcpToolDescriptor
        {
            Name = "unity.project.get_info",
            Title = "Unity Project Info",
            Description = "Report Unity project, scene, and editor state.",
            DefaultTimeoutMs = 10000,
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
            if (!TryDeserializeArgs<GetProjectInfoArgs>(context.RawArgsJson, out _, out var failure))
            {
                return failure;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? context.ProjectRoot ?? string.Empty;
            var payload = new GetProjectInfoPayload
            {
                unityVersion = Application.unityVersion,
                projectPath = projectRoot.Replace('\\', '/'),
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlaying,
                activeScene = ResolveActiveScenePath()
            };

            return new UnityMcpToolResult
            {
                Success = true,
                Status = UnityMcpToolStatus.Success,
                Summary = "Project info collected.",
                MetricsObjectJson = JsonConvert.SerializeObject(payload, Formatting.None),
                ReportPath = WriteReport(context, payload)
            };
        }

        private static string ResolveActiveScenePath()
        {
            var activeScenePath = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrWhiteSpace(activeScenePath))
            {
                return activeScenePath;
            }

            var buildScenePath = EditorBuildSettings.scenes.FirstOrDefault(scene => scene.enabled)?.path;
            if (!string.IsNullOrWhiteSpace(buildScenePath))
            {
                return buildScenePath;
            }

            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            if (sceneGuids == null || sceneGuids.Length == 0)
            {
                return string.Empty;
            }

            return AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
        }

        private static string WriteReport(UnityMcpToolContext context, GetProjectInfoPayload payload)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? context.ProjectRoot ?? string.Empty;
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(context.CommandId))
            {
                return null;
            }

            var tempRoot = string.IsNullOrWhiteSpace(context.TempRoot) ? "Temp/AgentBridge" : context.TempRoot;
            var reportsDirectory = Path.GetFullPath(Path.Combine(projectRoot, tempRoot.Replace('/', Path.DirectorySeparatorChar), "reports", "project_get_info"));
            Directory.CreateDirectory(reportsDirectory);
            var fileName = SanitizeFileName(context.CommandId) + ".json";
            var absolutePath = Path.Combine(reportsDirectory, fileName);
            File.WriteAllText(absolutePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
            return Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
            return new string(chars);
        }

        private static bool TryDeserializeArgs<TArgs>(string rawArgsJson, out TArgs args, out UnityMcpToolResult failure)
            where TArgs : class, new()
        {
            failure = null;
            args = null;

            if (!IsJsonObject(rawArgsJson))
            {
                failure = CreateInvalidArgsResult("AGENTBRIDGE_ARGS_OBJECT_REQUIRED", "args must be a JSON object.");
                return false;
            }

            try
            {
                args = JsonConvert.DeserializeObject<TArgs>(rawArgsJson);
                if (args == null)
                {
                    args = new TArgs();
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = CreateInvalidArgsResult("AGENTBRIDGE_ARGS_PARSE_FAILED", exception.Message);
                return false;
            }
        }

        private static UnityMcpToolResult CreateInvalidArgsResult(string code, string message)
        {
            return new UnityMcpToolResult
            {
                Success = false,
                Status = UnityMcpToolStatus.InvalidArgs,
                Summary = message,
                Errors = new List<UnityMcpToolError>
                {
                    new UnityMcpToolError
                    {
                        Code = code,
                        Message = message
                    }
                }
            };
        }

        private static bool IsJsonObject(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            try
            {
                return JToken.Parse(rawJson) is JObject;
            }
            catch
            {
                return false;
            }
        }
    }

    [Serializable]
    public sealed class GetProjectInfoArgs
    {
    }

    [Serializable]
    public sealed class GetProjectInfoPayload
    {
        public string unityVersion;
        public string projectPath;
        public string activeBuildTarget;
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public string activeScene;
    }
}
