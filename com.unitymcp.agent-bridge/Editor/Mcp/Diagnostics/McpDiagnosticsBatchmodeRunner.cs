using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public static class McpDiagnosticsBatchmodeRunner
    {
        private const string ResultsArg = "-emcpP4ResultsPath";
        private const string CategoryName = "AGBM_P4";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void RunP4EditModeTests()
        {
            var resultsPath = GetRequiredCommandLineValue(ResultsArg);
            var resultsDirectory = Path.GetDirectoryName(resultsPath);
            if (!string.IsNullOrWhiteSpace(resultsDirectory))
            {
                Directory.CreateDirectory(resultsDirectory);
            }

            var callbacks = ScriptableObject.CreateInstance<BatchmodeCallbacks>();
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                api.RegisterCallbacks(callbacks);
                var settings = new ExecutionSettings(new Filter
                {
                    testMode = TestMode.EditMode,
                    categoryNames = new[] { CategoryName },
                })
                {
                    runSynchronously = true,
                };

                api.Execute(settings);
                if (!callbacks.Finished)
                {
                    throw new InvalidOperationException("AGBM_P4 EditMode run did not finish.");
                }

                WriteResultsXml(resultsPath, callbacks.Results);
            }
            finally
            {
                api.UnregisterCallbacks(callbacks);
                UnityEngine.Object.DestroyImmediate(callbacks);
                UnityEngine.Object.DestroyImmediate(api);
            }
        }

        private static string GetRequiredCommandLineValue(string argName)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (!string.Equals(args[index], argName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = args[index + 1];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            throw new ArgumentException("Missing required command line argument: " + argName, argName);
        }

        private static void WriteResultsXml(string path, IReadOnlyList<TestCaseResultRecord> results)
        {
            var total = results.Count;
            var passed = results.Count(item => string.Equals(item.Outcome, "Passed", StringComparison.OrdinalIgnoreCase));
            var failed = results.Count(item => string.Equals(item.Outcome, "Failed", StringComparison.OrdinalIgnoreCase));
            var skipped = results.Count(item => string.Equals(item.Outcome, "Skipped", StringComparison.OrdinalIgnoreCase));
            var inconclusive = results.Count(item => string.Equals(item.Outcome, "Inconclusive", StringComparison.OrdinalIgnoreCase));

            var builder = new StringBuilder(4096);
            builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").AppendLine();
            builder.Append("<test-run")
                .Append(" total=\"").Append(total.ToString(CultureInfo.InvariantCulture)).Append('"')
                .Append(" passed=\"").Append(passed.ToString(CultureInfo.InvariantCulture)).Append('"')
                .Append(" failed=\"").Append(failed.ToString(CultureInfo.InvariantCulture)).Append('"')
                .Append(" skipped=\"").Append(skipped.ToString(CultureInfo.InvariantCulture)).Append('"')
                .Append(" inconclusive=\"").Append(inconclusive.ToString(CultureInfo.InvariantCulture)).Append('"')
                .AppendLine(">");
            builder.AppendLine("  <test-suite type=\"Assembly\" name=\"UnityMcp.AgentBridge.Tests.Editor.Mcp\">");
            builder.AppendLine("    <results>");

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                builder.Append("      <test-case")
                    .Append(" name=\"").Append(EscapeXml(result.Name)).Append('"')
                    .Append(" fullname=\"").Append(EscapeXml(result.FullName)).Append('"')
                    .Append(" result=\"").Append(EscapeXml(result.Outcome)).Append('"')
                    .Append(" duration=\"").Append(result.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append('"');

                if (!string.IsNullOrEmpty(result.TestId))
                {
                    builder.Append(" testcaseid=\"").Append(EscapeXml(result.TestId)).Append('"');
                }

                builder.AppendLine(" />");
            }

            builder.AppendLine("    </results>");
            builder.AppendLine("  </test-suite>");
            builder.AppendLine("</test-run>");

            File.WriteAllText(path, builder.ToString(), Utf8NoBom);
        }

        private static string EscapeXml(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private sealed class BatchmodeCallbacks : ScriptableObject, ICallbacks
        {
            private readonly List<TestCaseResultRecord> _results = new List<TestCaseResultRecord>();

            public IReadOnlyList<TestCaseResultRecord> Results => _results;
            public bool Finished { get; private set; }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Finished = false;
                _results.Clear();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                CollectLeafResults(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            private void CollectLeafResults(ITestResultAdaptor node)
            {
                if (node == null)
                {
                    return;
                }

                if (!node.HasChildren || node.Children == null || !node.Children.Any())
                {
                    var categories = node.Test != null && node.Test.Categories != null
                        ? node.Test.Categories
                        : Array.Empty<string>();
                    var testId = categories.FirstOrDefault(item => item != null && item.StartsWith("AGBM_", StringComparison.Ordinal));

                    _results.Add(new TestCaseResultRecord
                    {
                        Name = node.Name ?? string.Empty,
                        FullName = node.FullName ?? string.Empty,
                        Outcome = node.TestStatus.ToString(),
                        DurationSeconds = Math.Max(0d, node.Duration),
                        TestId = testId ?? string.Empty,
                    });
                    return;
                }

                foreach (var child in node.Children)
                {
                    CollectLeafResults(child);
                }
            }
        }

        private sealed class TestCaseResultRecord
        {
            public string Name { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Outcome { get; set; } = string.Empty;
            public double DurationSeconds { get; set; }
            public string TestId { get; set; } = string.Empty;
        }
    }
}
