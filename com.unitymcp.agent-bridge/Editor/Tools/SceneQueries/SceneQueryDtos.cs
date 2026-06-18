using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityGetEditorStateArgs
    {
    }

    [Serializable]
    public sealed class UnityOpenSceneArgs
    {
        public string scenePath;
        public string mode = SceneQueryContract.OpenSceneModeSingle;
        public bool setActive = true;
        public bool saveModifiedScenes;
    }

    [Serializable]
    public sealed class UnityGetHierarchyArgs
    {
        public string locator;
        public int maxDepth = SceneQueryContract.DefaultHierarchyMaxDepth;
        public int limit = SceneQueryContract.DefaultHierarchyLimit;
        public bool includeComponents;
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
        public string contractVersion = SceneQueryContract.EditorStateContractVersion;
        public EditorStateSnapshot editorState;
        public string runtimeMode;
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlaying;
        public bool isPlayingOrWillChangePlaymode;
        public SceneSummaryRecord activeScene;
        public SceneSummaryRecord[] loadedScenes;
    }

    [Serializable]
    public sealed class OpenSceneMetrics
    {
        public string contractVersion = SceneQueryContract.OpenSceneContractVersion;
        public string scenePath;
        public string mode;
        public bool setActive;
        public bool savedModifiedScenes;
        public bool alreadyLoaded;
        public SceneSummaryRecord openedScene;
        public SceneSummaryRecord activeScene;
        public SceneSummaryRecord[] loadedScenes;
        public EditorStateSnapshot editorState;
    }

    [Serializable]
    public sealed class HierarchyMetrics
    {
        public string contractVersion = SceneQueryContract.HierarchyContractVersion;
        public HierarchyTargetRecord target;
        public int rootCount;
        public int? nodeCount;
        public int returnedNodeCount;
        public bool truncated;
        public int limit;
        public int maxDepth;
        public int visitedCount;
        public HierarchyNodeRecord[] nodes;
        public ToolResultDetailsMetadata details;
        public ToolFollowUpMetadata followUp;
    }

    [Serializable]
    public sealed class HierarchyTargetRecord
    {
        public string locator;
        public string targetKind;
        public string scenePath;
        public string path;
        public string name;
        public int? instanceId;
    }

    [Serializable]
    public sealed class HierarchyNodeRecord
    {
        public int nodeIndex;
        public int? parentIndex;
        public string name;
        public string locator;
        public string path;
        public string scenePath;
        public int? instanceId;
        public int depth;
        public int siblingIndex;
        public bool activeSelf;
        public bool activeInHierarchy;
        public int childCount;
        public int componentCount;
        public bool isPrefabInstance;
        public string prefabAssetPath;
        public HierarchyComponentSummaryRecord[] components;
        public bool? componentsTruncated;
        public bool hasMissingScripts;
    }

    [Serializable]
    public sealed class HierarchyComponentSummaryRecord
    {
        public int index;
        public string type;
    }
}
