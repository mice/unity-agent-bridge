using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityMcp.BuiltInPlugins.RoslynExecution;

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
    }
}
