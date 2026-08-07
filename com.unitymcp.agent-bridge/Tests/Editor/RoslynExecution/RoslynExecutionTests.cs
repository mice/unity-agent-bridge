using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.BuiltInPlugins.RoslynExecution;
using UnityMcp.Plugin;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class RoslynExecutionTests
    {
        [Test]
        [Category("AGB_Core")]
        public void Validation_BlockedApi_ReturnsFalseWithBlockedToken()
        {
            var args = new ExecuteCSharpArgs
            {
                code = "return typeof(System.IO.File).FullName;",
                timeoutMs = 1000
            };

            var isValid = RoslynExecutionValidation.TryValidate(args, out var validationMessage);

            Assert.That(isValid, Is.False);
            Assert.That(validationMessage, Does.Contain("System.IO"));
        }

        [Test]
        [Category("AGB_Core")]
        public void PolicyResolution_OmittedRequestUsesConfiguredDefault()
        {
            var args = new ExecuteCSharpArgs
            {
                code = "return UnityEditor.Selection.activeObject;"
            };

            var resolved = RoslynExecutionValidation.TryResolvePolicy(
                args,
                RoslynExecutionContracts.PolicyQueryOnly,
                out var decision,
                out var message);

            Assert.That(resolved, Is.True, message);
            Assert.That(decision.Policy, Is.EqualTo(RoslynExecutionContracts.PolicyQueryOnly));
            Assert.That(decision.Source, Is.EqualTo("settings"));
        }

        [Test]
        [Category("AGB_Core")]
        public void PolicyResolution_RequestOverridesConfiguredDefault()
        {
            var args = new ExecuteCSharpArgs
            {
                code = "return 1;",
                executionPolicy = RoslynExecutionContracts.PolicyTrusted
            };

            var resolved = RoslynExecutionValidation.TryResolvePolicy(
                args,
                RoslynExecutionContracts.PolicyQueryOnly,
                out var decision,
                out var message);

            Assert.That(resolved, Is.True, message);
            Assert.That(decision.Policy, Is.EqualTo(RoslynExecutionContracts.PolicyTrusted));
            Assert.That(decision.Source, Is.EqualTo("request"));
        }

        [Test]
        [Category("AGB_Core")]
        public void PolicyResolution_UnknownPolicyIsRejected()
        {
            var args = new ExecuteCSharpArgs
            {
                code = "return 1;",
                executionPolicy = "unsafe_future_mode"
            };

            var resolved = RoslynExecutionValidation.TryResolvePolicy(
                args,
                RoslynExecutionContracts.PolicyTrusted,
                out var decision,
                out var message);

            Assert.That(resolved, Is.False);
            Assert.That(decision, Is.Null);
            Assert.That(message, Does.Contain("trusted"));
        }

        [Test]
        [Category("AGB_Core")]
        public void QueryOnlyValidation_AllowsCommonReadOnlyQuery()
        {
            var allowed = RoslynExecutionValidation.TryValidateQueryOnly(
                "return UnityEditor.AssetDatabase.FindAssets(\"t:Prefab\");",
                out var denial);

            Assert.That(allowed, Is.True);
            Assert.That(denial, Is.Null);
        }

        [Test]
        [Category("AGB_Core")]
        public void QueryOnlyValidation_DeniesProjectWrite()
        {
            var allowed = RoslynExecutionValidation.TryValidateQueryOnly(
                "UnityEditor.AssetDatabase.SaveAssets(); return null;",
                out var denial);

            Assert.That(allowed, Is.False);
            Assert.That(denial.DenialCategory, Is.EqualTo("project_write"));
            Assert.That(denial.MatchedOperation, Is.EqualTo("AssetDatabase.SaveAssets"));
            Assert.That(denial.Message, Does.Contain("query_only"));
        }

        [Test]
        [Category("AGB_RoslynQueryOnlyIntegration")]
        public void QueryOnlyExecution_ActiveSceneQuery_Succeeds()
        {
            var result = ExecuteQueryOnly(
                "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;",
                "agb.roslyn.query_only.active_scene");

            AssertQueryOnlyExecutionSucceeded(result, "active_scene");
        }

        [Test]
        [Category("AGB_RoslynQueryOnlyIntegration")]
        public void QueryOnlyExecution_HierarchyAndSelectionQuery_Succeeds()
        {
            var result = ExecuteQueryOnly(
                "return UnityEditor.Selection.activeObject == null ? \"none\" : UnityEditor.Selection.activeObject.name;",
                "agb.roslyn.query_only.selection");

            AssertQueryOnlyExecutionSucceeded(result, "selection");
        }

        [Test]
        [Category("AGB_RoslynQueryOnlyIntegration")]
        public void QueryOnlyExecution_AssetSearchAndDependenciesQuery_Succeeds()
        {
            var result = ExecuteQueryOnly(
                "return new { search = UnityEditor.AssetDatabase.FindAssets(\"t:Scene\").Length, dependencies = UnityEditor.AssetDatabase.GetDependencies(\"Assets/Scenes/AppMain.unity\").Length };",
                "agb.roslyn.query_only.asset_queries");

            AssertQueryOnlyExecutionSucceeded(result, "asset_queries");
        }

        [Test]
        [Category("AGB_RoslynQueryOnlyIntegration")]
        public void QueryOnlyExecution_SerializedInspectionQuery_Succeeds()
        {
            var result = ExecuteQueryOnly(
                "var target = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(\"Assets/Scenes/AppMain.unity\"); if (target == null) return \"missing\"; var serialized = new UnityEditor.SerializedObject(target); return serialized.targetObject.name;",
                "agb.roslyn.query_only.serialized_inspection");

            AssertQueryOnlyExecutionSucceeded(result, "serialized_inspection");
        }

        [Test]
        [Category("AGB_RoslynQueryOnlyIntegration")]
        public void QueryOnlyExecution_ProjectWriteIsRejectedBeforeCompiler()
        {
            var result = ExecuteQueryOnly(
                "UnityEditor.AssetDatabase.SaveAssets(); return null;",
                "agb.roslyn.query_only.project_write");

            Assert.That(result.Status, Is.EqualTo(RoslynExecutionContracts.PhaseValidationFailed));
            Assert.That(result.Errors, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Errors[0].Code, Is.EqualTo(RoslynExecutionContracts.PolicyDeniedCode));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"policyDenialCategory\":\"project_write\""));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"strategy\":\"not_started\""));
        }

        [Test]
        [Category("AGB_Core")]
        public void QueryOnlyValidation_IgnoresCommentsAndStringLiterals()
        {
            const string body = "// AssetDatabase.SaveAssets();\nreturn \"System.IO.File\";";

            var allowed = RoslynExecutionValidation.TryValidateQueryOnly(body, out var denial);

            Assert.That(allowed, Is.True);
            Assert.That(denial, Is.Null);
        }

        private static UnityMcpToolResult ExecuteQueryOnly(string code, string commandId)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            Assert.That(
                RoslynExecutionRuntimeState.TryResolveToolAvailability(projectRoot, out var availability),
                Is.True,
                "The prepared Roslyn compiler runtime must be available for integration coverage.");

            var tool = new UnityExecuteCSharpTool(availability);
            var context = new UnityMcpToolContext
            {
                CommandId = commandId,
                ToolName = "unity.execute_csharp",
                TimeoutMs = RoslynExecutionContracts.MaximumTimeoutMs,
                RawArgsJson = JsonUtility.ToJson(new ExecuteCSharpArgs
                {
                    code = code,
                    timeoutMs = RoslynExecutionContracts.MaximumTimeoutMs,
                    executionPolicy = RoslynExecutionContracts.PolicyQueryOnly
                }),
                ProjectRoot = projectRoot,
                TempRoot = "Temp/AgentBridge"
            };

            return tool.Execute(context, NoOpUnityMcpCancellation.Instance);
        }

        private static void AssertQueryOnlyExecutionSucceeded(UnityMcpToolResult result, string queryName)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Status, Is.EqualTo(UnityMcpToolStatus.Success), queryName + ": " + result.Summary);
            Assert.That(result.MetricsObjectJson, Is.Not.Null.And.Not.Empty);

            var metrics = JsonUtility.FromJson<RoslynExecutionMetrics>(result.MetricsObjectJson);
            Assert.That(metrics.success, Is.True);
            Assert.That(metrics.phase, Is.EqualTo(RoslynExecutionContracts.PhaseExecuted));
            Assert.That(metrics.executionPolicy, Is.EqualTo(RoslynExecutionContracts.PolicyQueryOnly));
            Assert.That(metrics.policySource, Is.EqualTo("request"));
            Assert.That(metrics.policyVersion, Is.EqualTo(RoslynExecutionContracts.PolicyVersion));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"result\""));
            Assert.That(result.ReportPath, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Category("AGB_Core")]
        public void Metrics_ApplyPolicy_EmitsPolicyMetadata()
        {
            var metrics = RoslynExecutionMetrics.CreateFailure(
                "exec_policy",
                "sha256:test",
                RoslynExecutionContracts.PhaseValidationFailed,
                "denied",
                null);
            metrics.ApplyPolicy(new RoslynExecutionPolicyDecision
            {
                Policy = RoslynExecutionContracts.PolicyQueryOnly,
                Source = "request",
                Version = RoslynExecutionContracts.PolicyVersion
            });

            Assert.That(metrics.executionPolicy, Is.EqualTo("query_only"));
            Assert.That(metrics.policySource, Is.EqualTo("request"));
            Assert.That(metrics.policyVersion, Is.EqualTo(RoslynExecutionContracts.PolicyVersion));
        }

        [Test]
        [Category("AGB_Core")]
        public void PolicyDeniedResult_UsesStableCodeAndNotStartedStages()
        {
            var result = RoslynExecutionResultFactory.PolicyDenied(
                null,
                new RoslynExecutionPolicyDecision
                {
                    Policy = RoslynExecutionContracts.PolicyQueryOnly,
                    Source = "request",
                    Version = RoslynExecutionContracts.PolicyVersion,
                    DenialCategory = "project_write",
                    MatchedOperation = "AssetDatabase.SaveAssets",
                    Message = "Query-only policy denied AssetDatabase.SaveAssets."
                },
                Directory.GetParent(Application.dataPath).FullName,
                "sha256:" + new string('a', 64));

            Assert.That(result.Status, Is.EqualTo(RoslynExecutionContracts.PhaseValidationFailed));
            Assert.That(result.Errors[0].Code, Is.EqualTo(RoslynExecutionContracts.PolicyDeniedCode));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"strategy\":\"not_started\""));
            Assert.That(result.MetricsObjectJson, Does.Contain("\"executionPolicy\":\"query_only\""));
        }

        [Test]
        [Category("AGB_Core")]
        public void BuildWrappedSource_InsertsMethodBodyWithoutAcceptingFullFileMode()
        {
            const string body = "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;";

            var wrapped = RoslynExecutionUtility.BuildWrappedSource(body);

            Assert.That(wrapped, Does.Contain("public static class Entry"));
            Assert.That(wrapped, Does.Contain("public static string Run()"));
            Assert.That(wrapped, Does.Contain("private static object __Run()"));
            Assert.That(wrapped, Does.Contain(body));
            Assert.That(wrapped, Does.Contain("RoslynExecutionRuntimeSerializer.SerializeSuccess"));
        }

        [Test]
        [Category("AGB_Core")]
        public void RuntimeSerializer_LongString_TruncatesAndMarksReason()
        {
            var raw = new string('x', RoslynExecutionContracts.MaxStringLength + 128);

            var envelope = RoslynExecutionRuntimeSerializer.SerializeSuccess(raw);

            Assert.That(envelope, Does.Contain("\"truncated\":true"));
            Assert.That(envelope, Does.Contain("\"truncationReason\":\"maxStringLength\""));
            Assert.That(envelope, Does.Contain("\"resultKind\":\"string\""));
            Assert.That(envelope, Does.Contain("\"error\":\"\""));
            Assert.That(envelope, Does.Not.Contain(new string('x', RoslynExecutionContracts.MaxStringLength + 16)));
        }

        [Test]
        [Category("AGB_Core")]
        public void RuntimeSerializer_LargeCollection_TruncatesAtCollectionLimit()
        {
            var values = new List<int>();
            for (var index = 0; index < RoslynExecutionContracts.MaxCollectionLength + 25; index++)
            {
                values.Add(index);
            }

            var envelope = RoslynExecutionRuntimeSerializer.SerializeSuccess(values);

            Assert.That(envelope, Does.Contain("\"truncated\":true"));
            Assert.That(envelope, Does.Contain("\"truncationReason\":\"maxCollectionLength\""));
            Assert.That(envelope, Does.Contain("\"resultKind\":\"array\""));
            Assert.That(envelope, Does.Contain("\"result\":[0,1,2"));
            Assert.That(envelope, Does.Not.Contain("," + (RoslynExecutionContracts.MaxCollectionLength + 5) + ","));
        }

        [Test]
        [Category("AGB_Core")]
        public void RuntimeSerializer_GameObject_ReturnsUnityObjectSummary()
        {
            var root = new GameObject("RoslynExecutionSummaryRoot");
            var child = new GameObject("RoslynExecutionSummaryChild");
            child.transform.SetParent(root.transform, false);

            try
            {
                var envelope = RoslynExecutionRuntimeSerializer.SerializeSuccess(child);

                Assert.That(envelope, Does.Contain("\"resultKind\":\"unity_object\""));
                Assert.That(envelope, Does.Contain("\"type\":\"" + typeof(GameObject).FullName + "\""));
                Assert.That(envelope, Does.Contain("\"name\":\"RoslynExecutionSummaryChild\""));
                Assert.That(envelope, Does.Contain("\"instanceId\":" + child.GetInstanceID()));
                Assert.That(envelope, Does.Contain("\"hierarchyPath\":\"RoslynExecutionSummaryRoot/RoslynExecutionSummaryChild\""));
                Assert.That(envelope, Does.Contain("\"assetPath\":null"));
                Assert.That(envelope, Does.Contain("\"scenePath\":"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(child);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Category("AGB_Core")]
        public void Metrics_CreateFailureForProxyFailure_PrefixesError()
        {
            var metrics = RoslynExecutionMetrics.CreateFailure(
                "exec_proxy_failure",
                "sha256:test",
                RoslynExecutionContracts.PhaseProxyFailed,
                "Compiler proxy returned empty stdout.",
                null);

            Assert.That(metrics.phase, Is.EqualTo(RoslynExecutionContracts.PhaseProxyFailed));
            Assert.That(metrics.error, Is.EqualTo("proxy_failed: Compiler proxy returned empty stdout."));
        }

        [Test]
        [Category("AGB_Core")]
        public void Metrics_CreateFailureForTimeout_PreservesTimeoutPhase()
        {
            var metrics = RoslynExecutionMetrics.CreateFailure(
                "exec_timeout",
                "sha256:test",
                RoslynExecutionContracts.PhaseTimeout,
                "Compilation timed out.",
                null);

            Assert.That(metrics.phase, Is.EqualTo(RoslynExecutionContracts.PhaseTimeout));
            Assert.That(metrics.error, Is.EqualTo("Compilation timed out."));
            Assert.That(metrics.result.kind, Is.EqualTo("null"));
        }

        [Test]
        [Category("AGB_Core")]
        public void ParseExecutionEnvelope_SerializationFailure_PreservesPhaseHints()
        {
            var envelope = RoslynExecutionUtility.ParseExecutionEnvelope(
                "{\"result\":null,\"error\":\"serialization_failed: bad serializer\",\"resultKind\":\"null\",\"truncated\":false,\"truncationReason\":null}");

            Assert.That(envelope.error, Is.EqualTo("serialization_failed: bad serializer"));
            Assert.That(envelope.resultKind, Is.EqualTo("null"));
            Assert.That(envelope.truncated, Is.False);
        }

        [Test]
        [Category("AGB_Core")]
        public void ParseExecutionEnvelope_MissingRequiredObject_ThrowsLoadFailureStyleException()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                RoslynExecutionUtility.ParseExecutionEnvelope("[1,2,3]"));

            Assert.That(exception.Message, Does.Contain("non-object JSON payload"));
        }

        [Test]
        [Category("AGB_Core")]
        public void ParseExecutionEnvelope_EmptyPayload_ThrowsLoadFailureStyleException()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                RoslynExecutionUtility.ParseExecutionEnvelope(string.Empty));

            Assert.That(exception.Message, Does.Contain("empty payload"));
        }

        public static void RunQueryOnlyIntegrationBatchmode()
        {
            var report = new QueryOnlyIntegrationReport
            {
                unityVersion = Application.unityVersion,
                tests = new List<QueryOnlyIntegrationResult>()
            };
            var testInstance = new RoslynExecutionTests();
            var cases = new[]
            {
                new QueryOnlyIntegrationCase("active_scene", () => testInstance.QueryOnlyExecution_ActiveSceneQuery_Succeeds()),
                new QueryOnlyIntegrationCase("selection", () => testInstance.QueryOnlyExecution_HierarchyAndSelectionQuery_Succeeds()),
                new QueryOnlyIntegrationCase("asset_queries", () => testInstance.QueryOnlyExecution_AssetSearchAndDependenciesQuery_Succeeds()),
                new QueryOnlyIntegrationCase("serialized_inspection", () => testInstance.QueryOnlyExecution_SerializedInspectionQuery_Succeeds()),
                new QueryOnlyIntegrationCase("rejected_project_write", () => testInstance.QueryOnlyExecution_ProjectWriteIsRejectedBeforeCompiler())
            };

            foreach (var testCase in cases)
            {
                var startedAt = DateTime.UtcNow;
                try
                {
                    testCase.Run();
                    report.tests.Add(new QueryOnlyIntegrationResult
                    {
                        name = testCase.Name,
                        outcome = "passed",
                        durationMs = Math.Max(1L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds),
                        error = string.Empty
                    });
                }
                catch (Exception exception)
                {
                    report.tests.Add(new QueryOnlyIntegrationResult
                    {
                        name = testCase.Name,
                        outcome = "failed",
                        durationMs = Math.Max(1L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds),
                        error = (exception.InnerException ?? exception).Message
                    });
                }
            }

            var reportPath = Environment.GetEnvironmentVariable("AGB_ROSLYN_QUERY_ONLY_REPORT");
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                reportPath = Path.GetFullPath("Temp/RoslynQueryOnlyIntegrationReport.json");
            }

            reportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? string.Empty);
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Debug.Log("Roslyn query-only integration report: " + reportPath);

            var failures = report.tests.FindAll(item => string.Equals(item.outcome, "failed", StringComparison.Ordinal));
            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Roslyn query-only integration failures: " + JsonUtility.ToJson(failures));
            }
        }

        [Serializable]
        private sealed class QueryOnlyIntegrationReport
        {
            public string unityVersion;
            public List<QueryOnlyIntegrationResult> tests;
        }

        [Serializable]
        private sealed class QueryOnlyIntegrationResult
        {
            public string name;
            public string outcome;
            public long durationMs;
            public string error;
        }

        private sealed class QueryOnlyIntegrationCase
        {
            public QueryOnlyIntegrationCase(string name, Action run)
            {
                Name = name;
                Run = run;
            }

            public string Name { get; }
            public Action Run { get; }
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
