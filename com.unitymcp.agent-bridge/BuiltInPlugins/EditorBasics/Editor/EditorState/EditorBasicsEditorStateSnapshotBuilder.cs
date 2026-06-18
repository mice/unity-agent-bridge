using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    internal static class EditorBasicsEditorStateSnapshotBuilder
    {
        public static EditorStateSnapshot Build()
        {
            var flags = new EditorStateFlags
            {
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlaying,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
            };

            var runtimeMode = GetRuntimeMode();
            var loadedScenes = GetLoadedScenes().ToArray();
            var blockers = GetSceneMutationBlockers(runtimeMode, loadedScenes).ToArray();

            return new EditorStateSnapshot
            {
                runtimeMode = runtimeMode,
                activityState = GetActivityState(flags),
                flags = flags,
                sceneMutation = new SceneMutationState
                {
                    canOpenScene = blockers.Length == 0,
                    blockers = blockers
                },
                activeScene = CreateSceneSummary(EditorSceneManager.GetActiveScene(), true),
                loadedScenes = loadedScenes
            };
        }

        private static IEnumerable<SceneSummaryRecord> GetLoadedScenes()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid())
                {
                    yield return CreateSceneSummary(scene, scene == activeScene);
                }
            }
        }

        private static SceneSummaryRecord CreateSceneSummary(Scene scene, bool isActive)
        {
            var path = string.IsNullOrEmpty(scene.path) ? string.Empty : scene.path.Replace('\\', '/');
            return new SceneSummaryRecord
            {
                path = string.IsNullOrWhiteSpace(path) ? null : path,
                name = scene.name,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty,
                isActive = scene.IsValid() ? (bool?)isActive : null,
                rootCount = scene.IsValid() && scene.isLoaded ? (int?)scene.rootCount : null,
                buildIndex = scene.buildIndex
            };
        }

        private static string GetRuntimeMode()
        {
            if (EditorApplication.isPlaying)
            {
                return "play_mode";
            }

            return EditorApplication.isPlayingOrWillChangePlaymode ? "entering_play_mode" : "edit_mode";
        }

        private static string GetActivityState(EditorStateFlags flags)
        {
            if (flags.isCompiling && flags.isUpdating)
            {
                return "compiling_and_updating";
            }

            if (flags.isCompiling)
            {
                return "compiling";
            }

            return flags.isUpdating ? "updating" : "idle";
        }

        private static IEnumerable<string> GetSceneMutationBlockers(string runtimeMode, IReadOnlyList<SceneSummaryRecord> loadedScenes)
        {
            switch (runtimeMode)
            {
                case "entering_play_mode":
                    yield return "entering_play_mode";
                    break;
                case "play_mode":
                    yield return "play_mode";
                    break;
                case "exiting_play_mode":
                    yield return "exiting_play_mode";
                    break;
            }

            if (EditorApplication.isCompiling)
            {
                yield return "compiling";
            }

            if (EditorApplication.isUpdating)
            {
                yield return "updating";
            }

            foreach (var loadedScene in loadedScenes)
            {
                if (!loadedScene.isDirty)
                {
                    continue;
                }

                yield return string.IsNullOrWhiteSpace(loadedScene.path) ? "untitled_dirty_scene" : "dirty_scene";
            }
        }
    }
}
