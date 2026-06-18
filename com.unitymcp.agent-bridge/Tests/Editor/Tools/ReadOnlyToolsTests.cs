using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityMcp.Plugin;
using EditorBasics = UnityMcp.BuiltInPlugins.EditorBasics;
using ProjectInfo = UnityMcp.BuiltInPlugins.ProjectInfo;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class ReadOnlyToolsTests
    {
        private readonly List<string> _reportPaths = new List<string>();
        private readonly List<string> _assetPathsToDelete = new List<string>();
        private readonly List<GameObject> _runtimeObjectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            foreach (var reportPath in _reportPaths)
            {
                if (string.IsNullOrWhiteSpace(reportPath))
                {
                    continue;
                }

                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    continue;
                }

                var absolutePath = Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }

            _reportPaths.Clear();
            foreach (var assetPath in _assetPathsToDelete)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            _assetPathsToDelete.Clear();
            foreach (var runtimeObject in _runtimeObjectsToDestroy)
            {
                if (runtimeObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeObject);
                }
            }

            _runtimeObjectsToDestroy.Clear();
            var activeScene = EditorSceneManager.GetActiveScene();
            if (SceneManager.sceneCount != 1 ||
                activeScene.path != "Assets/Scenes/AppMain.unity" ||
                activeScene.isDirty)
            {
                EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            }

            AgentConsoleLogStore.ResetForTests();
            EditorBasics.EditorBasicsConsoleLogStore.ResetForTests();
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_043.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_043")]
        public void UnityPingTool_Execute_ReturnsSuccessAndReport()
        {
            var tool = new EditorBasics.UnityPingTool();

            var result = tool.Execute(CreatePluginContext("agb.ping.043", "unity.ping", "{}"), NoOpUnityMcpCancellation.Instance);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(result.Summary, Is.EqualTo("pong"));
            Assert.That(result.ReportPath, Is.Not.Null.And.Not.Empty);
            Assert.That(result.MetricsObjectJson, Does.Contain("unityVersion"));
            TrackReport(result.ReportPath);
            Assert.That(File.Exists(GetReportAbsolutePath(result.ReportPath)), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_044.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_044")]
        public void ProjectInfoPluginTool_Execute_ReturnsRequiredMetrics()
        {
            var tool = new ProjectInfo.GetProjectInfoTool();

            var result = tool.Execute(CreatePluginContext("agb.projectinfo.044", "unity.project.get_info", "{}"), NoOpUnityMcpCancellation.Instance);
            var metrics = JsonUtility.FromJson<ProjectInfo.GetProjectInfoPayload>(result.MetricsObjectJson);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(metrics.unityVersion, Is.Not.Empty);
            Assert.That(metrics.projectPath.Replace('\\', '/'), Does.Contain("/UnityMCP"));
            Assert.That(metrics.activeScene, Is.EqualTo("Assets/Scenes/AppMain.unity"));
            TrackReport(result.ReportPath);
            Assert.That(File.Exists(GetReportAbsolutePath(result.ReportPath)), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_045.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_045")]
        public void UnityConsoleLogTool_ErrorType_ExcludesWarningEntries()
        {
            const string warningMarker = "AGB045 warning";
            const string errorMarker = "AGB045 error";
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(warningMarker, string.Empty, LogType.Warning);
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(errorMarker, string.Empty, LogType.Error);

            var tool = new EditorBasics.UnityConsoleLogTool();
            var result = tool.Execute(CreatePluginContext("agb.console.045", "unity.get_console", "{\"types\":[\"error\"],\"count\":50}"), NoOpUnityMcpCancellation.Instance);
            var metrics = JsonUtility.FromJson<EditorBasics.UnityConsoleLogMetrics>(result.MetricsObjectJson);
            var bucket = metrics.results[0];

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(metrics.requestedTypes, Is.EqualTo(new[] { "error" }));
            Assert.That(metrics.requestedCountPerType, Is.EqualTo(50));
            Assert.That(metrics.results, Has.Length.EqualTo(1));
            Assert.That(bucket.type, Is.EqualTo("error"));
            Assert.That(bucket.returnedCount, Is.EqualTo(1));
            Assert.That(bucket.entries, Has.Some.Matches<EditorBasics.UnityConsoleLogEntry>(entry => entry.condition == errorMarker));
            Assert.That(bucket.entries, Has.None.Matches<EditorBasics.UnityConsoleLogEntry>(entry => entry.condition == warningMarker));
            TrackReport(result.ReportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_046.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_046")]
        public void UnityConsoleLogTool_WarningTypeAndCount_AppliesLimits()
        {
            const string warningMarkerOne = "AGB046 warning one";
            const string warningMarkerTwo = "AGB046 warning two";
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(warningMarkerOne, string.Empty, LogType.Warning);
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(warningMarkerTwo, string.Empty, LogType.Warning);

            var tool = new EditorBasics.UnityConsoleLogTool();
            var result = tool.Execute(CreatePluginContext("agb.console.046", "unity.get_console", "{\"types\":[\"warning\"],\"count\":1}"), NoOpUnityMcpCancellation.Instance);
            var metrics = JsonUtility.FromJson<EditorBasics.UnityConsoleLogMetrics>(result.MetricsObjectJson);
            var bucket = metrics.results[0];

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(metrics.requestedTypes, Is.EqualTo(new[] { "warning" }));
            Assert.That(metrics.requestedCountPerType, Is.EqualTo(1));
            Assert.That(bucket.type, Is.EqualTo("warning"));
            Assert.That(bucket.returnedCount, Is.EqualTo(1));
            Assert.That(bucket.entries, Has.Length.EqualTo(1));
            Assert.That(bucket.entries[0].condition, Is.EqualTo(warningMarkerTwo));
            TrackReport(result.ReportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_090.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_090")]
        public void UnityConsoleLogTool_InfoTypeAndZeroCount_ReturnsAllMatchingEntries()
        {
            const string infoMarkerOne = "AGB046 info one";
            const string infoMarkerTwo = "AGB046 info two";
            const string errorMarker = "AGB046 error";
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(infoMarkerOne, string.Empty, LogType.Log);
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(errorMarker, string.Empty, LogType.Error);
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(infoMarkerTwo, string.Empty, LogType.Log);

            var tool = new EditorBasics.UnityConsoleLogTool();
            var result = tool.Execute(CreatePluginContext("agb.console.046.all", "unity.get_console", "{\"types\":[\"info\"],\"count\":0}"), NoOpUnityMcpCancellation.Instance);
            var metrics = JsonUtility.FromJson<EditorBasics.UnityConsoleLogMetrics>(result.MetricsObjectJson);
            var bucket = metrics.results[0];

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(metrics.requestedTypes, Is.EqualTo(new[] { "info" }));
            Assert.That(metrics.requestedCountPerType, Is.EqualTo(0));
            Assert.That(bucket.type, Is.EqualTo("info"));
            Assert.That(bucket.returnedCount, Is.EqualTo(2));
            Assert.That(bucket.entries, Has.Length.EqualTo(2));
            Assert.That(bucket.entries, Has.All.Matches<EditorBasics.UnityConsoleLogEntry>(entry => entry.type == "Log"));
            Assert.That(bucket.entries[0].condition, Is.EqualTo(infoMarkerTwo));
            TrackReport(result.ReportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_046.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_046")]
        public void UnityConsoleLogTool_MultiTypeQuery_ReturnsGroupedBucketsInRequestedOrder()
        {
            const string warningMarker = "AGB046 grouped warning";
            const string errorMarker = "AGB046 grouped error";
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(errorMarker, string.Empty, LogType.Error);
            EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry(warningMarker, string.Empty, LogType.Warning);

            var tool = new EditorBasics.UnityConsoleLogTool();
            var result = tool.Execute(CreatePluginContext("agb.console.046.multi", "unity.get_console", "{\"types\":[\"warning\",\"error\"],\"count\":5}"), NoOpUnityMcpCancellation.Instance);
            var metrics = JsonUtility.FromJson<EditorBasics.UnityConsoleLogMetrics>(result.MetricsObjectJson);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(metrics.requestedTypes, Is.EqualTo(new[] { "warning", "error" }));
            Assert.That(metrics.requestedCountPerType, Is.EqualTo(5));
            Assert.That(metrics.results, Has.Length.EqualTo(2));
            Assert.That(metrics.results[0].type, Is.EqualTo("warning"));
            Assert.That(metrics.results[0].returnedCount, Is.EqualTo(1));
            Assert.That(metrics.results[0].entries[0].condition, Is.EqualTo(warningMarker));
            Assert.That(metrics.results[1].type, Is.EqualTo("error"));
            Assert.That(metrics.results[1].returnedCount, Is.EqualTo(1));
            Assert.That(metrics.results[1].entries[0].condition, Is.EqualTo(errorMarker));
            TrackReport(result.ReportPath);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_091.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_091")]
        public void UnityConsoleLogTool_InvalidType_ReturnsInvalidArgs()
        {
            var tool = new EditorBasics.UnityConsoleLogTool();

            var result = tool.Execute(CreatePluginContext("agb.console.091", "unity.get_console", "{\"types\":[\"warnning\"],\"count\":1}"), NoOpUnityMcpCancellation.Instance);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
            Assert.That(result.Errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.Errors[0].Code, Is.EqualTo("AGENTBRIDGE_CONSOLE_TYPE_INVALID"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_092.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_092")]
        public void UnityConsoleLogTool_CountAboveRetentionBound_ReturnsInvalidArgs()
        {
            var tool = new EditorBasics.UnityConsoleLogTool();

            var result = tool.Execute(CreatePluginContext("agb.console.092", "unity.get_console", "{\"types\":[\"error\"],\"count\":1001}"), NoOpUnityMcpCancellation.Instance);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
            Assert.That(result.Errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.Errors[0].Code, Is.EqualTo("AGENTBRIDGE_CONSOLE_COUNT_INVALID"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_091.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_091")]
        public void UnityConsoleLogTool_DuplicateTypes_ReturnsInvalidArgs()
        {
            var tool = new EditorBasics.UnityConsoleLogTool();

            var result = tool.Execute(CreatePluginContext("agb.console.091.duplicate", "unity.get_console", "{\"types\":[\"error\",\"error\"],\"count\":1}"), NoOpUnityMcpCancellation.Instance);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
            Assert.That(result.Errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.Errors[0].Code, Is.EqualTo("AGENTBRIDGE_CONSOLE_TYPE_DUPLICATE"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_093.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_093")]
        public void EditorBasicsConsoleLogStore_RetainsAtMostOneThousandEntriesPerType()
        {
            for (var index = 0; index < EditorBasics.EditorBasicsConsoleLogStore.MaxEntriesPerType + 1; index++)
            {
                EditorBasics.EditorBasicsConsoleLogStore.AppendTestEntry("AGB093 warning " + index, string.Empty, LogType.Warning);
            }

            var snapshot = EditorBasics.EditorBasicsConsoleLogStore.GetSnapshot(EditorBasics.ConsoleLogQueryType.Warning, 0);

            Assert.That(snapshot.Count, Is.EqualTo(EditorBasics.EditorBasicsConsoleLogStore.MaxEntriesPerType));
            Assert.That(snapshot[0].Condition, Is.EqualTo("AGB093 warning 1000"));
            Assert.That(snapshot[snapshot.Count - 1].Condition, Is.EqualTo("AGB093 warning 1"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_047.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_047")]
        public void AgentToolRegistry_Discover_FindsNonMigratedTools()
        {
            var registry = new AgentToolRegistry();

            registry.Discover();

            Assert.That(registry.TryGetTool("unity.ping", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.project_info", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.project.get_info", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.get_console", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.assetdatabase_search", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.get_editor_state", out _), Is.False);
            Assert.That(registry.TryGetTool("unity.open_scene", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.get_hierarchy", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.read_report", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.get_selection_info", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.get_gameobject_component_info", out _), Is.True);
            Assert.That(registry.TryGetTool("unity.compile", out _), Is.True);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void AgentToolRegistry_ListTools_AllDiscoveredToolsExposeGovernedMetadata()
        {
            var registry = new AgentToolRegistry();

            registry.Discover();

            var descriptors = registry.ListTools();
            Assert.That(descriptors, Is.Not.Empty);
            Assert.That(descriptors, Has.All.Matches<ToolDescriptor>(descriptor =>
                !string.IsNullOrWhiteSpace(descriptor.Name) &&
                !string.IsNullOrWhiteSpace(descriptor.Description) &&
                descriptor.AllowedModes != ToolExecutionModes.None));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.compile" &&
                descriptor.AllowedModes == ToolExecutionModes.Edit &&
                descriptor.SideEffect == ToolSideEffect.MutatesProject));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.assetdatabase_search" &&
                descriptor.AllowedModes == ToolExecutionModes.EditAndPlay &&
                descriptor.SideEffect == ToolSideEffect.ReadsProject &&
                descriptor.ArgsSchemaPath == "Documentation~/schemas/unity.assetdatabase_search.args.schema.json"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.open_scene" &&
                descriptor.AllowedModes == ToolExecutionModes.Edit &&
                descriptor.SideEffect == ToolSideEffect.MutatesProject &&
                descriptor.ArgsSchemaPath == "Documentation~/schemas/unity.open_scene.args.schema.json"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.get_hierarchy" &&
                descriptor.AllowedModes == ToolExecutionModes.EditAndPlay &&
                descriptor.SideEffect == ToolSideEffect.ReadsProject &&
                descriptor.ArgsSchemaPath == "Documentation~/schemas/unity.get_hierarchy.args.schema.json"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.read_report" &&
                descriptor.AllowedModes == ToolExecutionModes.EditAndPlay &&
                descriptor.SideEffect == ToolSideEffect.ReadsProject &&
                descriptor.ArgsSchemaPath == "Documentation~/schemas/unity.read_report.args.schema.json"));
            Assert.That(descriptors, Has.Some.Matches<ToolDescriptor>(descriptor =>
                descriptor.Name == "unity.run_static_method" &&
                descriptor.AllowedModes == ToolExecutionModes.EditAndPlay &&
                descriptor.SideEffect == ToolSideEffect.RunsUserCode));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_048.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_048")]
        public void ReadOnlyToolSchemas_Exist()
        {
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.ping.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_console.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.compile.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.assetdatabase_search.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_editor_state.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_editor_state.metrics.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_editor_state.payload.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.read_report.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.read_report.metrics.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.read_report.payload.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.open_scene.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.open_scene.metrics.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.open_scene.payload.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_hierarchy.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_hierarchy.metrics.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_hierarchy.payload.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_selection_info.args.schema.json")), Is.True);
            Assert.That(File.Exists(GetPackageRelativePath("Documentation~/schemas/unity.get_gameobject_component_info.args.schema.json")), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_166.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_166")]
        public void UnityGetEditorStateTool_Execute_ReturnsSnapshotAndReport()
        {
            var tool = new EditorBasics.UnityGetEditorStateTool();

            var result = tool.Execute(CreatePluginContext("agb.editorstate.001", "unity.get_editor_state", "{}"), NoOpUnityMcpCancellation.Instance);

            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"contractVersion\":\"editor_state.v1\""));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"editorState\""));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"runtimeMode\""));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"loadedScenes\""));
            Assert.That(result.ChangedFiles, Is.Empty);
            Assert.That(result.ReportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.ReportPath);
            var report = File.ReadAllText(GetReportAbsolutePath(result.ReportPath));
            Assert.That(report, Does.Contain("\"payloadVersion\":\"editor_state.v1\""));
            Assert.That(report, Does.Contain("\"generatedAt\":\""));
            Assert.That(report, Does.Contain("\"editorState\""));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_147")]
        public void UnityGetHierarchyTool_CurrentScene_EnumeratesActiveSceneOnly()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            var additiveScenePath = EnsureTempSceneAsset("HierarchyAdditiveScene");
            var additiveScene = EditorSceneManager.OpenScene(additiveScenePath, OpenSceneMode.Additive);
            var additiveRoot = new GameObject("AdditiveSceneRoot");
            SceneManager.MoveGameObjectToScene(additiveRoot, additiveScene);

            try
            {
                EditorSceneManager.SetActiveScene(activeScene);
                var tool = new UnityGetHierarchyTool();
                var result = tool.Execute(CreateContext("agb.hierarchy.001", "{}"), NoOpAgentCancellation.Instance);

                Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
                Assert.That(result.metricsObjectJson, Does.Contain("\"contractVersion\":\"hierarchy.v2\""));
                Assert.That(result.metricsObjectJson, Does.Contain("\"targetKind\":\"scene_root\""));
                Assert.That(result.metricsObjectJson, Does.Contain("\"maxDepth\":" + SceneQueryContract.DefaultHierarchyMaxDepth));
                Assert.That(result.metricsObjectJson, Does.Contain("\"limit\":" + SceneQueryContract.DefaultHierarchyLimit));
                Assert.That(result.metricsObjectJson, Does.Contain("\"details\":{\"available\":true"));
                Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedRead\":false"));
                Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedPointers\":[\"/result/nodes\"]"));
                Assert.That(result.metricsObjectJson, Does.Contain("\"reportPath\":\"" + result.reportPath + "\""));
                Assert.That(result.metricsObjectJson, Does.Contain("\"followUp\":{\"recommended\":true"));
                Assert.That(
                    result.metricsObjectJson,
                    Does.Contain("\"tool\":\"unity.get_hierarchy\"").Or.Contain("\"tool\":\"unity.read_report\""));
                Assert.That(result.metricsObjectJson, Does.Not.Contain("AdditiveSceneRoot"));
                Assert.That(result.changedFiles, Is.Empty);
                var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));
                Assert.That(report, Does.Contain("\"payloadVersion\":\"hierarchy.v2\""));
                Assert.That(report, Does.Contain("\"boundedCompleteness\":{\"completeWithinAppliedBounds\":true"));
                TrackReport(result.reportPath);
            }
            finally
            {
                if (additiveRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(additiveRoot);
                }

                EditorSceneManager.CloseScene(additiveScene, true);
            }
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_InvalidSelection_ReturnsInvalidArgsAndReport()
        {
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath("Assets/Scenes/AppMain.unity");
            Assume.That(Selection.activeObject, Is.Not.Null);
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(CreateContext("agb.hierarchy.002", "{\"locator\":\"selection:active\"}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_SELECTION_NOT_GAMEOBJECT"));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.reportPath);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));
            Assert.That(report, Does.Contain("\"status\":\"invalid_args\""));
            Assert.That(report, Does.Contain("\"locator\":\"selection:active\""));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_InvalidPath_ReturnsInvalidArgsAndReport()
        {
            var tool = new UnityOpenSceneTool();

            var result = tool.Execute(CreateContext("agb.openscene.001", "{\"scenePath\":\"Packages/Bad.unity\"}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_SCENE_PATH_INVALID"));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.reportPath);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));
            Assert.That(report, Does.Contain("\"payloadVersion\":\"open_scene.v1\""));
            Assert.That(report, Does.Contain("\"status\":\"invalid_args\""));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_AdditiveAlreadyLoaded_IsIdempotent()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScenePath = EnsureTempSceneAsset("OpenSceneAdditiveScene");
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScene = EditorSceneManager.OpenScene(additiveScenePath, OpenSceneMode.Additive);
            Assert.That(EditorSceneManager.SaveScene(additiveScene), Is.True, "Expected additive scene fixture to be saved before open_scene validation.");
            var tool = new UnityOpenSceneTool();

            var result = tool.Execute(
                CreateContext(
                    "agb.openscene.002",
                    "{\"scenePath\":\"" + additiveScenePath + "\",\"mode\":\"additive\",\"setActive\":false}"),
                NoOpAgentCancellation.Instance);

            if (result.status != ToolResultStatus.Success)
            {
                Assert.Fail("Expected success but got " + result.status + " with summary: " + result.summary);
            }

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"alreadyLoaded\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"scenePath\":\"" + additiveScenePath + "\""));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_DefaultsToSingleModeAndSetActiveTrue()
        {
            WaitForEditorIdleSynchronously();
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var tool = new UnityOpenSceneTool();
            var context = CreateContext("agb.openscene.003", "{\"scenePath\":\"Assets/Scenes/AppMain.unity\"}");
            Assert.That(context.Settings, Is.Not.Null, "Expected test context settings to be created.");

            var result = tool.Execute(context, NoOpAgentCancellation.Instance);

            if (result.status != ToolResultStatus.Success)
            {
                Assert.Fail("Expected success but got " + result.status + " with summary: " + result.summary + " errors: " + SerializeErrors(result));
            }

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"single\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"setActive\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"scenePath\":\"Assets/Scenes/AppMain.unity\""));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(GetReportAbsolutePath(result.reportPath)), Is.True, "Expected open_scene success report to exist at " + result.reportPath + ".");
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_UnsupportedMode_ReturnsInvalidArgs()
        {
            var tool = new UnityOpenSceneTool();

            var result = tool.Execute(
                CreateContext("agb.openscene.004", "{\"scenePath\":\"Assets/Scenes/AppMain.unity\",\"mode\":\"replace\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_OPEN_SCENE_MODE_INVALID"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_DirtySceneWithoutExplicitSave_ReturnsBlocked()
        {
            WaitForEditorIdleSynchronously();
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScenePath = EnsureTempSceneAsset("OpenSceneDirtyBlocked");
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScene = EditorSceneManager.OpenScene(additiveScenePath, OpenSceneMode.Additive);
            var dirtyRoot = new GameObject("DirtyBlockedRoot");
            SceneManager.MoveGameObjectToScene(dirtyRoot, additiveScene);
            EditorSceneManager.MarkSceneDirty(additiveScene);
            var tool = new UnityOpenSceneTool();

            try
            {
                var result = tool.Execute(
                    CreateContext("agb.openscene.005", "{\"scenePath\":\"Assets/Scenes/AppMain.unity\",\"mode\":\"single\"}"),
                    NoOpAgentCancellation.Instance);

                if (result.status != ToolResultStatus.Blocked)
                {
                    Assert.Fail("Expected blocked but got " + result.status + " with summary: " + result.summary + " errors: " + SerializeErrors(result));
                }

                Assert.That(result.status, Is.EqualTo(ToolResultStatus.Blocked));
                Assert.That(result.errors, Has.Count.GreaterThanOrEqualTo(1));
                Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_DIRTY_SCENE_BLOCKED"));
                Assert.That(result.metricsObjectJson, Does.Contain("\"dirtyScenes\""));
                TrackReport(result.reportPath);
            }
            finally
            {
                if (dirtyRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(dirtyRoot);
                }

                if (additiveScene.IsValid() && additiveScene.isLoaded)
                {
                    EditorSceneManager.SaveScene(additiveScene);
                }

                EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityOpenSceneTool_SaveModifiedScenesTrue_SavesAndSucceeds()
        {
            WaitForEditorIdleSynchronously();
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScenePath = EnsureTempSceneAsset("OpenSceneDirtySaved");
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScene = EditorSceneManager.OpenScene(additiveScenePath, OpenSceneMode.Additive);
            var dirtyRoot = new GameObject("DirtySavedRoot");
            SceneManager.MoveGameObjectToScene(dirtyRoot, additiveScene);
            EditorSceneManager.MarkSceneDirty(additiveScene);
            var tool = new UnityOpenSceneTool();
            var context = CreateContext("agb.openscene.006", "{\"scenePath\":\"Assets/Scenes/AppMain.unity\",\"saveModifiedScenes\":true}");

            try
            {
                Assert.That(context.Settings, Is.Not.Null, "Expected test context settings to be created.");
                var result = tool.Execute(context, NoOpAgentCancellation.Instance);

                if (result.status != ToolResultStatus.Success)
                {
                    Assert.Fail("Expected success but got " + result.status + " with summary: " + result.summary + " errors: " + SerializeErrors(result));
                }

                Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
                Assert.That(result.metricsObjectJson, Does.Contain("\"savedModifiedScenes\":true"));
                Assert.That(result.metricsObjectJson, Does.Contain("\"scenePath\":\"Assets/Scenes/AppMain.unity\""));
                Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty, "Expected saveModifiedScenes result to emit a report path.");
                Assert.That(File.Exists(GetReportAbsolutePath(result.reportPath)), Is.True, "Expected saveModifiedScenes report to exist at " + result.reportPath + ".");
                TrackReport(result.reportPath);
            }
            finally
            {
                EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_LoadedSceneLocator_UsesAssetScopedLocators()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var additiveScenePath = EnsureTempSceneAsset("HierarchyLoadedScene");
            var additiveScene = EditorSceneManager.OpenScene(additiveScenePath, OpenSceneMode.Additive);
            var additiveRoot = new GameObject("LoadedSceneRoot");
            SceneManager.MoveGameObjectToScene(additiveRoot, additiveScene);
            Assert.That(EditorSceneManager.SaveScene(additiveScene), Is.True);
            var tool = new UnityGetHierarchyTool();

            try
            {
                var activeSceneResult = tool.Execute(
                    CreateContext("agb.hierarchy.003.active", "{\"locator\":\"Assets/Scenes/AppMain.unity\"}"),
                    NoOpAgentCancellation.Instance);

                Assert.That(activeSceneResult.status, Is.EqualTo(ToolResultStatus.Success));
                Assert.That(activeSceneResult.metricsObjectJson, Does.Contain("\"scenePath\":\"Assets/Scenes/AppMain.unity\""));
                Assert.That(activeSceneResult.metricsObjectJson, Does.Contain("\"locator\":\"Assets/Scenes/AppMain.unity#"));
                Assert.That(activeSceneResult.metricsObjectJson, Does.Not.Contain("\"locator\":\"currentScene#"));
                TrackReport(activeSceneResult.reportPath);

                var result = tool.Execute(
                    CreateContext("agb.hierarchy.003", "{\"locator\":\"" + additiveScenePath + "\"}"),
                    NoOpAgentCancellation.Instance);

                Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
                Assert.That(result.metricsObjectJson, Does.Contain("\"scenePath\":\"" + additiveScenePath + "\""));
                Assert.That(result.metricsObjectJson, Does.Contain("\"locator\":\"" + additiveScenePath + "#LoadedSceneRoot\""));
                TrackReport(result.reportPath);
            }
            finally
            {
                EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_PrefabLocator_IncludeComponents_ReturnsPrefabRoot()
        {
            var prefabPath = EnsureTempPrefabAsset();
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext("agb.hierarchy.004", "{\"locator\":\"" + prefabPath + "\",\"includeComponents\":true}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"targetKind\":\"prefab_root\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"prefabAssetPath\":\"" + prefabPath + "\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"componentCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"components\":["));
            Assert.That(result.metricsObjectJson, Does.Contain("\"index\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"type\":\"" + typeof(Transform).FullName + "\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_SelectionLocator_ReturnsSelectedSubtree()
        {
            var root = CreateTempComponentHost("SelectionHierarchyRoot");
            new GameObject("SelectionChild").transform.SetParent(root.transform, false);
            Selection.activeObject = root;
            Selection.objects = new UnityEngine.Object[] { root };
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext("agb.hierarchy.005", "{\"locator\":\"selection:active\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"targetKind\":\"selection\""));
            Assert.That(result.metricsObjectJson, Does.Contain("SelectionHierarchyRoot"));
            Assert.That(result.metricsObjectJson, Does.Contain("SelectionChild"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_InstanceLocator_DepthLimit_TruncatesSubtree()
        {
            var root = CreateTempComponentHost("InstanceHierarchyRoot");
            new GameObject("InstanceHierarchyChild").transform.SetParent(root.transform, false);
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext(
                    "agb.hierarchy.006",
                    "{\"locator\":\"instance:" + root.GetInstanceID() + "\",\"maxDepth\":0,\"limit\":50}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"targetKind\":\"instance\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedNodeCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"truncated\":true"));
            Assert.That(result.metricsObjectJson, Does.Not.Contain("InstanceHierarchyChild"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_CurrentSceneSubtree_IncludeComponents_KeepsInactiveNodes()
        {
            var root = CreateTempComponentHost("InactiveHierarchyRoot");
            root.AddComponent<Camera>();
            var inactiveChild = new GameObject("InactiveHierarchyChild");
            inactiveChild.transform.SetParent(root.transform, false);
            inactiveChild.SetActive(false);
            _runtimeObjectsToDestroy.Add(inactiveChild);
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext(
                    "agb.hierarchy.007",
                    "{\"locator\":\"currentScene#InactiveHierarchyRoot\",\"includeComponents\":true}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"activeSelf\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"activeInHierarchy\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"components\":["));
            Assert.That(result.metricsObjectJson, Does.Contain("InactiveHierarchyChild"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_147")]
        public void UnityGetHierarchyTool_ComponentSummaries_AreCappedAndMissingScriptUsesNullType()
        {
            var prefabPath = EnsureTempMissingScriptPrefabAsset();
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext("agb.hierarchy.007.missing", "{\"locator\":\"" + prefabPath + "\",\"includeComponents\":true}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"hasMissingScripts\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"type\":null"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_147")]
        public void UnityGetHierarchyTool_ComponentSummaries_TruncateAfterEightEntries()
        {
            var root = CreateTempComponentHost("HierarchyManyComponents");
            for (var index = 0; index < 9; index++)
            {
                root.AddComponent<TestStringComponent>();
            }

            var tool = new UnityGetHierarchyTool();
            var locator = GameObjectLocatorFormatter.GetLocator(root);
            var result = tool.Execute(
                CreateContext("agb.hierarchy.007.cap", "{\"locator\":\"" + locator + "\",\"includeComponents\":true}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"componentsTruncated\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"componentCount\":10"));
            Assert.That(CountOccurrences(result.metricsObjectJson, "\"type\":\""), Is.EqualTo(SceneQueryContract.HierarchyComponentSummaryLimit));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_CurrentSceneSubtree_DuplicateNames_PicksFirstSibling()
        {
            var root = CreateTempComponentHost("HierarchyDuplicateRoot");
            var first = new GameObject("Dup");
            var second = new GameObject("Dup");
            _runtimeObjectsToDestroy.Add(first);
            _runtimeObjectsToDestroy.Add(second);
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext("agb.hierarchy.007.duplicate", "{\"locator\":\"currentScene#HierarchyDuplicateRoot/Dup\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"path\":\"HierarchyDuplicateRoot/Dup\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"instanceId\":" + first.GetInstanceID()));
            Assert.That(result.metricsObjectJson, Does.Not.Contain("\"instanceId\":" + second.GetInstanceID()));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_UnloadedSceneLocator_ReturnsInvalidArgs()
        {
            var tempScenePath = EnsureTempSceneAsset("HierarchyUnloadedScene");
            EditorSceneManager.OpenScene("Assets/Scenes/AppMain.unity", OpenSceneMode.Single);
            var tool = new UnityGetHierarchyTool();

            var result = tool.Execute(
                CreateContext("agb.hierarchy.008", "{\"locator\":\"" + tempScenePath + "\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_SCENE_NOT_LOADED"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGetHierarchyTool_InvalidBounds_ReturnInvalidArgs()
        {
            var tool = new UnityGetHierarchyTool();

            var negativeDepthResult = tool.Execute(
                CreateContext("agb.hierarchy.009.depth", "{\"maxDepth\":-1}"),
                NoOpAgentCancellation.Instance);
            var zeroLimitResult = tool.Execute(
                CreateContext("agb.hierarchy.009.limit.zero", "{\"limit\":0}"),
                NoOpAgentCancellation.Instance);
            var oversizeLimitResult = tool.Execute(
                CreateContext("agb.hierarchy.009.limit.large", "{\"limit\":5001}"),
                NoOpAgentCancellation.Instance);

            Assert.That(negativeDepthResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(negativeDepthResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_HIERARCHY_MAX_DEPTH_INVALID"));
            Assert.That(zeroLimitResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(zeroLimitResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_HIERARCHY_LIMIT_INVALID"));
            Assert.That(oversizeLimitResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(oversizeLimitResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_HIERARCHY_LIMIT_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_AssetSearch_WritesReportAndPaginates()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.001", "{\"query\":\"t:Scene\",\"limit\":1,\"offset\":0,\"includeDetails\":true}"), NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"query\":\"t:Scene\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"results\""));
            Assert.That(result.reportPath, Is.Not.Null.And.Not.Empty);
            TrackReport(result.reportPath);
            Assert.That(report, Does.Contain("\"generatedAt\":\""));
            Assert.That(report, Does.Contain("\"assets\":["));
            Assert.That(report, Does.Contain("\"guid\":\""));
            Assert.That(report, Does.Contain("\"locator\":\""));
            Assert.That(report, Does.Contain("\"assetPath\":\""));
            Assert.That(report, Does.Contain("\"mainObjectType\":\""));
            Assert.That(report, Does.Contain("\"extension\":\""));
            Assert.That(report, Does.Contain("\"isFolder\":"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_InvalidFolder_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.002", "{\"query\":\"t:Scene\",\"folders\":[\"Packages\"]}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ASSET_FOLDER_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_MissingQuery_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.002.required", "{}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_QUERY_REQUIRED"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_ZeroLimit_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.003", "{\"query\":\"t:Scene\",\"limit\":0}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ASSET_LIMIT_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_DefaultLimitAndOffset_AreApplied()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.004", "{\"query\":\"t:Scene\"}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"offset\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"limit\":20"));
            Assert.That(result.changedFiles, Is.Empty);
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_NegativeOffset_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.005", "{\"query\":\"t:Scene\",\"offset\":-1}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ASSET_OFFSET_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_OverMaxLimit_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.006", "{\"query\":\"t:Scene\",\"limit\":201}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ASSET_LIMIT_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_NegativeLimit_ReturnsInvalidArgs()
        {
            var tool = new UnityAssetDatabaseSearchTool();

            var result = tool.Execute(CreateContext("agb.assetsearch.006.negative", "{\"query\":\"t:Scene\",\"limit\":-1}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_ASSET_LIMIT_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_SortsBeforePaging_AndMarksTruncation()
        {
            var folder = EnsureAssetQueryTestFolder();
            var zetaPath = folder + "/ZetaScene.unity";
            var alphaPath = folder + "/AlphaScene.unity";
            _assetPathsToDelete.Add(zetaPath);
            _assetPathsToDelete.Add(alphaPath);
            File.Copy(GetProjectRelativePath("Assets/Scenes/AppMain.unity"), GetProjectRelativePath(zetaPath), true);
            File.Copy(GetProjectRelativePath("Assets/Scenes/AppMain.unity.meta"), GetProjectRelativePath(zetaPath + ".meta"), true);
            File.Copy(GetProjectRelativePath("Assets/Scenes/AppMain.unity"), GetProjectRelativePath(alphaPath), true);
            File.Copy(GetProjectRelativePath("Assets/Scenes/AppMain.unity.meta"), GetProjectRelativePath(alphaPath + ".meta"), true);
            AssetDatabase.Refresh();

            var tool = new UnityAssetDatabaseSearchTool();
            var result = tool.Execute(
                CreateContext("agb.assetsearch.007", "{\"query\":\"t:Scene\",\"folders\":[\"" + folder + "\"],\"limit\":1,\"offset\":0}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"truncated\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"nextOffset\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"name\":\"AlphaScene\""));
            Assert.That(report, Does.Contain("\"assetPath\":\"" + alphaPath + "\""));
            Assert.That(report, Does.Contain("\"dependencyCount\":null"));
            Assert.That(report, Does.Contain("\"subAssetCount\":null"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityAssetDatabaseSearchTool_IncludeDetails_UsesSampleCaps()
        {
            var folder = EnsureAssetQueryTestFolder();
            var baseScenePath = folder + "/DetailBase.unity";
            var sceneMetaPath = "Assets/Scenes/AppMain.unity.meta";
            if (!File.Exists(GetProjectRelativePath(baseScenePath)))
            {
                File.Copy(GetProjectRelativePath("Assets/Scenes/AppMain.unity"), GetProjectRelativePath(baseScenePath), true);
                File.Copy(GetProjectRelativePath(sceneMetaPath), GetProjectRelativePath(baseScenePath + ".meta"), true);
                _assetPathsToDelete.Add(baseScenePath);
            }

            var dependencyFolder = folder + "/Deps";
            if (!AssetDatabase.IsValidFolder(dependencyFolder))
            {
                AssetDatabase.CreateFolder(folder, "Deps");
                _assetPathsToDelete.Add(dependencyFolder);
            }

            for (var index = 0; index < 25; index++)
            {
                var materialPath = dependencyFolder + "/Mat" + index + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                {
                    var material = new Material(Shader.Find("Sprites/Default"));
                    AssetDatabase.CreateAsset(material, materialPath);
                    _assetPathsToDelete.Add(materialPath);
                }
            }

            var assetPath = folder + "/SampledSubAssets.asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath) == null)
            {
                var main = ScriptableObject.CreateInstance<AssetQueryDummyAsset>();
                AssetDatabase.CreateAsset(main, assetPath);
                _assetPathsToDelete.Add(assetPath);
                for (var index = 0; index < 25; index++)
                {
                    var child = ScriptableObject.CreateInstance<AssetQueryDummyAsset>();
                    child.name = "Child" + index;
                    AssetDatabase.AddObjectToAsset(child, assetPath);
                }
                AssetDatabase.ImportAsset(assetPath);
            }

            var tool = new UnityAssetDatabaseSearchTool();
            var result = tool.Execute(
                CreateContext("agb.assetsearch.008", "{\"query\":\"SampledSubAssets\",\"folders\":[\"" + folder + "\"],\"includeDetails\":true}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(report, Does.Contain("\"dependencySampleTruncated\":"));
            Assert.That(report, Does.Contain("\"subAssetSampleTruncated\":true"));
            Assert.That(CountOccurrences(report, "\"Child"), Is.GreaterThanOrEqualTo(20));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnitySelectionInfoTool_EmptySelection_ReturnsSuccess()
        {
            Selection.objects = Array.Empty<UnityEngine.Object>();
            var tool = new UnitySelectionInfoTool();

            var result = tool.Execute(CreateContext("agb.selection.001", "{}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"selectionCount\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"active\":null"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnitySelectionInfoTool_SceneSelection_ReturnsReusableLocator()
        {
            var sceneObject = CreateTempComponentHost("SceneSelectionHost");
            Selection.activeObject = sceneObject;
            Selection.objects = new UnityEngine.Object[] { sceneObject };
            var tool = new UnitySelectionInfoTool();

            var result = tool.Execute(CreateContext("agb.selection.002", "{}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"kind\":\"sceneObject\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"locator\":\"currentScene#").Or.Contain("\"locator\":\"instance:"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnitySelectionInfoTool_AssetSelection_ReturnsAssetLocatorAndGuid()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Scenes/AppMain.unity");
            Assume.That(asset, Is.Not.Null);
            Selection.activeObject = asset;
            Selection.objects = new[] { asset };
            var tool = new UnitySelectionInfoTool();

            var result = tool.Execute(CreateContext("agb.selection.003", "{}"), NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"kind\":\"asset\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"assets\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"selectionCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"locator\":\"Assets/Scenes/AppMain.unity\""));
            Assert.That(report, Does.Contain("\"activeIndex\":0"));
            Assert.That(report, Does.Contain("\"guid\":\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnitySelectionInfoTool_ComponentSelection_ClassifiesComponent()
        {
            var sceneObject = CreateTempComponentHost("SelectionComponentHost");
            var component = sceneObject.AddComponent<Camera>();
            Selection.activeObject = component;
            Selection.objects = new UnityEngine.Object[] { component };
            var tool = new UnitySelectionInfoTool();

            var result = tool.Execute(CreateContext("agb.selection.004", "{}"), NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"components\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"selectionCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"kind\":\"component\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"active\""));
            Assert.That(report, Does.Contain("\"activeIndex\":0"));
            Assert.That(report, Does.Contain(typeof(Camera).FullName));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_148")]
        public void UnityGameObjectComponentInfoTool_ListMode_ReturnsComponentSummaries()
        {
            var sceneObject = CreateTempComponentHost("ListModeHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();

            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);
            var result = tool.Execute(CreateContext("agb.components.001", "{\"locator\":\"" + locator + "\"}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"component_list\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"componentCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"details\":{\"available\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedRead\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"reportPath\":\"" + result.reportPath + "\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"followUp\":{\"recommended\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"tool\":\"unity.get_gameobject_component_info\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"locator\":\"" + locator + "\""));
            Assert.That(result.metricsObjectJson, Does.Match("\\\"componentIndex\\\":\\d+"));
            Assert.That(result.changedFiles, Is.Empty);
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_InvalidPropertyMode_ReturnsInvalidArgs()
        {
            var tool = new UnityGameObjectComponentInfoTool();

            var result = tool.Execute(CreateContext("agb.components.002", "{\"propertyMode\":\"summary\"}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_PROPERTY_MODE_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_DefaultPropertyMode_IsDebug()
        {
            var sceneObject = CreateTempComponentHost("DefaultPropertyModeHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);

            var result = tool.Execute(
                CreateContext("agb.components.003", "{\"locator\":\"" + locator + "\",\"componentName\":\"Camera\",\"propertyLimit\":0}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"component_inspect\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"propertyMode\":\"debug\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"matchedCount\":"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_NegativePropertyLimit_ReturnsInvalidArgs()
        {
            var tool = new UnityGameObjectComponentInfoTool();

            var result = tool.Execute(CreateContext("agb.components.004", "{\"propertyLimit\":-1}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_PROPERTY_LIMIT_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_SelectionActive_ResolvesComponentGameObject()
        {
            var sceneObject = CreateTempComponentHost("SelectionResolverHost");
            var component = sceneObject.AddComponent<Camera>();
            Selection.activeObject = component;

            var success = GameObjectLocatorResolver.TryResolve("selection:active", out var resolved, out var failure);

            Assert.That(success, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(resolved, Is.EqualTo(sceneObject));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_InstanceId_ResolvesGameObject()
        {
            var sceneObject = CreateTempComponentHost("InstanceResolverHost");

            var success = GameObjectLocatorResolver.TryResolve("instance:" + sceneObject.GetInstanceID(), out var resolved, out var failure);

            Assert.That(success, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(resolved, Is.EqualTo(sceneObject));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_InvalidHierarchySyntax_ReturnsInvalidArgs()
        {
            var success = GameObjectLocatorResolver.TryResolve("currentScene#/Main Camera", out _, out var failure);

            Assert.That(success, Is.False);
            Assert.That(failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_LOCATOR_UNSUPPORTED"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_CurrentScene_ResolvesInactiveGameObject()
        {
            var root = CreateTempComponentHost("InactiveRoot");
            var child = new GameObject("InactiveChild");
            _runtimeObjectsToDestroy.Add(child);
            child.transform.SetParent(root.transform, false);
            child.SetActive(false);

            var success = GameObjectLocatorResolver.TryResolve("currentScene#InactiveRoot/InactiveChild", out var resolved, out var failure);

            Assert.That(success, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(resolved, Is.EqualTo(child));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_CurrentScene_FirstMatchTraversal_PicksFirstSibling()
        {
            var root = CreateTempComponentHost("DuplicateRoot");
            var first = new GameObject("Dup");
            var second = new GameObject("Dup");
            _runtimeObjectsToDestroy.Add(first);
            _runtimeObjectsToDestroy.Add(second);
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);

            var success = GameObjectLocatorResolver.TryResolve("currentScene#DuplicateRoot/Dup", out var resolved, out var failure);

            Assert.That(success, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(resolved, Is.EqualTo(first));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_LoadedSceneLookup_UsesSceneAssetPathSyntax()
        {
            var root = CreateTempComponentHost("LoadedSceneLookupRoot");

            var success = GameObjectLocatorResolver.TryResolve("Assets/Scenes/AppMain.unity#LoadedSceneLookupRoot", out var resolved, out var failure);

            Assert.That(success, Is.True, failure != null ? failure.summary : string.Empty);
            Assert.That(failure, Is.Null);
            Assert.That(resolved, Is.EqualTo(root));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_UnloadedScene_ReturnsSceneNotLoaded()
        {
            var tempScenePath = EnsureTempSceneAsset("LocatorScene");

            var success = GameObjectLocatorResolver.TryResolve(tempScenePath + "#Root", out _, out var failure);

            Assert.That(success, Is.False);
            Assert.That(failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_SCENE_NOT_LOADED"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_UnsupportedLocator_ReturnsInvalidArgs()
        {
            var success = GameObjectLocatorResolver.TryResolve("Packages/com.foo/Thing.prefab", out _, out var failure);

            Assert.That(success, Is.False);
            Assert.That(failure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(failure.errors[0].code, Is.EqualTo("AGENTBRIDGE_LOCATOR_UNSUPPORTED"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_NamesContainingSlashOrHash_AreNotAddressableByHierarchyLocator()
        {
            var successSlash = GameObjectLocatorResolver.TryResolve("currentScene#Bad/Name", out _, out var slashFailure);
            var successHash = GameObjectLocatorResolver.TryResolve("currentScene#Bad#Name", out _, out var hashFailure);

            Assert.That(successSlash, Is.False);
            Assert.That(slashFailure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(hashFailure.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(successHash, Is.False);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void GameObjectLocatorResolver_PrefabRootAndChild_ResolveSuccessfully()
        {
            var prefabPath = EnsureTempPrefabAsset();
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefabAsset, Is.Not.Null);
            Assert.That(prefabAsset.transform.childCount, Is.EqualTo(1));
            var childGameObject = prefabAsset.transform.GetChild(0).gameObject;
            var childLocator = GameObjectLocatorFormatter.GetLocator(childGameObject);

            var rootSuccess = GameObjectLocatorResolver.TryResolve(prefabPath, out var prefabRoot, out var rootFailure);
            var childSuccess = GameObjectLocatorResolver.TryResolve(childLocator, out var prefabChild, out var childFailure);

            Assert.That(rootSuccess, Is.True);
            Assert.That(rootFailure, Is.Null);
            Assert.That(prefabRoot, Is.EqualTo(prefabAsset));
            Assert.That(childSuccess, Is.True);
            Assert.That(childFailure, Is.Null);
            Assert.That(prefabChild, Is.EqualTo(childGameObject));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_OmittedLocator_UsesActiveSelection()
        {
            var sceneObject = CreateTempComponentHost("SelectionDefaultHost");
            sceneObject.AddComponent<Camera>();
            Selection.activeObject = sceneObject;
            var tool = new UnityGameObjectComponentInfoTool();

            var result = tool.Execute(CreateContext("agb.components.005", "{}"), NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"component_list\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ComponentQueryMiss_ReturnsSuccessWithZeroMatches()
        {
            var sceneObject = CreateTempComponentHost("ComponentMissHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);

            var result = tool.Execute(
                CreateContext("agb.components.006", "{\"locator\":\"" + locator + "\",\"componentName\":\"MissingComponent\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"matchedCount\":0"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ComponentIndexOutOfRange_ReturnsInvalidArgs()
        {
            var sceneObject = CreateTempComponentHost("ComponentIndexHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);

            var result = tool.Execute(
                CreateContext("agb.components.007", "{\"locator\":\"" + locator + "\",\"componentIndex\":999}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors[0].code, Is.EqualTo("AGENTBRIDGE_COMPONENT_INDEX_OUT_OF_RANGE"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_NameMatchingAndRepeatedComponents_AreStable()
        {
            var sceneObject = CreateTempComponentHost("RepeatedCameraHost");
            sceneObject.AddComponent<Camera>();
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);
            var components = sceneObject.GetComponents<Component>();
            var secondCameraIndex = Array.FindLastIndex(components, component => component is Camera);
            Assume.That(secondCameraIndex, Is.GreaterThanOrEqualTo(0));

            var result = tool.Execute(
                CreateContext("agb.components.007.repeated", "{\"locator\":\"" + locator + "\",\"componentName\":\"UnityEngine.Camera\",\"componentIndex\":" + secondCameraIndex + ",\"propertyLimit\":0}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"matchedCount\":1"));
            Assert.That(report, Does.Contain("\"index\":" + secondCameraIndex));
            Assert.That(report, Does.Contain("\"type\":\"UnityEngine.Camera\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_148")]
        public void UnityGameObjectComponentInfoTool_ComponentIndex_UsesInspectModeAndReportFirstGuidance()
        {
            var sceneObject = CreateTempComponentHost("ComponentIndexInspectHost");
            sceneObject.AddComponent<Camera>();
            var second = sceneObject.AddComponent<Light>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);
            var components = sceneObject.GetComponents<Component>();
            var lightIndex = Array.FindIndex(components, component => component == second);

            var result = tool.Execute(
                CreateContext("agb.components.007.inspect", "{\"locator\":\"" + locator + "\",\"componentIndex\":" + lightIndex + ",\"propertyLimit\":1}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"component_inspect\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"componentIndex\":" + lightIndex));
            Assert.That(result.metricsObjectJson, Does.Contain("\"matchedCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"details\":{\"available\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedRead\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"followUp\":{\"recommended\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"tool\":\"unity.read_report\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"reportPath\":\"" + result.reportPath + "\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"jsonPointer\":\"/components\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_MissingScriptSlot_DoesNotAbortListing()
        {
            var prefabPath = EnsureTempMissingScriptPrefabAsset();
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefabAsset, Is.Not.Null);
            Assert.That(prefabAsset.GetComponents<Component>(), Has.Some.Null);

            var tool = new UnityGameObjectComponentInfoTool();
            var result = tool.Execute(
                CreateContext("agb.components.007.missing", "{\"locator\":\"" + prefabPath + "\"}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"mode\":\"component_list\""));
            Assert.That(report, Does.Contain("\"name\":\"Missing Script\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_SerializedMode_AndStringLimit_AreApplied()
        {
            var tempObject = CreateTempComponentHost("LongStringComponent");
            var component = tempObject.AddComponent<TestStringComponent>();
            component.text = new string('x', 40);
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            var result = tool.Execute(
                CreateContext("agb.components.008", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestStringComponent\",\"propertyMode\":\"serialized\",\"stringMaxLength\":16}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"propertyMode\":\"serialized\""));
            Assert.That(report, Does.Contain("\"path\":\""));
            Assert.That(report, Does.Contain("\"propertyType\":\""));
            Assert.That(report, Does.Contain("\"type\":\""));
            Assert.That(report, Does.Contain("\"isUnityObject\":false"));
            Assert.That(report, Does.Contain("\"isNull\":false"));
            Assert.That(report, Does.Contain("\"isContainer\":false"));
            Assert.That(report, Does.Contain("\"value\":\"" + new string('x', 16) + "\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_PropertyLimitZero_PreservesCountsAndMarksTruncation()
        {
            var sceneObject = CreateTempComponentHost("PropertyLimitHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);
            var result = tool.Execute(
                CreateContext("agb.components.009", "{\"locator\":\"" + locator + "\",\"componentName\":\"Camera\",\"propertyLimit\":0}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"propertyCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedPropertyCount\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"truncated\":true"));
            Assert.That(report, Does.Contain("\"properties\":[]"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ArrayElementLimitZero_KeepsArraySize()
        {
            var tempObject = CreateTempComponentHost("ArrayComponent");
            var component = tempObject.AddComponent<TestIntArrayComponent>();
            component.values = new[] { 1, 2, 3 };
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);
            var result = tool.Execute(
                CreateContext("agb.components.010", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestIntArrayComponent\",\"arrayElementLimit\":0}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(report, Does.Contain("\"isContainer\":true"));
            Assert.That(report, Does.Contain("\"value\":null"));
            Assert.That(report, Does.Contain("\"values.Array.size\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_DefaultPropertyLimits_AreApplied()
        {
            var tempObject = CreateTempComponentHost("DefaultLimitsComponent");
            var component = tempObject.AddComponent<TestStringComponent>();
            component.text = new string('x', 400);
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            var result = tool.Execute(
                CreateContext("agb.components.011", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestStringComponent\"}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"propertyMode\":\"debug\""));
            Assert.That(report, Does.Contain("\"value\":\"" + new string('x', AssetQueryContract.DefaultStringMaxLength) + "\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_DefaultArrayElementLimit_TruncatesArrayEntries()
        {
            var tempObject = CreateTempComponentHost("DefaultArrayLimitComponent");
            var component = tempObject.AddComponent<TestIntArrayComponent>();
            component.values = CreateSequentialArray(25);
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            var result = tool.Execute(
                CreateContext("agb.components.011.array", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestIntArrayComponent\",\"propertyMode\":\"serialized\"}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"truncated\":true"));
            Assert.That(report, Does.Contain("Array.data[19]"));
            Assert.That(report, Does.Not.Contain("Array.data[20]"));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_OverMaxArrayAndStringLimits_ReturnInvalidArgs()
        {
            var tool = new UnityGameObjectComponentInfoTool();

            var propertyLimitResult = tool.Execute(CreateContext("agb.components.012.property", "{\"propertyLimit\":1001}"), NoOpAgentCancellation.Instance);
            var arrayLimitResult = tool.Execute(CreateContext("agb.components.012", "{\"arrayElementLimit\":201}"), NoOpAgentCancellation.Instance);
            var stringLimitResult = tool.Execute(CreateContext("agb.components.013", "{\"stringMaxLength\":4001}"), NoOpAgentCancellation.Instance);

            Assert.That(propertyLimitResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(propertyLimitResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_PROPERTY_LIMIT_INVALID"));
            Assert.That(arrayLimitResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(arrayLimitResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_ARRAY_ELEMENT_LIMIT_INVALID"));
            Assert.That(stringLimitResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(stringLimitResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_STRING_MAX_LENGTH_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoArgs_Defaults_MatchContract()
        {
            var success = AssetQueryJson.TryDeserializeArgs<UnityGetGameObjectComponentInfoArgs>("{}", out var args, out var failure);

            Assert.That(success, Is.True, failure != null ? failure.summary : string.Empty);
            Assert.That(args, Is.Not.Null);
            Assert.That(args.propertyMode, Is.EqualTo(AssetQueryContract.DefaultPropertyMode));
            Assert.That(args.propertyLimit, Is.EqualTo(AssetQueryContract.DefaultPropertyLimit));
            Assert.That(args.arrayElementLimit, Is.EqualTo(AssetQueryContract.DefaultArrayElementLimit));
            Assert.That(args.stringMaxLength, Is.EqualTo(AssetQueryContract.DefaultStringMaxLength));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ExplicitDebugMode_IsAccepted()
        {
            var sceneObject = CreateTempComponentHost("ExplicitDebugModeHost");
            sceneObject.AddComponent<Camera>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(sceneObject);

            var result = tool.Execute(
                CreateContext("agb.components.013.debug", "{\"locator\":\"" + locator + "\",\"componentName\":\"Camera\",\"propertyMode\":\"debug\",\"propertyLimit\":0}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"propertyMode\":\"debug\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ObjectReference_IncludesReferenceIdentity()
        {
            var tempObject = CreateTempComponentHost("ObjectReferenceComponent");
            var component = tempObject.AddComponent<TestObjectReferenceComponent>();
            component.reference = AssetDatabase.LoadMainAssetAtPath("Assets/Scenes/AppMain.unity");
            Assume.That(component.reference, Is.Not.Null);
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            var result = tool.Execute(
                CreateContext("agb.components.013.objectref", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestObjectReferenceComponent\",\"propertyMode\":\"serialized\"}"),
                NoOpAgentCancellation.Instance);
            var report = File.ReadAllText(GetReportAbsolutePath(result.reportPath));

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(report, Does.Contain("\"isUnityObject\":true"));
            Assert.That(report, Does.Contain("\"isNull\":false"));
            Assert.That(report, Does.Contain("\"isDestroyed\":false"));
            Assert.That(report, Does.Contain("\"path\":\"Assets/Scenes/AppMain.unity\""));
            Assert.That(report, Does.Contain("\"guid\":\""));
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_Cancellation_IsObserved()
        {
            var tempObject = CreateTempComponentHost("CancellationComponent");
            tempObject.AddComponent<TestStringComponent>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            Assert.Throws<OperationCanceledException>(() =>
                tool.Execute(
                    CreateContext("agb.components.014.cancel", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestStringComponent\"}"),
                    new ThrowingAgentCancellation()));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void UnityGameObjectComponentInfoTool_ReadOnlyResults_LeaveChangedFilesEmpty()
        {
            var tempObject = CreateTempComponentHost("ChangedFilesHost");
            tempObject.AddComponent<TestStringComponent>();
            var tool = new UnityGameObjectComponentInfoTool();
            var locator = GameObjectLocatorFormatter.GetLocator(tempObject);

            var result = tool.Execute(
                CreateContext("agb.components.015", "{\"locator\":\"" + locator + "\",\"componentName\":\"TestStringComponent\",\"propertyLimit\":0}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.changedFiles, Is.Empty);
            TrackReport(result.reportPath);
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_149")]
        public void UnityReadReportTool_ArrayPaging_ReturnsGovernedMetricsAndSlice()
        {
            var hierarchyResult = new UnityGetHierarchyTool().Execute(
                CreateContext("agb.readreport.001.source", "{\"locator\":\"currentScene\"}"),
                NoOpAgentCancellation.Instance);
            TrackReport(hierarchyResult.reportPath);

            var tool = new UnityReadReportTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.readreport.001",
                    "{\"reportPath\":\"" + hierarchyResult.reportPath + "\",\"jsonPointer\":\"/result/nodes\",\"offset\":0,\"limit\":1,\"maxBytes\":65536}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"contractVersion\":\"report_read.v1\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"reportPath\":\"" + hierarchyResult.reportPath + "\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"jsonPointer\":\"/result/nodes\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"selectedIsArray\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"totalCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"items\":["));
            Assert.That(result.metricsObjectJson, Does.Contain("\"value\":null"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_149")]
        public void UnityReadReportTool_NonArraySelection_UsesValueAndNoPagingCounts()
        {
            var hierarchyResult = new UnityGetHierarchyTool().Execute(
                CreateContext("agb.readreport.002.source", "{\"locator\":\"currentScene\"}"),
                NoOpAgentCancellation.Instance);
            TrackReport(hierarchyResult.reportPath);

            var tool = new UnityReadReportTool();
            var result = tool.Execute(
                CreateContext(
                    "agb.readreport.002",
                    "{\"reportPath\":\"" + hierarchyResult.reportPath + "\",\"jsonPointer\":\"/target\",\"maxBytes\":65536}"),
                NoOpAgentCancellation.Instance);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"selectedIsArray\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"returnedCount\":null"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"totalCount\":null"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"nextOffset\":null"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"items\":null"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"value\":{"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_149")]
        public void UnityReadReportTool_UnsafeAndInvalidPaths_ReturnInvalidArgs()
        {
            var tool = new UnityReadReportTool();

            var absolutePathResult = tool.Execute(
                CreateContext("agb.readreport.003.abs", "{\"reportPath\":\"C:/temp/not-allowed.json\"}"),
                NoOpAgentCancellation.Instance);
            var traversalResult = tool.Execute(
                CreateContext("agb.readreport.003.parent", "{\"reportPath\":\"Temp/AgentBridge/reports/../outside.json\"}"),
                NoOpAgentCancellation.Instance);
            var extensionResult = tool.Execute(
                CreateContext("agb.readreport.003.ext", "{\"reportPath\":\"Temp/AgentBridge/reports/not-json.txt\"}"),
                NoOpAgentCancellation.Instance);

            Assert.That(absolutePathResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(absolutePathResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_REPORT_PATH_ABSOLUTE"));
            Assert.That(traversalResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(traversalResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_REPORT_PATH_OUTSIDE_ROOT"));
            Assert.That(extensionResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(extensionResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_REPORT_PATH_EXTENSION_INVALID"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_149")]
        public void UnityReadReportTool_NonReportJsonAndOversizeSlice_ReturnGovernedFailures()
        {
            var settings = AgentBridgeSettingsLoader.Load().Settings ?? AgentBridgeSettingsLoader.CreateDefaultSettings();
            var nonReportPath = AgentBridgeReportWriter.WriteReport(settings, "agb.readreport.004.fake", "manual", new ManualReportPayload { hello = "world" });
            TrackReport(nonReportPath);

            var hierarchyResult = new UnityGetHierarchyTool().Execute(
                CreateContext("agb.readreport.004.source", "{\"locator\":\"currentScene\"}"),
                NoOpAgentCancellation.Instance);
            TrackReport(hierarchyResult.reportPath);

            var tool = new UnityReadReportTool();
            var shapeResult = tool.Execute(
                CreateContext("agb.readreport.004.shape", "{\"reportPath\":\"" + nonReportPath + "\"}"),
                NoOpAgentCancellation.Instance);
            var sizeResult = tool.Execute(
                CreateContext(
                    "agb.readreport.004.size",
                    "{\"reportPath\":\"" + hierarchyResult.reportPath + "\",\"jsonPointer\":\"/result/nodes\",\"limit\":1,\"maxBytes\":32}"),
                NoOpAgentCancellation.Instance);

            Assert.That(shapeResult.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(shapeResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_REPORT_SHAPE_INVALID"));
            Assert.That(sizeResult.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(sizeResult.errors[0].code, Is.EqualTo("AGENTBRIDGE_REPORT_SLICE_TOO_LARGE"));
            Assert.That(sizeResult.metricsObjectJson, Does.Contain("\"maxBytes\":32"));
            Assert.That(sizeResult.metricsObjectJson, Does.Contain("\"jsonPointer\":\"/result/nodes\""));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_149")]
        public void UnityReadReportTool_Source_StaysReadOnly()
        {
            var content = File.ReadAllText(GetPackageRelativePath("Editor/Tools/UnityReadReportTool.cs"));
            Assert.That(content, Does.Not.Contain("AssetDatabase."));
            Assert.That(content, Does.Not.Contain("Selection."));
            Assert.That(content, Does.Not.Contain("CompilationPipeline."));
            Assert.That(content, Does.Not.Contain("EditorSceneManager."));
            Assert.That(content, Does.Not.Contain("GetComponents<"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_148")]
        public void UnityCompileOperationManager_BuildCompileResult_CleanSuccessAvoidsConsoleFollowUp()
        {
            var state = new CompileOperationState
            {
                commandId = "agb.compile.001",
                tool = "unity.compile",
                projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                tempRoot = "Temp/AgentBridge",
                startedAt = "2026-06-15T00:00:00.0000000Z",
                targetEpoch = 4,
                completedEpoch = 4,
                lifecycleStage = "finished",
                lastTransition = "compile_finished",
                lastTransitionAtUtc = "2026-06-15T00:00:01.0000000Z"
            };
            var lifecycle = new CompileLifecycleState { compileEpoch = 4 };
            var result = InvokeCompileResultBuilder(state, lifecycle);

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.metricsObjectJson, Does.Contain("\"contractVersion\":\"compile.v1\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"errorCount\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"warningCount\":0"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"details\":{\"available\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedRead\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"followUp\":{\"recommended\":false,\"options\":[]}"));
            Assert.That(result.metricsObjectJson, Does.Not.Contain("unity.get_console"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_148")]
        public void UnityCompileOperationManager_BuildCompileResult_FailurePrefersReadReport()
        {
            var state = new CompileOperationState
            {
                commandId = "agb.compile.002",
                tool = "unity.compile",
                projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                tempRoot = "Temp/AgentBridge",
                startedAt = "2026-06-15T00:00:00.0000000Z",
                targetEpoch = 5,
                completedEpoch = 5,
                lifecycleStage = "finished",
                lastTransition = "compile_finished",
                lastTransitionAtUtc = "2026-06-15T00:00:01.0000000Z",
                warningCount = 1,
                compilerErrors = new List<CompilerErrorRecord>
                {
                    new CompilerErrorRecord
                    {
                        code = "CS1001",
                        message = "Example compile error.",
                        file = "Assets/Test.cs",
                        line = 12,
                        column = 3
                    }
                },
                compilerWarnings = new List<CompilerWarningRecord>
                {
                    new CompilerWarningRecord
                    {
                        code = "CS0168",
                        message = "Variable declared but never used.",
                        file = "Assets/Test.cs",
                        line = 8,
                        column = 1
                    }
                }
            };

            var result = InvokeCompileResultBuilder(state, new CompileLifecycleState { compileEpoch = 5 });

            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Failed));
            Assert.That(result.metricsObjectJson, Does.Contain("\"errorCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"warningCount\":1"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"diagnosticSampleCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"recommendedRead\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"followUp\":{\"recommended\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"tool\":\"unity.read_report\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"args\":{}"));
            Assert.That(result.errors, Has.Count.EqualTo(1));
            Assert.That(result.errors[0].code, Is.EqualTo("CS1001"));
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void AssetQueryToolSources_DoNotInvokeMutationApis()
        {
            var sourceFiles = new[]
            {
                "Editor/Tools/UnityAssetDatabaseSearchTool.cs",
                "Editor/Tools/UnitySelectionInfoTool.cs",
                "Editor/Tools/UnityGameObjectComponentInfoTool.cs",
                "Editor/Tools/AssetQueries/AssetQueryResultBuilder.cs",
                "Editor/Tools/AssetQueries/AssetQueryReportBuilder.cs",
                "Editor/Tools/AssetQueries/AssetQueryPathValidator.cs",
                "Editor/Tools/AssetQueries/GameObjectLocatorFormatter.cs",
                "Editor/Tools/AssetQueries/GameObjectLocatorResolver.cs",
                "Editor/Tools/AssetQueries/SerializedComponentSampler.cs",
                "BuiltInPlugins/EditorBasics/Editor/EditorBasicsProvider.cs",
                "Editor/Tools/UnityGetHierarchyTool.cs",
                "Editor/Tools/SceneQueries/SceneQueryReportBuilder.cs",
                "Editor/Tools/SceneQueries/SceneQueryJson.cs",
                "Editor/Tools/SceneQueries/SceneQueryDtos.cs",
                "Editor/Tools/SceneQueries/SceneQueryContract.cs",
                "Editor/Tools/SceneQueries/HierarchyTargetResolver.cs"
            };

            foreach (var relativeFile in sourceFiles)
            {
                var content = File.ReadAllText(GetPackageRelativePath(relativeFile));
                Assert.That(content, Does.Not.Contain("AssetDatabase.Refresh("), relativeFile);
                Assert.That(content, Does.Not.Contain("AssetDatabase.SaveAssets("), relativeFile);
                Assert.That(content, Does.Not.Contain("EditorSceneManager.OpenScene("), relativeFile);
            }
        }

        [Test]
        [Category("AGB_ReadOnly")]
        public void SceneOpenToolSources_AvoidInteractiveSavePromptApis()
        {
            var sourceFiles = new[]
            {
                "Editor/Tools/UnityOpenSceneTool.cs",
                "Editor/Tools/SceneQueries/EditorStateSnapshotBuilder.cs",
                "Editor/Tools/SceneQueries/SceneQueryReportBuilder.cs"
            };

            foreach (var relativeFile in sourceFiles)
            {
                var content = File.ReadAllText(GetPackageRelativePath(relativeFile));
                Assert.That(content, Does.Not.Contain("SaveModifiedScenesIfUserWantsTo("), relativeFile);
            }
        }

        private AgentToolContext CreateContext(string commandId, string rawArgsJson)
        {
            var settingsLoad = AgentBridgeSettingsLoader.Load();
            var settings = settingsLoad.Settings ?? AgentBridgeSettingsLoader.CreateDefaultSettings();
            if (!AssetDatabase.Contains(settings))
            {
                settings.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }

            return new AgentToolContext
            {
                Command = new AgentCommand
                {
                    schemaVersion = "1.0",
                    commandId = commandId,
                    tool = "test",
                    timeoutMs = 5000,
                    createdAt = "2026-06-05T10:00:00Z",
                    rawArgsJson = rawArgsJson
                },
                RawArgsJson = rawArgsJson,
                Settings = settings
            };
        }

        private static UnityMcpToolContext CreatePluginContext(string commandId, string toolName, string rawArgsJson)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return new UnityMcpToolContext
            {
                CommandId = commandId,
                ToolName = toolName,
                TimeoutMs = 5000,
                RawArgsJson = rawArgsJson,
                ProjectRoot = projectRoot,
                TempRoot = "Temp/AgentBridge"
            };
        }

        private void TrackReport(string reportPath)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                _reportPaths.Add(reportPath);
            }
        }

        private static string SerializeErrors(ToolResult result)
        {
            return result?.errors == null ? "[]" : JsonUtility.ToJson(new ToolErrorListWrapper { errors = result.errors.ToArray() });
        }

        private static ToolResult InvokeCompileResultBuilder(CompileOperationState state, CompileLifecycleState lifecycle)
        {
            var method = typeof(UnityCompileOperationManager).GetMethod("BuildCompileResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (ToolResult)method.Invoke(null, new object[] { state, lifecycle });
        }

        private static string GetReportAbsolutePath(string reportPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetPackageRelativePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge", relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetProjectRelativePath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private string EnsureAssetQueryTestFolder()
        {
            const string folder = "Assets/AssetQueryTemp";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "AssetQueryTemp");
                _assetPathsToDelete.Add(folder);
            }

            return folder;
        }

        private string EnsureTempSceneAsset(string sceneName)
        {
            var folder = EnsureAssetQueryTestFolder();
            var scenePath = folder + "/" + sceneName + ".unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(scenePath) != null || File.Exists(GetProjectRelativePath(scenePath)))
                {
                    AssetDatabase.DeleteAsset(scenePath);
                }

                Assert.That(
                    AssetDatabase.CopyAsset("Assets/Scenes/AppMain.unity", scenePath),
                    Is.True,
                    "Expected Unity to create a copied scene asset fixture at " + scenePath + ".");
                if (!_assetPathsToDelete.Contains(scenePath))
                {
                    _assetPathsToDelete.Add(scenePath);
                }
            }

            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath),
                Is.Not.Null,
                "Expected copied scene asset fixture to resolve at " + scenePath + ".");

            return scenePath;
        }

        private string EnsureTempPrefabAsset()
        {
            var folder = EnsureAssetQueryTestFolder();
            var prefabPath = folder + "/LocatorPrefab.prefab";
            if (!File.Exists(GetProjectRelativePath(prefabPath)))
            {
                var root = new GameObject("PrefabRoot");
                new GameObject("Child").transform.SetParent(root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                _assetPathsToDelete.Add(prefabPath);
            }

            return prefabPath;
        }

        private string EnsureTempMissingScriptPrefabAsset()
        {
            var folder = EnsureAssetQueryTestFolder();
            var prefabPath = folder + "/MissingScriptPrefab.prefab";
            if (!File.Exists(GetProjectRelativePath(prefabPath)))
            {
                var root = new GameObject("MissingScriptRoot");
                root.AddComponent<TestStringComponent>().text = "orphan";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);

                var absolutePath = GetProjectRelativePath(prefabPath);
                var yaml = File.ReadAllText(absolutePath);
                var scriptReference = new Regex(@"m_Script:\s*\{fileID:\s*\d+,\s*guid:\s*[0-9a-f]{32},\s*type:\s*3\}", RegexOptions.CultureInvariant);
                yaml = scriptReference.Replace(yaml, "m_Script: {fileID: 11500000, guid: 00000000000000000000000000000000, type: 3}", 1);
                File.WriteAllText(absolutePath, yaml);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                _assetPathsToDelete.Add(prefabPath);
            }

            return prefabPath;
        }

        private GameObject CreateTempComponentHost(string name)
        {
            var gameObject = new GameObject(name);
            _runtimeObjectsToDestroy.Add(gameObject);
            return gameObject;
        }

        private static IEnumerator WaitForEditorIdle()
        {
            const int maxFrames = 120;
            for (var frame = 0; frame < maxFrames; frame++)
            {
                if (!EditorApplication.isCompiling &&
                    !EditorApplication.isUpdating &&
                    !EditorApplication.isPlaying &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Editor did not reach idle state within the allotted frames.");
        }

        private static void WaitForEditorIdleSynchronously()
        {
            const int maxIterations = 240;
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                if (!EditorApplication.isCompiling &&
                    !EditorApplication.isUpdating &&
                    !EditorApplication.isPlaying &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                System.Threading.Thread.Sleep(25);
            }

            Assert.Fail("Editor did not reach idle state within the allotted wait.");
        }

        [Serializable]
        private sealed class ToolErrorListWrapper
        {
            public ToolError[] errors;
        }

        [Serializable]
        private sealed class ManualReportPayload
        {
            public string hello;
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static int[] CreateSequentialArray(int count)
        {
            var values = new int[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = index;
            }

            return values;
        }

        private sealed class TestStringComponent : MonoBehaviour
        {
            public string text;
        }

        private sealed class TestIntArrayComponent : MonoBehaviour
        {
            public int[] values;
        }

        private sealed class TestObjectReferenceComponent : MonoBehaviour
        {
            public UnityEngine.Object reference;
        }

        private sealed class AssetQueryDummyAsset : ScriptableObject
        {
        }

        private sealed class ThrowingAgentCancellation : IAgentCancellation
        {
            public bool IsCancellationRequested => true;

            public void ThrowIfCancellationRequested()
            {
                throw new OperationCanceledException("test cancellation");
            }
        }

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
