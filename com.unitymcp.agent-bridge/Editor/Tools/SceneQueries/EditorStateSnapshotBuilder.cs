using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMcp.AgentBridge
{
    internal static class EditorStateSnapshotBuilder
    {
        public static EditorStateSnapshot Build()
        {
            var flags = new EditorStateFlags();
            flags.isCompiling = EditorApplication.isCompiling;
            flags.isUpdating = EditorApplication.isUpdating;
            flags.isPlaying = EditorApplication.isPlaying;
            flags.isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode;

            var runtimeMode = GetRuntimeMode();
            var loadedScenes = GetLoadedScenes().ToArray();
            var blockers = GetSceneMutationBlockers(runtimeMode, loadedScenes).ToArray();

            var sceneMutation = new SceneMutationState();
            sceneMutation.canOpenScene = blockers.Length == 0;
            sceneMutation.blockers = blockers;

            var snapshot = new EditorStateSnapshot();
            snapshot.runtimeMode = runtimeMode;
            snapshot.activityState = GetActivityState(flags);
            snapshot.flags = flags;
            snapshot.sceneMutation = sceneMutation;
            snapshot.activeScene = CreateSceneSummary(EditorSceneManager.GetActiveScene(), true);
            snapshot.loadedScenes = loadedScenes;
            return snapshot;
        }

        private static IEnumerable<SceneSummaryRecord> GetLoadedScenes()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid())
                {
                    continue;
                }

                yield return CreateSceneSummary(scene, scene == activeScene);
            }
        }

        private static SceneSummaryRecord CreateSceneSummary(Scene scene, bool isActive)
        {
            var path = string.Empty;
            if (!string.IsNullOrEmpty(scene.path))
            {
                path = scene.path.Replace('\\', '/');
            }

            var summary = new SceneSummaryRecord();
            summary.path = string.IsNullOrWhiteSpace(path) ? null : path;
            summary.name = scene.name;
            summary.isLoaded = scene.isLoaded;
            summary.isDirty = scene.isDirty;
            summary.isActive = scene.IsValid() ? (bool?)isActive : null;
            summary.rootCount = scene.IsValid() && scene.isLoaded ? (int?)scene.rootCount : null;
            summary.buildIndex = scene.buildIndex;
            return summary;
        }

        private static string GetRuntimeMode()
        {
            switch (UnityEditorRuntimeModeProvider.Instance.GetCurrentMode())
            {
                case UnityRuntimeMode.EditMode:
                    return "edit_mode";
                case UnityRuntimeMode.EnteringPlayMode:
                    return "entering_play_mode";
                case UnityRuntimeMode.PlayMode:
                    return "play_mode";
                case UnityRuntimeMode.ExitingPlayMode:
                    return "exiting_play_mode";
                default:
                    return "edit_mode";
            }
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

            if (flags.isUpdating)
            {
                return "updating";
            }

            return "idle";
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

                if (string.IsNullOrWhiteSpace(loadedScene.path))
                {
                    yield return "untitled_dirty_scene";
                }
                else
                {
                    yield return "dirty_scene";
                }
            }
        }
    }
}
