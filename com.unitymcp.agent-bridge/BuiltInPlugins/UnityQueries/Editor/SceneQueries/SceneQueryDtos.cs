using System;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    [Serializable]
    public sealed class UnityGetHierarchyArgs
    {
        public string locator;
        public int maxDepth = SceneQueryContract.DefaultHierarchyMaxDepth;
        public int limit = SceneQueryContract.DefaultHierarchyLimit;
        public bool includeComponents;
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
