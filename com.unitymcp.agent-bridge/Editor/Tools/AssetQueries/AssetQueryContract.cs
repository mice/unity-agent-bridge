using System;
using System.Globalization;

namespace UnityMcp.AgentBridge
{
    internal static class AssetQueryContract
    {
        public const string AssetSearchContractVersion = "assetdatabase_search.v1";
        public const string SelectionInfoContractVersion = "selection_info.v1";
        public const string GameObjectComponentInfoContractVersion = "gameobject_component_info.v1";

        public const int DefaultAssetSearchLimit = 20;
        public const int MaxAssetSearchLimit = 200;
        public const int DetailSampleLimit = 20;

        public const string DefaultPropertyMode = "debug";
        public const string SerializedPropertyMode = "serialized";
        public const int DefaultPropertyLimit = 200;
        public const int MaxPropertyLimit = 1000;
        public const int DefaultArrayElementLimit = 20;
        public const int MaxArrayElementLimit = 200;
        public const int DefaultStringMaxLength = 300;
        public const int MinStringMaxLength = 16;
        public const int MaxStringMaxLength = 4000;

        public static string CreateGeneratedAtUtc()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
