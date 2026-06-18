using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public static class McpFocusedEditModeTestMethods
    {
        public static void RunFocusedTests(RunFocusedEditModeTestsArgs args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (args.testNames == null || args.testNames.Length == 0)
            {
                throw new ArgumentException("testNames must contain at least one fully-qualified test name.", nameof(args));
            }

            var callback = ScriptableObject.CreateInstance<FocusedEditModeTestCallbacks>();
            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callback);

                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = args.assemblyNames,
                    testNames = args.testNames
                };

                api.Execute(new ExecutionSettings(filter)
                {
                    runSynchronously = true
                });

                var report = callback.BuildReport(args);
                if (NeedsManualFallback(report))
                {
                    report = BuildManualFallbackReport(args, report);
                }

                WriteReport(args.reportPath, report);
                Debug.Log("McpFocusedEditModeTestMethods.RunFocusedTests().Done");
                Debug.Log(JsonConvert.SerializeObject(report, Formatting.None));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(callback);
            }
        }

        private static void WriteReport(string reportPath, FocusedEditModeTestReport report)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var absolutePath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(absolutePath, JsonConvert.SerializeObject(report, Formatting.Indented));
        }

        private static bool NeedsManualFallback(FocusedEditModeTestReport report)
        {
            if (report == null || report.requestedTestNames == null || report.requestedTestNames.Length == 0)
            {
                return false;
            }

            if (report.results == null || report.results.Length == 0)
            {
                return true;
            }

            if (report.results.Length == 1 &&
                string.Equals(report.results[0].fullName, report.rootFullName, StringComparison.Ordinal) &&
                !report.requestedTestNames.Contains(report.rootFullName, StringComparer.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static FocusedEditModeTestReport BuildManualFallbackReport(RunFocusedEditModeTestsArgs args, FocusedEditModeTestReport originalReport)
        {
            var results = new List<FocusedEditModeLeafResult>();
            var runState = "Passed";
            for (var index = 0; index < args.testNames.Length; index++)
            {
                var fullName = args.testNames[index];
                var startedAtUtc = DateTime.UtcNow;
                try
                {
                    InvokeTestMethod(fullName);
                    results.Add(new FocusedEditModeLeafResult
                    {
                        fullName = fullName,
                        outcome = "Passed",
                        durationMs = Math.Max(1L, (long)Math.Round((DateTime.UtcNow - startedAtUtc).TotalMilliseconds, MidpointRounding.AwayFromZero))
                    });
                }
                catch (Exception exception)
                {
                    runState = "Failed";
                    results.Add(new FocusedEditModeLeafResult
                    {
                        fullName = fullName,
                        outcome = "Failed: " + (exception.InnerException ?? exception).Message,
                        durationMs = Math.Max(1L, (long)Math.Round((DateTime.UtcNow - startedAtUtc).TotalMilliseconds, MidpointRounding.AwayFromZero))
                    });
                }
            }

            return new FocusedEditModeTestReport
            {
                rootFullName = originalReport != null ? originalReport.rootFullName : string.Empty,
                runState = runState,
                requestedAssemblyNames = args.assemblyNames ?? Array.Empty<string>(),
                requestedTestNames = args.testNames ?? Array.Empty<string>(),
                executionMode = "manual_reflection_fallback",
                results = results.ToArray()
            };
        }

        private static void InvokeTestMethod(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                throw new ArgumentException("fullName must not be empty.", nameof(fullName));
            }

            var separatorIndex = fullName.LastIndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= fullName.Length - 1)
            {
                throw new InvalidOperationException("Invalid fully-qualified test name: " + fullName);
            }

            var typeName = fullName.Substring(0, separatorIndex);
            var methodName = fullName.Substring(separatorIndex + 1);
            var targetType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(typeName, false))
                .FirstOrDefault(type => type != null);
            if (targetType == null)
            {
                throw new InvalidOperationException("Could not resolve test type: " + typeName);
            }

            var testMethod = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (testMethod == null)
            {
                throw new InvalidOperationException("Could not resolve test method: " + fullName);
            }

            var instance = Activator.CreateInstance(targetType);
            try
            {
                InvokeAttributedMethods(targetType, instance, "NUnit.Framework.SetUpAttribute");
                testMethod.Invoke(instance, null);
            }
            finally
            {
                InvokeAttributedMethods(targetType, instance, "NUnit.Framework.TearDownAttribute");
                if (instance is ScriptableObject scriptableObject)
                {
                    UnityEngine.Object.DestroyImmediate(scriptableObject);
                }
            }
        }

        private static void InvokeAttributedMethods(Type targetType, object instance, string attributeTypeName)
        {
            var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                if (method.GetParameters().Length != 0)
                {
                    continue;
                }

                var attributes = method.GetCustomAttributes(false);
                for (var attributeIndex = 0; attributeIndex < attributes.Length; attributeIndex++)
                {
                    var attribute = attributes[attributeIndex];
                    if (attribute != null && string.Equals(attribute.GetType().FullName, attributeTypeName, StringComparison.Ordinal))
                    {
                        method.Invoke(instance, null);
                        break;
                    }
                }
            }
        }

        private sealed class FocusedEditModeTestCallbacks : ScriptableObject, ICallbacks
        {
            private readonly List<FocusedEditModeLeafResult> _results = new List<FocusedEditModeLeafResult>();
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
            private string _rootFullName = string.Empty;
            private string _runState = string.Empty;

            public void RunStarted(ITestAdaptor testsToRun)
            {
                _rootFullName = testsToRun?.FullName ?? string.Empty;
                _runState = "started";
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _runState = result?.TestStatus.ToString() ?? "finished";
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null)
                {
                    return;
                }

                if (result.HasChildren)
                {
                    return;
                }

                var fullName = result.FullName ?? string.Empty;
                if (!_seen.Add(fullName))
                {
                    return;
                }

                _results.Add(new FocusedEditModeLeafResult
                {
                    fullName = fullName,
                    outcome = result.TestStatus.ToString(),
                    durationMs = Math.Max(1L, (long)Math.Round(result.Duration * 1000.0d, MidpointRounding.AwayFromZero))
                });
            }

            public FocusedEditModeTestReport BuildReport(RunFocusedEditModeTestsArgs args)
            {
                return new FocusedEditModeTestReport
                {
                    rootFullName = _rootFullName,
                    runState = _runState,
                    requestedAssemblyNames = args.assemblyNames ?? Array.Empty<string>(),
                    requestedTestNames = args.testNames ?? Array.Empty<string>(),
                    executionMode = "unity_test_runner_api",
                    results = _results.ToArray()
                };
            }
        }
    }

    [Serializable]
    public sealed class RunFocusedEditModeTestsArgs : IStaticMethodArgsValidator
    {
        public string[] assemblyNames;
        public string[] testNames;
        public string reportPath;

        public bool Validate(out string validationMessage)
        {
            if (testNames == null || testNames.Length == 0)
            {
                validationMessage = "testNames must contain at least one fully-qualified test name.";
                return false;
            }

            for (var index = 0; index < testNames.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(testNames[index]))
                {
                    validationMessage = "testNames entries must not be empty.";
                    return false;
                }
            }

            validationMessage = null;
            return true;
        }
    }

    [Serializable]
    public sealed class FocusedEditModeTestReport
    {
        public string rootFullName;
        public string runState;
        public string[] requestedAssemblyNames;
        public string[] requestedTestNames;
        public string executionMode;
        public FocusedEditModeLeafResult[] results;
    }

    [Serializable]
    public sealed class FocusedEditModeLeafResult
    {
        public string fullName;
        public string outcome;
        public long durationMs;
    }
}
