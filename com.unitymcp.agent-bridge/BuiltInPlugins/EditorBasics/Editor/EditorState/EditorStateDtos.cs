using System;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    [Serializable]
    public sealed class UnityGetEditorStateArgs
    {
    }

    [Serializable]
    public sealed class EditorStateSnapshot
    {
        public string runtimeMode;
        public string activityState;
        public EditorStateFlags flags;
        public SceneMutationState sceneMutation;
        public SceneSummaryRecord activeScene;
        public SceneSummaryRecord[] loadedScenes;
    }

    [Serializable]
    public sealed class EditorStateFlags
    {
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public bool isPlayingOrWillChangePlaymode;
    }

    [Serializable]
    public sealed class SceneMutationState
    {
        public bool canOpenScene;
        public string[] blockers;
    }

    [Serializable]
    public sealed class SceneSummaryRecord
    {
        public string path;
        public string name;
        public bool isLoaded;
        public bool isDirty;
        public bool? isActive;
        public int? rootCount;
        public int buildIndex;
    }

    [Serializable]
    public sealed class EditorStateMetrics
    {
        public string contractVersion = EditorBasicsContracts.EditorStateContractVersion;
        public EditorStateSnapshot editorState;
        public string runtimeMode;
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public bool isPlayingOrWillChangePlaymode;
        public SceneSummaryRecord activeScene;
        public SceneSummaryRecord[] loadedScenes;
    }
}
