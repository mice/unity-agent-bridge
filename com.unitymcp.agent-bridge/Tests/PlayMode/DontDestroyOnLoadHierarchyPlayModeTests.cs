using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class DontDestroyOnLoadHierarchyPlayModeTests
    {
        private const string UnityQueriesNamespace = "UnityMcp.BuiltInPlugins.UnityQueries.";
        private readonly List<GameObject> _objectsToDestroy = new List<GameObject>();
        private readonly List<string> _reportPaths = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.isPlaying, Is.True);
            SetRootDiscoveryOverride(null);
            SetSelection(null);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetRootDiscoveryOverride(null);
            SetSelection(null);
            foreach (var gameObject in _objectsToDestroy)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.Destroy(gameObject);
                }
            }

            _objectsToDestroy.Clear();
            yield return null;
            DeleteReports();
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_187.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_187")]
        public IEnumerator Discovery_ReportsSpecialSceneMetadataAndRootOrdering()
        {
            var first = CreatePersistentHierarchy("AGB_DDOL_187_First", "InactiveChild", true);
            var second = CreatePersistentHierarchy("AGB_DDOL_187_Second", "Child", false);
            second.root.transform.SetSiblingIndex(0);

            var specialScene = first.root.scene;
            Assert.That(specialScene.IsValid(), Is.True);
            Assert.That(specialScene.isLoaded, Is.True);
            Assert.That(specialScene.name, Is.EqualTo("DontDestroyOnLoad"));
            Assert.That(
                string.IsNullOrEmpty(specialScene.path) || specialScene.path == "DontDestroyOnLoad",
                Is.True);
            Assert.That(
                Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).All(scene => scene.handle != specialScene.handle),
                Is.True);
            Assert.That(InvokeIsMember(first.root), Is.True, "The shared classifier includes main-stage membership.");
            Assert.That(
                Resources.FindObjectsOfTypeAll<GameObject>(),
                Does.Contain(first.child),
                "Inactive DontDestroyOnLoad descendants must remain discoverable.");

            var roots = InvokeGetRoots(out var failure);
            Assert.That(failure, Is.Null);
            Assert.That(Array.IndexOf(roots, second.root), Is.LessThan(Array.IndexOf(roots, first.root)));
            Assert.That(roots, Does.Contain(first.root));
            Assert.That(roots, Does.Contain(second.root));

            yield return null;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_188.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_188")]
        public IEnumerator FormatterAndResolver_RoundTripInactiveDescendantAndRejectInvalidSyntax()
        {
            var fixture = CreatePersistentHierarchy("AGB_DDOL_188_Root", "InactiveChild", true);

            var locator = InvokeFormatter(fixture.child);
            var success = InvokeResolver(locator, out var resolved, out var failure);
            var invalidSuccess = InvokeResolver("dontDestroyOnLoad#/AGB_DDOL_188_Root", out _, out var invalidFailure);

            Assert.That(locator, Is.EqualTo("dontDestroyOnLoad#AGB_DDOL_188_Root/InactiveChild"));
            Assert.That(success, Is.True, failure != null ? failure.Summary : string.Empty);
            Assert.That(resolved, Is.SameAs(fixture.child));
            Assert.That(resolved.activeSelf, Is.False);
            Assert.That(invalidSuccess, Is.False);
            Assert.That(invalidFailure.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
            Assert.That(invalidFailure.Errors[0].Code, Is.EqualTo("AGENTBRIDGE_LOCATOR_UNSUPPORTED"));

            yield return null;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_189.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_189")]
        public IEnumerator Resolver_DuplicatePathPicksFirstRootSibling()
        {
            var first = CreatePersistentHierarchy("AGB_DDOL_189_Duplicate", "Child", false);
            var second = CreatePersistentHierarchy("AGB_DDOL_189_Duplicate", "Child", false);
            second.root.transform.SetSiblingIndex(0);

            var success = InvokeResolver(
                "dontDestroyOnLoad#AGB_DDOL_189_Duplicate/Child",
                out var resolved,
                out var failure);

            Assert.That(success, Is.True, failure != null ? failure.Summary : string.Empty);
            Assert.That(resolved, Is.SameAs(second.child));
            Assert.That(resolved, Is.Not.SameAs(first.child));

            yield return null;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_190.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_190")]
        public IEnumerator HierarchyTool_RootAndSubtreeReturnReusableHierarchyV2Identity()
        {
            Assert.That(Application.isPlaying, Is.True);
            CreatePersistentHierarchy("AGB_DDOL_190_Root", "InactiveChild", true);
            var tool = CreateTool("UnityGetHierarchyTool");

            var rootResult = tool.Execute(
                CreateContext("agb.ddol.190.root", "unity.get_hierarchy", "{\"locator\":\"dontDestroyOnLoad\",\"maxDepth\":2,\"limit\":100,\"includeComponents\":true}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(rootResult.ReportPath);
            var rootMetrics = JsonUtility.FromJson<HierarchyMetricsMirror>(rootResult.MetricsObjectJson);
            var childNode = rootMetrics.nodes.First(node => node.name == "InactiveChild");

            Assert.That(rootResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(rootResult.ChangedFiles, Is.Empty);
            Assert.That(rootMetrics.contractVersion, Is.EqualTo("hierarchy.v2"));
            Assert.That(rootMetrics.target.targetKind, Is.EqualTo("scene_root"));
            Assert.That(rootMetrics.target.name, Is.EqualTo("DontDestroyOnLoad"));
            Assert.That(string.IsNullOrEmpty(rootMetrics.target.scenePath), Is.True);
            Assert.That(childNode.locator, Is.EqualTo("dontDestroyOnLoad#AGB_DDOL_190_Root/InactiveChild"));
            Assert.That(childNode.path, Is.EqualTo("AGB_DDOL_190_Root/InactiveChild"));
            Assert.That(string.IsNullOrEmpty(childNode.scenePath), Is.True);
            Assert.That(childNode.components, Is.Not.Null);
            Assert.That(rootResult.MetricsObjectJson, Does.Contain("\"scenePath\":null"));
            Assert.That(rootResult.MetricsObjectJson, Does.Contain("dontDestroyOnLoad#"));

            var subtreeResult = tool.Execute(
                CreateContext(
                    "agb.ddol.190.subtree",
                    "unity.get_hierarchy",
                    "{\"locator\":\"dontDestroyOnLoad#AGB_DDOL_190_Root/InactiveChild\",\"maxDepth\":0,\"limit\":10}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(subtreeResult.ReportPath);
            var subtreeMetrics = JsonUtility.FromJson<HierarchyMetricsMirror>(subtreeResult.MetricsObjectJson);

            Assert.That(subtreeResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(subtreeMetrics.target.targetKind, Is.EqualTo("scene_subtree"));
            Assert.That(subtreeMetrics.rootCount, Is.EqualTo(1));
            Assert.That(subtreeMetrics.returnedNodeCount, Is.EqualTo(1));

            yield return null;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_191.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_191")]
        public IEnumerator HierarchyTool_EmptyVirtualRootSucceedsAndCurrentSceneRemainsScoped()
        {
            var tool = CreateTool("UnityGetHierarchyTool");
            SetRootDiscoveryOverride(() => Array.Empty<GameObject>());
            var emptyResult = tool.Execute(
                CreateContext("agb.ddol.191.empty", "unity.get_hierarchy", "{\"locator\":\"dontDestroyOnLoad\"}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(emptyResult.ReportPath);
            var emptyMetrics = JsonUtility.FromJson<HierarchyMetricsMirror>(emptyResult.MetricsObjectJson);
            SetRootDiscoveryOverride(null);

            Assert.That(emptyResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(emptyMetrics.rootCount, Is.Zero);
            Assert.That(emptyMetrics.nodeCount, Is.Zero);
            Assert.That(emptyMetrics.returnedNodeCount, Is.Zero);
            Assert.That(emptyMetrics.nodes, Is.Empty);

            var currentSceneRoot = new GameObject("AGB_DDOL_191_CurrentScene");
            _objectsToDestroy.Add(currentSceneRoot);
            CreatePersistentHierarchy("AGB_DDOL_191_Persistent", "Child", false);

            var currentSceneResult = tool.Execute(
                CreateContext("agb.ddol.191.current", "unity.get_hierarchy", "{\"locator\":\"currentScene\",\"maxDepth\":1,\"limit\":100}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(currentSceneResult.ReportPath);
            var currentMetrics = JsonUtility.FromJson<HierarchyMetricsMirror>(currentSceneResult.MetricsObjectJson);

            Assert.That(currentSceneResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(currentMetrics.nodes.Any(node => node.name == "AGB_DDOL_191_CurrentScene"), Is.True);
            Assert.That(currentMetrics.nodes.Any(node => node.name == "AGB_DDOL_191_Persistent"), Is.False);

            yield return null;
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_192.md
        [UnityTest]
        [Category("AGB_ReadOnly")]
        [Category("AGB_192")]
        public IEnumerator SelectionAndComponentInfo_ReuseCanonicalLocator()
        {
            var fixture = CreatePersistentHierarchy("AGB_DDOL_192_Root", "SelectedChild", false);
            SetSelection(fixture.child);

            var selectionTool = CreateTool("UnitySelectionInfoTool");
            var selectionResult = selectionTool.Execute(
                CreateContext("agb.ddol.192.selection", "unity.get_selection_info", "{}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(selectionResult.ReportPath);
            var selectionMetrics = JsonUtility.FromJson<SelectionMetricsMirror>(selectionResult.MetricsObjectJson);
            var locator = selectionMetrics.active.locator;

            Assert.That(selectionResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(selectionResult.ChangedFiles, Is.Empty);
            Assert.That(locator, Is.EqualTo("dontDestroyOnLoad#AGB_DDOL_192_Root/SelectedChild"));

            var componentTool = CreateTool("UnityGameObjectComponentInfoTool");
            var componentResult = componentTool.Execute(
                CreateContext(
                    "agb.ddol.192.components",
                    "unity.get_gameobject_component_info",
                    "{\"locator\":\"" + locator + "\"}"),
                NoOpUnityMcpCancellation.Instance);
            TrackReport(componentResult.ReportPath);
            var componentMetrics = JsonUtility.FromJson<ComponentMetricsMirror>(componentResult.MetricsObjectJson);
            var componentReport = ReadReport(componentResult.ReportPath);

            Assert.That(componentResult.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(componentResult.ChangedFiles, Is.Empty);
            Assert.That(componentMetrics.target.locator, Is.EqualTo(locator));
            Assert.That(componentMetrics.target.path, Is.EqualTo("AGB_DDOL_192_Root/SelectedChild"));
            Assert.That(string.IsNullOrEmpty(componentMetrics.target.scenePath), Is.True);
            Assert.That(componentResult.MetricsObjectJson, Does.Contain("\"scenePath\":null"));
            Assert.That(componentReport, Does.Contain("\"scenePath\":null"));

            yield return null;
        }

        private (GameObject root, GameObject child) CreatePersistentHierarchy(string rootName, string childName, bool inactiveChild)
        {
            var root = new GameObject(rootName);
            var child = new GameObject(childName);
            child.transform.SetParent(root.transform, false);
            child.SetActive(!inactiveChild);
            _objectsToDestroy.Add(root);
            UnityEngine.Object.DontDestroyOnLoad(root);
            return (root, child);
        }

        private static bool InvokeIsMember(GameObject gameObject)
        {
            var method = FindType(UnityQueriesNamespace + "DontDestroyOnLoadHierarchy")
                .GetMethod("IsMember", BindingFlags.Public | BindingFlags.Static);
            return (bool)method.Invoke(null, new object[] { gameObject });
        }

        private static GameObject[] InvokeGetRoots(out UnityMcpToolResult failure)
        {
            var method = FindType(UnityQueriesNamespace + "DontDestroyOnLoadHierarchy")
                .GetMethod("TryGetRoots", BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[] { null, null };
            var success = (bool)method.Invoke(null, arguments);
            failure = arguments[1] as UnityMcpToolResult;
            Assert.That(success, Is.True, failure != null ? failure.Summary : string.Empty);
            return (GameObject[])arguments[0];
        }

        private static string InvokeFormatter(GameObject gameObject)
        {
            var method = FindType(UnityQueriesNamespace + "GameObjectLocatorFormatter")
                .GetMethod("GetLocator", BindingFlags.Public | BindingFlags.Static);
            return (string)method.Invoke(null, new object[] { gameObject });
        }

        private static bool InvokeResolver(string locator, out GameObject gameObject, out UnityMcpToolResult failure)
        {
            var method = FindType(UnityQueriesNamespace + "GameObjectLocatorResolver")
                .GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[] { locator, null, null };
            var success = (bool)method.Invoke(null, arguments);
            gameObject = arguments[1] as GameObject;
            failure = arguments[2] as UnityMcpToolResult;
            return success;
        }

        private static void SetRootDiscoveryOverride(Func<GameObject[]> provider)
        {
            var field = FindType(UnityQueriesNamespace + "DontDestroyOnLoadHierarchy")
                .GetField("RootDiscoveryOverrideForTests", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "Expected UNITY_INCLUDE_TESTS root discovery seam.");
            field.SetValue(null, provider);
        }

        private static IUnityMcpTool CreateTool(string typeName)
        {
            return (IUnityMcpTool)Activator.CreateInstance(FindType(UnityQueriesNamespace + typeName));
        }

        private static void SetSelection(GameObject gameObject)
        {
            var selectionType = FindType("UnityEditor.Selection");
            selectionType.GetProperty("activeObject", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, gameObject);
            selectionType.GetProperty("objects", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, gameObject != null ? new UnityEngine.Object[] { gameObject } : Array.Empty<UnityEngine.Object>());
        }

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "Could not resolve loaded type " + fullName + ".");
            return type;
        }

        private void TrackReport(string reportPath)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                _reportPaths.Add(reportPath);
            }
        }

        private static string ReadReport(string reportPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return File.ReadAllText(Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private void DeleteReports()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            foreach (var reportPath in _reportPaths)
            {
                var absolutePath = Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }

            _reportPaths.Clear();
        }

        private static UnityMcpToolContext CreateContext(string commandId, string toolName, string rawArgsJson)
        {
            return new UnityMcpToolContext
            {
                CommandId = commandId,
                ToolName = toolName,
                TimeoutMs = 10000,
                RawArgsJson = rawArgsJson,
                ProjectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                TempRoot = "Temp/AgentBridge"
            };
        }

#pragma warning disable 0649 // Fields are populated by JsonUtility during contract assertions.
        [Serializable]
        private sealed class HierarchyMetricsMirror
        {
            public string contractVersion;
            public HierarchyTargetMirror target;
            public int rootCount;
            public int nodeCount;
            public int returnedNodeCount;
            public HierarchyNodeMirror[] nodes;
        }

        [Serializable]
        private sealed class HierarchyTargetMirror
        {
            public string targetKind;
            public string scenePath;
            public string name;
        }

        [Serializable]
        private sealed class HierarchyNodeMirror
        {
            public string name;
            public string locator;
            public string path;
            public string scenePath;
            public HierarchyComponentMirror[] components;
        }

        [Serializable]
        private sealed class HierarchyComponentMirror
        {
            public int index;
            public string type;
        }

        [Serializable]
        private sealed class SelectionMetricsMirror
        {
            public SelectionItemMirror active;
        }

        [Serializable]
        private sealed class SelectionItemMirror
        {
            public string locator;
        }

        [Serializable]
        private sealed class ComponentMetricsMirror
        {
            public ComponentTargetMirror target;
        }

        [Serializable]
        private sealed class ComponentTargetMirror
        {
            public string locator;
            public string path;
            public string scenePath;
        }
#pragma warning restore 0649

        private sealed class NoOpUnityMcpCancellation : IUnityMcpCancellation
        {
            public static readonly NoOpUnityMcpCancellation Instance = new NoOpUnityMcpCancellation();

            public bool IsCancellationRequested => false;

            public void ThrowIfCancellationRequested()
            {
            }
        }
    }
}
