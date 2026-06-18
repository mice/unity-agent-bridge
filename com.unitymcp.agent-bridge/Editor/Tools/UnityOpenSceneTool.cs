using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMcp.AgentBridge
{
    [AgentTool("unity.open_scene")]
    public sealed class UnityOpenSceneTool : IAgentTool
    {
        public ToolDescriptor Descriptor { get; } = new ToolDescriptor
        {
            Name = "unity.open_scene",
            SchemaVersion = JsonUtil.CurrentSchemaVersion,
            Description = "Open an explicit Unity scene asset in Edit Mode with dirty-scene safeguards.",
            AllowedModes = ToolExecutionModes.Edit,
            SideEffect = ToolSideEffect.MutatesProject,
            MayTriggerDomainReload = false,
            ArgsSchemaPath = "Documentation~/schemas/unity.open_scene.args.schema.json"
        };

        public ToolResult Execute(AgentToolContext context, IAgentCancellation cancellation)
        {
            cancellation?.ThrowIfCancellationRequested();
            if (!SceneQueryJson.TryDeserializeArgs<UnityOpenSceneArgs>(context.RawArgsJson, out var args, out var failure))
            {
                return failure;
            }

            args ??= new UnityOpenSceneArgs();
            args.mode = string.IsNullOrWhiteSpace(args.mode) ? SceneQueryContract.OpenSceneModeSingle : args.mode;

            if (string.IsNullOrWhiteSpace(args.scenePath))
            {
                return FinalizeFailure(
                    context,
                    args,
                    ToolResult.InvalidArgs("AGENTBRIDGE_SCENE_PATH_REQUIRED", "scenePath is required."),
                    null);
            }

            var scenePath = AssetQueryPathValidator.NormalizeAssetPath(args.scenePath);
            if (!scenePath.StartsWith("Assets/", StringComparison.Ordinal) || !scenePath.EndsWith(".unity", StringComparison.Ordinal))
            {
                return FinalizeFailure(
                    context,
                    args,
                    ToolResult.InvalidArgs("AGENTBRIDGE_SCENE_PATH_INVALID", "scenePath must resolve to an Assets/**/*.unity scene asset."),
                    new
                    {
                        scenePath
                    });
            }

            if (string.Equals(args.mode, SceneQueryContract.OpenSceneModeSingle, StringComparison.Ordinal))
            {
                args.mode = SceneQueryContract.OpenSceneModeSingle;
            }
            else if (string.Equals(args.mode, SceneQueryContract.OpenSceneModeAdditive, StringComparison.Ordinal))
            {
                args.mode = SceneQueryContract.OpenSceneModeAdditive;
            }
            else
            {
                return FinalizeFailure(
                    context,
                    args,
                    ToolResult.InvalidArgs("AGENTBRIDGE_OPEN_SCENE_MODE_INVALID", "mode must be one of: single, additive."),
                    new
                    {
                        scenePath
                    });
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return FinalizeFailure(
                    context,
                    args,
                    ToolResult.InvalidArgs("AGENTBRIDGE_SCENE_PATH_INVALID", $"Scene asset '{scenePath}' could not be resolved."),
                    new
                    {
                        scenePath
                    });
            }

            var initialSnapshot = EditorStateSnapshotBuilder.Build();
            if (HasNonDirtySceneOpenBlockers(initialSnapshot))
            {
                return FinalizeFailure(
                    context,
                    args,
                    BuildBlockedResult(initialSnapshot, "Scene open is blocked by current editor state."),
                    new
                    {
                        scenePath,
                        editorState = initialSnapshot
                    });
            }

            if (!args.saveModifiedScenes)
            {
                var dirtyScenes = initialSnapshot.loadedScenes
                    .Where(scene => scene.isDirty)
                    .Select(scene => string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path)
                    .ToArray();
                if (dirtyScenes.Length > 0)
                {
                    return FinalizeFailure(
                        context,
                        args,
                        BuildDirtyBlockedResult(initialSnapshot, dirtyScenes),
                        new
                        {
                            scenePath,
                            editorState = initialSnapshot,
                            dirtyScenes
                        });
                }
            }
            else
            {
                foreach (var scene in initialSnapshot.loadedScenes.Where(scene => scene.isDirty))
                {
                    if (string.IsNullOrWhiteSpace(scene.path))
                    {
                        return FinalizeFailure(
                            context,
                            args,
                            BuildBlockedResult(initialSnapshot, "Dirty untitled scene cannot be saved non-interactively.", "AGENTBRIDGE_UNTITLED_DIRTY_SCENE"),
                            new
                            {
                                scenePath,
                                editorState = initialSnapshot
                            });
                    }
                }

                var loadedDirtyScenes = Enumerable.Range(0, SceneManager.sceneCount)
                    .Select(SceneManager.GetSceneAt)
                    .Where(scene => scene.IsValid() && scene.isLoaded && scene.isDirty)
                    .ToArray();

                foreach (var dirtyScene in loadedDirtyScenes)
                {
                    if (!EditorSceneManager.SaveScene(dirtyScene, dirtyScene.path))
                    {
                        return FinalizeFailure(
                            context,
                            args,
                            BuildBlockedResult(initialSnapshot, $"Dirty scene '{dirtyScene.path}' could not be saved before open_scene continued.", "AGENTBRIDGE_DIRTY_SCENE_SAVE_FAILED"),
                            new
                            {
                                scenePath,
                                editorState = initialSnapshot,
                                dirtyScene = dirtyScene.path.Replace('\\', '/')
                            });
                    }
                }
            }

            var existingScene = FindLoadedScene(scenePath);
            var alreadyLoaded = existingScene.IsValid() && existingScene.isLoaded;
            Scene openedScene;
            if (alreadyLoaded && string.Equals(args.mode, SceneQueryContract.OpenSceneModeAdditive, StringComparison.Ordinal))
            {
                openedScene = existingScene;
            }
            else
            {
                openedScene = EditorSceneManager.OpenScene(
                    scenePath,
                    string.Equals(args.mode, SceneQueryContract.OpenSceneModeAdditive, StringComparison.Ordinal)
                        ? OpenSceneMode.Additive
                        : OpenSceneMode.Single);
            }

            if (args.setActive && openedScene.IsValid())
            {
                SceneManager.SetActiveScene(openedScene);
            }

            var finalSnapshot = EditorStateSnapshotBuilder.Build();
            var metrics = new OpenSceneMetrics
            {
                scenePath = scenePath,
                mode = args.mode,
                setActive = args.setActive,
                savedModifiedScenes = args.saveModifiedScenes && initialSnapshot.loadedScenes.Any(scene => scene.isDirty),
                alreadyLoaded = alreadyLoaded,
                openedScene = finalSnapshot.loadedScenes.FirstOrDefault(scene => string.Equals(scene.path, scenePath, StringComparison.Ordinal)),
                activeScene = finalSnapshot.activeScene,
                loadedScenes = finalSnapshot.loadedScenes,
                editorState = finalSnapshot
            };

            var result = new ToolResult
            {
                success = true,
                status = ToolResultStatus.Success,
                summary = alreadyLoaded && string.Equals(args.mode, SceneQueryContract.OpenSceneModeAdditive, StringComparison.Ordinal)
                    ? $"Scene '{scenePath}' was already loaded."
                    : $"Scene '{scenePath}' opened.",
                metricsObjectJson = SceneQueryJson.Serialize(metrics)
            };
            result.reportPath = AgentBridgeReportWriter.WriteReport(context.Settings, context.Command.commandId, "open_scene", SceneQueryReportBuilder.CreateOpenSceneReport(args, metrics));
            return result;
        }

        private static ToolResult FinalizeFailure(AgentToolContext context, UnityOpenSceneArgs args, ToolResult result, object metrics)
        {
            result.reportPath = AgentBridgeReportWriter.WriteReport(
                context.Settings,
                context.Command.commandId,
                "open_scene",
                SceneQueryReportBuilder.CreateOpenSceneFailureReport(args, result, metrics));
            return result;
        }

        private static Scene FindLoadedScene(string scenePath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (string.Equals(scene.path.Replace('\\', '/'), scenePath, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default;
        }

        private static ToolResult BuildDirtyBlockedResult(EditorStateSnapshot snapshot, string[] dirtyScenes)
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Blocked,
                summary = "Scene open is blocked by unsaved dirty scenes.",
                errors = new List<ToolError>
                {
                    new ToolError
                    {
                        code = "AGENTBRIDGE_DIRTY_SCENE_BLOCKED",
                        message = "Dirty loaded scenes must be saved explicitly before opening another scene: " + string.Join(", ", dirtyScenes)
                    }
                },
                metricsObjectJson = SceneQueryJson.Serialize(new
                {
                    editorState = snapshot,
                    dirtyScenes
                })
            };
        }

        private static ToolResult BuildBlockedResult(EditorStateSnapshot snapshot, string message, string code = "AGENTBRIDGE_OPEN_SCENE_BLOCKED")
        {
            return new ToolResult
            {
                success = false,
                status = ToolResultStatus.Blocked,
                summary = message,
                errors = new List<ToolError>
                {
                    new ToolError
                    {
                        code = code,
                        message = message
                    }
                },
                metricsObjectJson = SceneQueryJson.Serialize(new
                {
                    editorState = snapshot
                })
            };
        }

        private static bool HasNonDirtySceneOpenBlockers(EditorStateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.sceneMutation == null || snapshot.sceneMutation.blockers == null)
            {
                return false;
            }

            foreach (var blocker in snapshot.sceneMutation.blockers)
            {
                if (string.Equals(blocker, "dirty_scene", StringComparison.Ordinal) ||
                    string.Equals(blocker, "untitled_dirty_scene", StringComparison.Ordinal))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
