using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityMcp.Plugin;
using UnityQueries = UnityMcp.BuiltInPlugins.UnityQueries;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class DontDestroyOnLoadHierarchyEditModeTests
    {
        // TestRecord: Packages/com.unitymcp.agent-bridge/Documentation~/test_records/AGB_193.md
        [Test]
        [Category("AGB_ReadOnly")]
        [Category("AGB_193")]
        public void SpecialLocators_AreRejectedOutsidePlayModeWithoutFallback()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);

            var rootsSuccess = UnityQueries.DontDestroyOnLoadHierarchy.TryGetRoots(
                out var roots,
                out var rootsFailure);

            var gameObjectSuccess = UnityQueries.GameObjectLocatorResolver.TryResolve(
                "dontDestroyOnLoad#Missing",
                out _,
                out var gameObjectFailure);
            var bareGameObjectSuccess = UnityQueries.GameObjectLocatorResolver.TryResolve(
                "dontDestroyOnLoad",
                out _,
                out var bareGameObjectFailure);
            var hierarchyTool = new UnityQueries.UnityGetHierarchyTool();
            var hierarchyResult = hierarchyTool.Execute(
                CreateContext("agb.ddol.193.hierarchy", "unity.get_hierarchy", "{\"locator\":\"dontDestroyOnLoad\"}"),
                NoOpUnityMcpCancellation.Instance);

            try
            {
                Assert.That(gameObjectSuccess, Is.False);
                Assert.That(rootsSuccess, Is.False);
                Assert.That(roots, Is.Empty);
                Assert.That(rootsFailure.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
                Assert.That(rootsFailure.Errors[0].Code, Is.EqualTo(UnityQueries.DontDestroyOnLoadHierarchy.NotAvailableErrorCode));
                Assert.That(gameObjectFailure.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
                Assert.That(gameObjectFailure.Errors[0].Code, Is.EqualTo(UnityQueries.DontDestroyOnLoadHierarchy.NotAvailableErrorCode));
                Assert.That(bareGameObjectSuccess, Is.False);
                Assert.That(bareGameObjectFailure.Errors[0].Code, Is.EqualTo("AGENTBRIDGE_LOCATOR_UNSUPPORTED"));
                Assert.That(hierarchyResult.Status, Is.EqualTo(UnityMcpToolStatus.InvalidArgs));
                Assert.That(hierarchyResult.Errors[0].Code, Is.EqualTo(UnityQueries.DontDestroyOnLoadHierarchy.NotAvailableErrorCode));
                Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            }
            finally
            {
                DeleteReport(hierarchyResult.ReportPath);
            }
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

        private static void DeleteReport(string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var absolutePath = Path.Combine(projectRoot, reportPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
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
