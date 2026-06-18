using System;
using UnityMcp.Plugin;
using System.Globalization;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class SceneQueryContract
    {
        public const string HierarchyContractVersion = "hierarchy.v2";

        public const int DefaultHierarchyMaxDepth = 4;
        public const int DefaultHierarchyLimit = 150;
        public const int MaxHierarchyLimit = 5000;
        public const int HierarchyComponentSummaryLimit = 8;

        public static string CreateGeneratedAtUtc()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
