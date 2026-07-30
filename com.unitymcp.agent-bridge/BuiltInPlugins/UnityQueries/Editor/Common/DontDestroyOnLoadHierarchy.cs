using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMcp.Plugin;

namespace UnityMcp.BuiltInPlugins.UnityQueries
{
    internal static class DontDestroyOnLoadHierarchy
    {
        public const string LocatorRoot = "dontDestroyOnLoad";
        public const string LocatorPrefix = LocatorRoot + "#";
        public const string SceneName = "DontDestroyOnLoad";
        public const string NotAvailableErrorCode = "AGENTBRIDGE_DONT_DESTROY_ON_LOAD_NOT_AVAILABLE";

#if UNITY_INCLUDE_TESTS
        internal static Func<GameObject[]> RootDiscoveryOverrideForTests;
#endif

        public static bool IsMember(GameObject gameObject)
        {
            if (!EditorApplication.isPlaying || gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                return false;
            }

            var scene = gameObject.scene;
            if (!scene.IsValid() ||
                !scene.isLoaded ||
                !string.Equals(scene.name, SceneName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!StageUtility.GetStageHandle(gameObject).Equals(StageUtility.GetMainStageHandle()))
            {
                return false;
            }

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).handle == scene.handle)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryGetRoots(out GameObject[] roots, out UnityMcpToolResult failure)
        {
            roots = Array.Empty<GameObject>();
            failure = null;
            if (!EditorApplication.isPlaying)
            {
                failure = CreateNotAvailableFailure();
                return false;
            }

#if UNITY_INCLUDE_TESTS
            if (RootDiscoveryOverrideForTests != null)
            {
                roots = RootDiscoveryOverrideForTests() ?? Array.Empty<GameObject>();
                return true;
            }
#endif

            roots = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(gameObject =>
                    gameObject != null &&
                    gameObject.transform.parent == null &&
                    IsMember(gameObject))
                .OrderBy(gameObject => gameObject.transform.GetSiblingIndex())
                .ThenBy(gameObject => gameObject.GetInstanceID())
                .ToArray();
            return true;
        }

        public static string GetScenePathOrNull(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.scene.IsValid() || IsMember(gameObject))
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(gameObject.scene.path)
                ? gameObject.scene.path.Replace('\\', '/')
                : null;
        }

        public static bool TryResolve(string hierarchyPath, out GameObject gameObject, out UnityMcpToolResult failure)
        {
            gameObject = null;
            if (!TryGetRoots(out var roots, out failure))
            {
                return false;
            }

            if (!TryValidateHierarchyPath(hierarchyPath, out var segments, out failure))
            {
                return false;
            }

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
                "AGENTBRIDGE_LOCATOR_UNSUPPORTED",
                $"Hierarchy path '{hierarchyPath}' could not be resolved in {LocatorRoot}.");
            return false;
        }

        private static UnityMcpToolResult CreateNotAvailableFailure()
        {
            return UnityQueriesResult.InvalidArgs(
                NotAvailableErrorCode,
                $"{LocatorRoot} locators are available only while the Unity Editor is in Play Mode.");
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
}
