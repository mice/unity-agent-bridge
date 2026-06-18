using System;
using UnityMcp.Plugin;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class HierarchyTargetResolver
    {
        public static bool TryResolve(string locator, out HierarchyTargetResolution resolution, out UnityMcpToolResult failure)
        {
            locator = NormalizeLocator(locator);
            resolution = null;
            failure = null;

            if (string.Equals(locator, "currentScene", StringComparison.Ordinal))
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_SCENE_NOT_LOADED", "Active scene is not open or loaded.");
                    return false;
                }

                resolution = HierarchyTargetResolution.ForScene(locator, activeScene);
                return true;
            }

            if (string.Equals(locator, "selection:active", StringComparison.Ordinal))
            {
                var activeObject = Selection.activeObject;
                if (activeObject is GameObject activeGameObject)
                {
                    resolution = HierarchyTargetResolution.ForGameObject(locator, activeGameObject, "selection");
                    return true;
                }

                if (activeObject is Component component)
                {
                    resolution = HierarchyTargetResolution.ForGameObject(locator, component.gameObject, "selection");
                    return true;
                }

                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_SELECTION_NOT_GAMEOBJECT", "Active selection does not resolve to a GameObject.");
                return false;
            }

            if (locator.StartsWith("instance:", StringComparison.Ordinal))
            {
                if (!int.TryParse(locator.Substring("instance:".Length), out var instanceId))
                {
                    failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "instance locator must contain an integer instance id.");
                    return false;
                }

                var instanceObject = EditorUtility.InstanceIDToObject(instanceId);
                if (instanceObject is GameObject instanceGameObject)
                {
                    resolution = HierarchyTargetResolution.ForGameObject(locator, instanceGameObject, "instance");
                    return true;
                }

                if (instanceObject is Component instanceComponent)
                {
                    resolution = HierarchyTargetResolution.ForGameObject(locator, instanceComponent.gameObject, "instance");
                    return true;
                }

                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"instance:{instanceId} does not resolve to a loaded GameObject.");
                return false;
            }

            if (locator.StartsWith("currentScene#", StringComparison.Ordinal))
            {
                if (!TryResolveHierarchyInScene(EditorSceneManager.GetActiveScene(), locator.Substring("currentScene#".Length), false, out var sceneObject, out failure))
                {
                    return false;
                }

                resolution = HierarchyTargetResolution.ForGameObject(locator, sceneObject, "scene_subtree");
                return true;
            }

            var hashIndex = locator.IndexOf('#');
            var assetPath = hashIndex >= 0 ? locator.Substring(0, hashIndex) : locator;
            var hierarchyPath = hashIndex >= 0 ? locator.Substring(hashIndex + 1) : null;
            assetPath = AssetQueryPathValidator.NormalizeAssetPath(assetPath);

            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Unsupported locator form.");
                return false;
            }

            if (assetPath.EndsWith(".unity", StringComparison.Ordinal))
            {
                var scene = FindLoadedScene(assetPath);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_SCENE_NOT_LOADED", $"Scene '{assetPath}' is not currently open or loaded.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(hierarchyPath))
                {
                    resolution = HierarchyTargetResolution.ForScene(locator, scene);
                    return true;
                }

                if (!TryResolveHierarchyInScene(scene, hierarchyPath, true, out var sceneObject, out failure))
                {
                    return false;
                }

                resolution = HierarchyTargetResolution.ForGameObject(locator, sceneObject, "scene_subtree");
                return true;
            }

            if (assetPath.EndsWith(".prefab", StringComparison.Ordinal))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (root == null)
                {
                    failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"Prefab asset '{assetPath}' could not be loaded.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(hierarchyPath))
                {
                    resolution = HierarchyTargetResolution.ForPrefab(locator, root, assetPath);
                    return true;
                }

                if (!TryResolveHierarchy(root, hierarchyPath, out var prefabObject, out failure))
                {
                    return false;
                }

                resolution = HierarchyTargetResolution.ForPrefabObject(locator, prefabObject, assetPath);
                return true;
            }

            failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Unsupported locator form.");
            return false;
        }

        private static string NormalizeLocator(string locator)
        {
            return string.IsNullOrWhiteSpace(locator) ? "currentScene" : locator.Trim();
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

            return default(Scene);
        }

        private static bool TryResolveHierarchyInScene(Scene scene, string hierarchyPath, bool requireLoadedScenePath, out GameObject gameObject, out UnityMcpToolResult failure)
        {
            gameObject = null;
            failure = null;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_SCENE_NOT_LOADED", "Scene is not open or loaded.");
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

            failure = UnityQueriesResult.InvalidArgs(
                requireLoadedScenePath ? "AGENTBRIDGE_LOCATOR_UNSUPPORTED" : "AGENTBRIDGE_LOCATOR_UNSUPPORTED",
                $"Hierarchy path '{hierarchyPath}' could not be resolved.");
            return false;
        }

        private static bool TryResolveHierarchy(GameObject root, string hierarchyPath, out GameObject gameObject, out UnityMcpToolResult failure)
        {
            gameObject = null;
            failure = null;
            if (!TryValidateHierarchyPath(hierarchyPath, out var segments, out failure))
            {
                return false;
            }

            if (segments.Length == 0)
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy path is required.");
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

            failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", $"Hierarchy path '{hierarchyPath}' could not be resolved.");
            return false;
        }

        private static bool TryValidateHierarchyPath(string hierarchyPath, out string[] segments, out UnityMcpToolResult failure)
        {
            failure = null;
            segments = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(hierarchyPath) ||
                hierarchyPath.StartsWith("/", StringComparison.Ordinal) ||
                hierarchyPath.EndsWith("/", StringComparison.Ordinal) ||
                hierarchyPath.Contains("#"))
            {
                failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy locator syntax is invalid.");
                return false;
            }

            segments = hierarchyPath.Split('/');
            foreach (var segment in segments)
            {
                if (string.IsNullOrEmpty(segment) || segment.IndexOf('/') >= 0 || segment.IndexOf('#') >= 0)
                {
                    failure = UnityQueriesResult.InvalidArgs("AGENTBRIDGE_LOCATOR_UNSUPPORTED", "Hierarchy locator syntax is invalid.");
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

    internal sealed class HierarchyTargetResolution
    {
        public string locator;
        public string targetKind;
        public Scene scene;
        public GameObject gameObject;
        public string prefabAssetPath;

        public static HierarchyTargetResolution ForScene(string locator, Scene scene)
        {
            return new HierarchyTargetResolution
            {
                locator = locator,
                targetKind = "scene_root",
                scene = scene
            };
        }

        public static HierarchyTargetResolution ForGameObject(string locator, GameObject gameObject, string targetKind)
        {
            return new HierarchyTargetResolution
            {
                locator = locator,
                targetKind = targetKind,
                gameObject = gameObject,
                scene = gameObject != null ? gameObject.scene : default(Scene)
            };
        }

        public static HierarchyTargetResolution ForPrefab(string locator, GameObject root, string prefabAssetPath)
        {
            return new HierarchyTargetResolution
            {
                locator = locator,
                targetKind = "prefab_root",
                gameObject = root,
                prefabAssetPath = prefabAssetPath
            };
        }

        public static HierarchyTargetResolution ForPrefabObject(string locator, GameObject gameObject, string prefabAssetPath)
        {
            return new HierarchyTargetResolution
            {
                locator = locator,
                targetKind = "prefab_subtree",
                gameObject = gameObject,
                prefabAssetPath = prefabAssetPath
            };
        }
    }
}