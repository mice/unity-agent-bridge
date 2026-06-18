using System;

namespace UnityMcp.BuiltInPlugins.EditorBasics
{
    internal static class EditorBasicsContracts
    {
        public const string EditorStateContractVersion = "editor_state.v1";

        public static string CreateGeneratedAtUtc()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
