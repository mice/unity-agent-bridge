using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcp.AgentBridge
{
    internal static class GameObjectLocatorFormatter
    {
        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var segments = new Stack<string>();
            var current = gameObject.transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments.ToArray());
        }

        public static string GetLocator(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var assetPath = AssetDatabase.GetAssetPath(gameObject);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                assetPath = assetPath.Replace('\\', '/');
                if (assetPath.EndsWith(".prefab", System.StringComparison.Ordinal))
                {
                    return gameObject.transform.parent == null ? assetPath : assetPath + "#" + GetRelativeHierarchyFromRoot(gameObject);
                }

                if (assetPath.EndsWith(".unity", System.StringComparison.Ordinal))
                {
                    return assetPath + "#" + GetHierarchyPath(gameObject);
                }
            }

            if (gameObject.scene.IsValid() && gameObject.scene == EditorSceneManager.GetActiveScene())
            {
                return "currentScene#" + GetHierarchyPath(gameObject);
            }

            if (gameObject.scene.IsValid() &&
                gameObject.scene.isLoaded &&
                !string.IsNullOrWhiteSpace(gameObject.scene.path))
            {
                return gameObject.scene.path.Replace('\\', '/') + "#" + GetHierarchyPath(gameObject);
            }

            return "instance:" + gameObject.GetInstanceID();
        }

        public static string GetRelativeHierarchyFromRoot(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var hierarchy = GetHierarchyPath(gameObject);
            var rootName = gameObject.transform.root.name;
            if (string.Equals(hierarchy, rootName, System.StringComparison.Ordinal))
            {
                return rootName;
            }

            return hierarchy.Substring(rootName.Length + 1);
        }
    }
}
