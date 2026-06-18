using System;
using System.Globalization;

namespace UnityMcp.AgentBridge
{
    internal static class SceneQueryContract
    {
        public const string EditorStateContractVersion = "editor_state.v1";
        public const string OpenSceneContractVersion = "open_scene.v1";
        public const string OpenSceneModeSingle = "single";
        public const string OpenSceneModeAdditive = "additive";

        public static string CreateGeneratedAtUtc()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
