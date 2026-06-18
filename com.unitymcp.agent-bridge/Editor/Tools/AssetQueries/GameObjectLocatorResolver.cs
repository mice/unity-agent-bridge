using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.AgentBridge
{
    internal static class GameObjectLocatorResolver
    {
        public static bool TryResolve(string locator, out GameObject gameObject, out ToolResult failure)
        {
            locator = string.IsNullOrWhiteSpace(locator) ? "selection:active" : locator.Trim();
            gameObject = null;
            failure = null;

            if (string.Equals(locator, "selection:active", StringComparison.Ordinal))
            {
                var activeObject = Selection.activeObject;
                if (activeObject is GameObject selectedGameObject)
                {
                    gameObject = selectedGameObject;
                    return true;
                }

                if (activeObject is Component component)
                {
                    gameObject = component.gameObject;
                    return true;
                }

                failure = ToolResult.InvalidArgs("AGENTBRIDGE_SELECTION_NOT_GAMEOBJECT", "Active selection does not resolve to a GameObject.");
                return false;
            }

            if (locator.StartsWith("instance:", StringComparison.Ordinal))
            {
                if (!int.TryParse(locator.Substring("instance:".Length), out var instanceId))
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "instance locator must contain an integer instance id.");
                    return false;
                }

                var instanceObject = EditorUtility.InstanceIDToObject(instanceId);
                if (instanceObject is GameObject instanceGameObject)
                {
                    gameObject = instanceGameObject;
                    return true;
                }

                if (instanceObject is Component instanceComponent)
                {
                    gameObject = instanceComponent.gameObject;
                    return true;
                }

                failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"instance:{instanceId} does not resolve to a loaded GameObject.");
                return false;
            }

            if (locator.StartsWith("currentScene#", StringComparison.Ordinal))
            {
                return TryResolveHierarchyInScene(EditorSceneManager.GetActiveScene(), locator.Substring("currentScene#".Length), false, out gameObject, out failure);
            }

            var hashIndex = locator.IndexOf('#');
            var assetPath = hashIndex >= 0 ? locator.Substring(0, hashIndex) : locator;
            var hierarchyPath = hashIndex >= 0 ? locator.Substring(hashIndex + 1) : null;
            assetPath = AssetQueryPathValidator.NormalizeAssetPath(assetPath);

            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Unsupported locator form.");
                return false;
            }

            if (assetPath.EndsWith(".prefab", StringComparison.Ordinal))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (root == null)
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"Prefab asset '{assetPath}' could not be loaded.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(hierarchyPath))
                {
                    gameObject = root;
                    return true;
                }

                return TryResolveHierarchy(root, hierarchyPath, out gameObject, out failure);
            }

            if (assetPath.EndsWith(".unity", StringComparison.Ordinal))
            {
                var scene = FindLoadedScene(assetPath);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_SCENE_NOT_LOADED", $"Scene '{assetPath}' is not currently open or loaded.");
                    return false;
                }

                return TryResolveHierarchyInScene(scene, hierarchyPath, true, out gameObject, out failure);
            }

            failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Unsupported locator form.");
            return false;
        }

        private static Scene FindLoadedScene(string assetPath)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (string.Equals(scene.path.Replace('\\', '/'), assetPath, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default;
        }

        private static bool TryResolveHierarchyInScene(Scene scene, string hierarchyPath, bool requireLoadedScenePath, out GameObject gameObject, out ToolResult failure)
        {
            gameObject = null;
            failure = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_SCENE_NOT_LOADED", "Scene is not open or loaded.");
                return false;
            }

            if (!TryValidateHierarchyPath(hierarchyPath, out var segments, out failure))
            {
                return false;
            }

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (!string.Equals(root.name, segments[0], StringComparison.Ordinal))
                {
                    continue;
                }

                if (segments.Length == 1)
                {
                    gameObject = root;
                    return true;
                }

                if (TryResolveChild(root.transform, segments, 1, out gameObject))
                {
                    return true;
                }
            }

            failure = ToolResult.InvalidArgs(
                requireLoadedScenePath ? "AGENTBRIDGE_LOCATOR_UNSUPPORTED" : "AGENTBRIDGE_LOCATOR_UNSUPPORTED",
                $"Hierarchy path '{hierarchyPath}' could not be resolved.");
            return false;
        }

        private static bool TryResolveHierarchy(GameObject root, string hierarchyPath, out GameObject gameObject, out ToolResult failure)
        {
            gameObject = null;
            failure = null;
            if (!TryValidateHierarchyPath(hierarchyPath, out var segments, out failure))
            {
                return false;
            }

            if (segments.Length == 0)
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy path is required.");
                return false;
            }

            if (segments.Length == 1 && string.Equals(root.name, segments[0], StringComparison.Ordinal))
            {
                gameObject = root;
                return true;
            }

            if (string.Equals(root.name, segments[0], StringComparison.Ordinal))
            {
                if (TryResolveChild(root.transform, segments, 1, out gameObject))
                {
                    return true;
                }
            }
            else if (TryResolveChild(root.transform, segments, 0, out gameObject))
            {
                return true;
            }

            failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"Hierarchy path '{hierarchyPath}' could not be resolved.");
            return false;
        }

        private static bool TryValidateHierarchyPath(string hierarchyPath, out string[] segments, out ToolResult failure)
        {
            failure = null;
            segments = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(hierarchyPath) ||
                hierarchyPath.StartsWith("/", StringComparison.Ordinal) ||
                hierarchyPath.EndsWith("/", StringComparison.Ordinal) ||
                hierarchyPath.Contains("#"))
            {
                failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy locator syntax is invalid.");
                return false;
            }

            segments = hierarchyPath.Split('/');
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment.IndexOf('/') >= 0 || segment.IndexOf('#') >= 0)
                {
                    failure = ToolResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy locator syntax is invalid.");
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveChild(Transform current, IReadOnlyList<string> segments, int segmentIndex, out GameObject gameObject)
        {
            if (segmentIndex >= segments.Count)
            {
                gameObject = current.gameObject;
                return true;
            }

            for (var index = 0; index < current.childCount; index++)
            {
                var child = current.GetChild(index);
                if (!string.Equals(child.name, segments[segmentIndex], StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryResolveChild(child, segments, segmentIndex + 1, out gameObject))
                {
                    return true;
                }
            }

            gameObject = null;
            return false;
        }
    }
}
