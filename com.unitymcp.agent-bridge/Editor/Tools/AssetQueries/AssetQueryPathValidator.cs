using System;
using System.IO;
using UnityEditor;

namespace UnityMcp.AgentBridge
{
    internal static class AssetQueryPathValidator
    {
        public static bool TryNormalizeFolders(string[] folders, out string[] normalizedFolders, out ToolResult failure)
        {
            normalizedFolders = Array.Empty<string>();
            failure = null;

            if (folders == null || folders.Length == 0)
            {
                return true;
            }

            normalizedFolders = new string[folders.Length];
            for (var index = 0; index < folders.Length; index++)
            {
                var rawFolder = folders[index]?.Trim().Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(rawFolder) ||
                    !rawFolder.StartsWith("Assets/", StringComparison.Ordinal) && !string.Equals(rawFolder, "Assets", StringComparison.Ordinal))
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_ASSET_FOLDER_INVALID", "folders must resolve to Unity project paths under Assets/.");
                    return false;
                }

                if (!AssetDatabase.IsValidFolder(rawFolder))
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_ASSET_FOLDER_INVALID", $"Folder '{rawFolder}' is not a valid Unity asset folder.");
                    return false;
                }

                normalizedFolders[index] = rawFolder;
            }

            return true;
        }

        public static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Replace('\\', '/');
        }

        public static string GetExtension(string assetPath)
        {
            return Path.GetExtension(assetPath ?? string.Empty) ?? string.Empty;
        }
    }
}
