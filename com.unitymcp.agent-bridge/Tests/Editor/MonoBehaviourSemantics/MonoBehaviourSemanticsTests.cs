using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityMcp.AgentBridge;
using UnityMcp.BuiltInPlugins.MonoBehaviourSemantics;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class MonoBehaviourSemanticsTests
    {
        private const string PrefabPath = "Assets/TempAgentBridgeMonoSemanticsTest.prefab";
        private const string ScriptGuid = "1234567890abcdef1234567890abcdef";

        [SetUp]
        public void SetUp()
        {
            var absolutePrefabPath = ToAbsoluteProjectPath(PrefabPath);
            File.WriteAllText(
                absolutePrefabPath,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &100000\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n  - component: {fileID: 400000}\n  - component: {fileID: 200000}\n  m_Layer: 0\n  m_Name: SampleGuidUsagePrefab\n  m_TagString: Untagged\n  m_Icon: {fileID: 0}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n--- !u!4 &400000\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 100000}\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_Children: []\n  m_Father: {fileID: 0}\n  m_RootOrder: 0\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n--- !u!114 &200000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 100000}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: " + ScriptGuid + ", type: 3}\n  m_Name: \n  m_EditorClassIdentifier: \n");
        }

        [TearDown]
        public void TearDown()
        {
            var absolutePrefabPath = ToAbsoluteProjectPath(PrefabPath);
            if (File.Exists(absolutePrefabPath))
            {
                File.Delete(absolutePrefabPath);
            }

            var metaPath = absolutePrefabPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_171.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_171")]
        public void FindScriptGuidUsages_ByScriptGuid_ReturnsPrefabTextCandidateAndProviderMetadata()
        {
            var result = Execute("{\"scriptGuid\":\"" + ScriptGuid + "\",\"assetTypes\":[\"prefab\"],\"searchFolders\":[\"Assets\"],\"limit\":10}");

            Assert.That(result.success, Is.True);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.Success));
            Assert.That(result.changedFiles, Is.Empty);
            Assert.That(result.reportPath, Is.Not.Empty);

            Assert.That(result.metricsObjectJson, Does.Contain("\"usageCount\":"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"semanticValidation\":\"not_performed\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"id\":\"guid_text_scan\""));
            Assert.That(result.metricsObjectJson, Does.Contain("\"textMatches\":true"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"gameObjectPath\":false"));
            Assert.That(result.metricsObjectJson, Does.Contain("\"assetPath\":\"" + PrefabPath + "\""));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_171.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_171")]
        public void FindScriptGuidUsages_Schema_DeclaresTargetChoiceAndBoundedFilters()
        {
            var schema = MonoBehaviourSemanticsSchemas.FindScriptGuidUsages;

            Assert.That(schema, Does.Contain("\"$schema\":\"https://json-schema.org/draft/2020-12/schema\""));
            Assert.That(schema, Does.Contain("\"additionalProperties\":false"));
            Assert.That(schema, Does.Contain("\"oneOf\""));
            Assert.That(schema, Does.Contain("\"required\":[\"scriptGuid\"]"));
            Assert.That(schema, Does.Contain("\"required\":[\"scriptPath\"]"));
            Assert.That(schema, Does.Contain("\"required\":[\"typeName\"]"));
            Assert.That(schema, Does.Contain("\"not\":{\"anyOf\""));
            Assert.That(schema, Does.Contain("\"pattern\":\"^[A-Fa-f0-9]{32}$\""));
            Assert.That(schema, Does.Contain("\"pattern\":\"^Assets/.+\\\\.cs$\""));
            Assert.That(schema, Does.Contain("\"searchFolders\":{\"type\":\"array\",\"uniqueItems\":true"));
            Assert.That(schema, Does.Contain("\"pattern\":\"^Assets(?:/.*)?$\""));
            Assert.That(schema, Does.Contain("\"assetTypes\":{\"type\":\"array\",\"uniqueItems\":true"));
            Assert.That(schema, Does.Contain("\"maximum\":1000"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_172.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_172")]
        public void FindScriptGuidUsages_InvalidScriptPath_ReturnsInvalidArgs()
        {
            var result = Execute("{\"scriptPath\":\"Assets/DoesNotExist/Missing.cs\"}");

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors.Any(error => error.code == "AGENTBRIDGE_MONO_SCRIPT_META_MISSING"), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_173.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_173")]
        public void FindScriptGuidUsages_ConflictingTargets_ReturnsInvalidArgs()
        {
            var result = Execute("{\"scriptGuid\":\"" + ScriptGuid + "\",\"scriptPath\":\"Assets/SomeScript.cs\"}");

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors.Any(error => error.code == "AGENTBRIDGE_MONO_TARGET_INVALID"), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_174.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_174")]
        public void FindScriptGuidUsages_InvalidSearchFolder_DoesNotScanProject()
        {
            var result = Execute("{\"scriptGuid\":\"" + ScriptGuid + "\",\"searchFolders\":[\"Packages\"]}");

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors.Any(error => error.code == "AGENTBRIDGE_MONO_SEARCH_FOLDER_INVALID"), Is.True);
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_175.md
        [Test]
        [Category("AGB_MonoBehaviourSemantics")]
        [Category("AGB_175")]
        public void FindScriptGuidUsages_FindReference2ProviderUnavailable_ReturnsInvalidArgs()
        {
            var result = Execute("{\"scriptGuid\":\"" + ScriptGuid + "\",\"provider\":\"findreference2\"}");

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(ToolResultStatus.InvalidArgs));
            Assert.That(result.errors.Any(error => error.code == "AGENTBRIDGE_MONO_PROVIDER_UNAVAILABLE"), Is.True);
        }

        private static ToolResult Execute(string rawArgsJson)
        {
            var registry = new AgentToolRegistry();
            registry.Register(new UnityMcpPluginToolAdapter(new FindScriptGuidUsagesTool(), Directory.GetParent(Application.dataPath)?.FullName, "Temp/AgentBridge"));
            Assert.That(registry.TryGetTool("unity.mono.find_script_guid_usages", out var tool), Is.True);

            return tool.Execute(new AgentToolContext
            {
                Command = new AgentCommand
                {
                    commandId = "cmd-mono-semantics-test",
                    tool = "unity.mono.find_script_guid_usages",
                    timeoutMs = 10000
                },
                RawArgsJson = rawArgsJson
            }, NoOpAgentCancellation.Instance);
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
