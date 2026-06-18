using System;

namespace UnityMcp.AgentBridge
{
    [Serializable]
    public sealed class UnityAssetDatabaseSearchArgs
    {
        public string query;
        public string[] folders;
        public int offset;
        public int limit = AssetQueryContract.DefaultAssetSearchLimit;
        public bool includeDetails;
    }

    [Serializable]
    public sealed class UnityGetSelectionInfoArgs
    {
        public bool includeDetails;
    }

    [Serializable]
    public sealed class UnityGetGameObjectComponentInfoArgs
    {
        public string locator;
        public string componentName;
        public int componentIndex = -1;
        public string propertyMode = AssetQueryContract.DefaultPropertyMode;
        public int propertyLimit = AssetQueryContract.DefaultPropertyLimit;
        public int arrayElementLimit = AssetQueryContract.DefaultArrayElementLimit;
        public int stringMaxLength = AssetQueryContract.DefaultStringMaxLength;
    }

    [Serializable]
    public sealed class AssetDatabaseSearchMetrics
    {
        public string contractVersion = AssetQueryContract.AssetSearchContractVersion;
        public string query;
        public string[] folders;
        public int totalCount;
        public int returnedCount;
        public int offset;
        public int limit;
        public bool truncated;
        public int? nextOffset;
        public AssetSummaryRecord[] results;
    }

    [Serializable]
    public sealed class SelectionInfoMetrics
    {
        public string contractVersion = AssetQueryContract.SelectionInfoContractVersion;
        public int selectionCount;
        public SelectionSummaryRecord active;
        public SelectionKindCounts counts;
        public SelectionSummaryRecord[] items;
    }

    [Serializable]
    public sealed class SelectionKindCounts
    {
        public int assets;
        public int sceneObjects;
        public int components;
        public int other;
    }

    [Serializable]
    public sealed class GameObjectComponentInfoMetrics
    {
        public string contractVersion = AssetQueryContract.GameObjectComponentInfoContractVersion;
        public string mode;
        public GameObjectTargetRecord target;
        public ComponentQueryRecord componentQuery;
        public int componentCount;
        public int? matchedCount;
        public int? propertyCount;
        public int? returnedPropertyCount;
        public bool? truncated;
        public ComponentSummaryRecord[] components;
        public ToolResultDetailsMetadata details;
        public ToolFollowUpMetadata followUp;
    }

    [Serializable]
    public sealed class ComponentQueryRecord
    {
        public string componentName;
        public int? componentIndex;
        public string propertyMode;
    }

    [Serializable]
    public sealed class AssetSummaryRecord
    {
        public int index;
        public string guid;
        public string name;
        public string locator;
        public string path;
        public string kind = "asset";
        public string type;
        public string extension;
        public bool isFolder;
    }

    [Serializable]
    public sealed class SelectionSummaryRecord
    {
        public int index;
        public string kind;
        public string name;
        public string locator;
        public string type;
    }

    [Serializable]
    public sealed class GameObjectTargetRecord
    {
        public string name;
        public string locator;
        public string path;
        public string scenePath;
        public int? instanceId;
    }

    [Serializable]
    public sealed class ComponentSummaryRecord
    {
        public int index;
        public string name;
        public string type;
        public string scriptPath;
        public int? propertyCount;
        public int? returnedPropertyCount;
    }

    [Serializable]
    public sealed class SerializedPropertyRecord
    {
        public string path;
        public string propertyType;
        public string type;
        public bool isUnityObject;
        public bool isNull;
        public bool isContainer;
        public object value;
    }

    [Serializable]
    public sealed class UnityObjectValueRecord
    {
        public string name;
        public string path;
        public string guid;
        public int? instanceId;
        public bool isDestroyed;
    }
}
