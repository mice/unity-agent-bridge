using System;

namespace UnityMcp.BuiltInPlugins.MonoBehaviourSemantics
{
    [Serializable]
    public sealed class FindScriptGuidUsagesArgs
    {
        public string scriptGuid;
        public string scriptPath;
        public string typeName;
        public string provider;
        public string[] searchFolders;
        public string[] assetTypes;
        public int? limit;
    }

    [Serializable]
    internal sealed class MonoBehaviourScriptIdentity
    {
        public string guid;
        public string path;
        public string typeName;
        public string assemblyName;
        public string source;
    }

    [Serializable]
    internal sealed class ReferenceProviderCapabilities
    {
        public bool textMatches;
        public bool gameObjectPath;
        public bool componentIndex;
        public bool serializedFields;
        public bool dependencyCache;
    }

    [Serializable]
    internal sealed class ReferenceProviderMetadata
    {
        public string id;
        public string selection;
        public string confidence;
        public ReferenceProviderCapabilities capabilities;
    }

    [Serializable]
    internal sealed class ScriptGuidUsageMatch
    {
        public string assetPath;
        public string assetType;
        public int line;
        public int column;
        public string text;
    }

    [Serializable]
    internal sealed class ToolResultDetailsMetadata
    {
        public bool available;
        public string reportPath;
        public bool recommendedRead;
        public string[] recommendedPointers = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class ToolFollowUpOption
    {
        public string tool;
        public string reason;
        public object args;
    }

    [Serializable]
    internal sealed class ToolFollowUpMetadata
    {
        public bool recommended;
        public ToolFollowUpOption[] options = Array.Empty<ToolFollowUpOption>();
    }

    [Serializable]
    internal sealed class FindScriptGuidUsagesMetrics
    {
        public MonoBehaviourScriptIdentity script;
        public ReferenceProviderMetadata provider;
        public int usageCount;
        public int matchedAssetCount;
        public int returnedCount;
        public int limit;
        public bool truncated;
        public string semanticValidation;
        public ScriptGuidUsageMatch[] matches = Array.Empty<ScriptGuidUsageMatch>();
        public ToolResultDetailsMetadata details;
        public ToolFollowUpMetadata followUp;
    }

    internal sealed class MonoBehaviourReferenceQuery
    {
        public MonoBehaviourScriptIdentity Script;
        public string[] SearchFolders = Array.Empty<string>();
        public string[] AssetTypes = Array.Empty<string>();
        public int Limit;
    }

    internal sealed class MonoBehaviourReferenceResult
    {
        public ReferenceProviderMetadata Provider;
        public int UsageCount;
        public int MatchedAssetCount;
        public bool Truncated;
        public ScriptGuidUsageMatch[] Matches = Array.Empty<ScriptGuidUsageMatch>();
    }
}
