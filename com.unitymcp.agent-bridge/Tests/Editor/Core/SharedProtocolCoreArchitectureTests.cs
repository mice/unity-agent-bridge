using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class SharedProtocolCoreArchitectureTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_139.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_139")]
        public void SharedProtocolCore_AsmdefAndAssemblyRemainEngineIndependent()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var packageRoot = Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge");
            var asmdefPath = Path.Combine(packageRoot, "Runtime", "SharedProtocolCore", "UnityMcp.AgentBridge.SharedProtocolCore.asmdef");
            Assert.That(File.Exists(asmdefPath), Is.True, "SharedProtocolCore asmdef must exist.");

            var asmdefText = File.ReadAllText(asmdefPath);
            StringAssert.Contains("\"noEngineReferences\": true", asmdefText);

            var sharedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "UnityMcp.AgentBridge.SharedProtocolCore", StringComparison.Ordinal));
            Assert.That(sharedAssembly, Is.Not.Null, "SharedProtocolCore assembly must load in the Unity test domain.");

            var references = sharedAssembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references, Does.Not.Contain("UnityEditor"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_163.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_163")]
        public void PluginAbstractions_AndProjectInfoPlugin_DoNotReferenceSharedProtocolCore()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var packageRoot = Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge");
            var abstractionsAsmdefPath = Path.Combine(packageRoot, "Runtime", "PluginAbstractions", "UnityMcp.Plugin.Abstractions.asmdef");
            var projectInfoAsmdefPath = Path.Combine(packageRoot, "BuiltInPlugins", "ProjectInfo", "Editor", "UnityMcp.BuiltInPlugins.ProjectInfo.asmdef");

            Assert.That(File.Exists(abstractionsAsmdefPath), Is.True, "Plugin abstractions asmdef must exist.");
            Assert.That(File.Exists(projectInfoAsmdefPath), Is.True, "ProjectInfo plugin asmdef must exist.");

            var abstractionsAsmdefText = File.ReadAllText(abstractionsAsmdefPath);
            var projectInfoAsmdefText = File.ReadAllText(projectInfoAsmdefPath);

            Assert.That(abstractionsAsmdefText, Does.Not.Contain("UnityMcp.AgentBridge.SharedProtocolCore"));
            Assert.That(projectInfoAsmdefText, Does.Not.Contain("UnityMcp.AgentBridge.SharedProtocolCore"));
            Assert.That(projectInfoAsmdefText, Does.Not.Contain("UnityMcp.AgentBridge.Editor"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_165.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_165")]
        public void EditorBasicsPlugin_DoesNotReferenceAgentBridgeEditorOrSharedProtocolCore()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var packageRoot = Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge");
            var editorBasicsAsmdefPath = Path.Combine(packageRoot, "BuiltInPlugins", "EditorBasics", "Editor", "UnityMcp.BuiltInPlugins.EditorBasics.asmdef");

            Assert.That(File.Exists(editorBasicsAsmdefPath), Is.True, "EditorBasics plugin asmdef must exist.");

            var editorBasicsAsmdefText = File.ReadAllText(editorBasicsAsmdefPath);
            Assert.That(editorBasicsAsmdefText, Does.Not.Contain("UnityMcp.AgentBridge.Editor"));
            Assert.That(editorBasicsAsmdefText, Does.Not.Contain("UnityMcp.AgentBridge.SharedProtocolCore"));
        }

        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_167.md
        [Test]
        [Category("AGB_Core")]
        [Category("AGB_167")]
        public void EditorBasicsPlugin_SourceFilesRemainSplitByResponsibility()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var sourceRoot = Path.Combine(projectRoot, "Packages", "com.unitymcp.agent-bridge", "BuiltInPlugins", "EditorBasics", "Editor");

            Assert.That(File.Exists(Path.Combine(sourceRoot, "EditorBasicsProvider.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "Ping", "UnityPingTool.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "Console", "UnityConsoleLogTool.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "Console", "EditorBasicsConsoleLogStore.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "EditorState", "UnityGetEditorStateTool.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "EditorState", "EditorBasicsEditorStateSnapshotBuilder.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(sourceRoot, "Common", "EditorBasicsReportWriter.cs")), Is.True);

            foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                var lineCount = File.ReadAllLines(sourceFile).Length;
                Assert.That(lineCount, Is.LessThanOrEqualTo(300), sourceFile);
            }
        }
    }
}
