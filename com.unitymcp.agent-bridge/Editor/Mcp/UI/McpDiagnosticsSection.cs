using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpDiagnosticsSection
    {
        private readonly string _fallbackTooltip;
        private bool _showDetails;

        public McpDiagnosticsSection(string fallbackTooltip)
        {
            _fallbackTooltip = fallbackTooltip ?? string.Empty;
        }

        public void Draw(
            IReadOnlyList<McpDiagnosticCheck> checks,
            McpReadiness readiness,
            string reportText,
            bool isRunning,
            Action runDiagnostics,
            Action clearDiagnostics)
        {
            if (Event.current == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Step 3: Verify", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Last Result", FormatReadiness(readiness));

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isRunning))
                    {
                        if (GUILayout.Button(isRunning ? "Running Diagnostics..." : "Run Quick Diagnostics", GUILayout.Height(24f)))
                        {
                            runDiagnostics?.Invoke();
                        }
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(reportText)))
                    {
                        if (GUILayout.Button("Copy Report", GUILayout.Width(110f)))
                        {
                            EditorGUIUtility.systemCopyBuffer = reportText ?? string.Empty;
                        }
                    }

                    if (GUILayout.Button("Open MCP Log Folder", GUILayout.Width(140f)))
                    {
                        OpenLogFolder();
                    }

                    using (new EditorGUI.DisabledScope((checks == null || checks.Count == 0) && string.IsNullOrEmpty(reportText)))
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(80f)))
                        {
                            clearDiagnostics?.Invoke();
                        }
                    }
                }

                EditorGUILayout.Space(8f);
                if (checks == null || checks.Count == 0)
                {
                    EditorGUILayout.HelpBox("No diagnostics have been run yet.", MessageType.Info);
                    return;
                }

                var topCheck = GetPriorityCheck(checks);
                if (topCheck != null)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            string.Format("{0} [{1}] {2} ({3}ms)", topCheck.Code, topCheck.Severity, topCheck.Summary, (int)topCheck.Duration.TotalMilliseconds),
                            EditorStyles.boldLabel);

                        if (!string.IsNullOrEmpty(topCheck.Details))
                        {
                            EditorGUILayout.LabelField("Details", topCheck.Details, EditorStyles.wordWrappedLabel);
                        }

                        if (!string.IsNullOrEmpty(topCheck.Remediation))
                        {
                            EditorGUILayout.LabelField("Remediation", topCheck.Remediation, EditorStyles.wordWrappedLabel);
                        }
                    }
                }

                _showDetails = EditorGUILayout.Foldout(_showDetails, "Show Details", true);
                if (!_showDetails)
                {
                    return;
                }

                for (var index = 0; index < checks.Count; index++)
                {
                    var check = checks[index];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(
                            string.Format("{0} [{1}] {2} ({3}ms)", check.Code, check.Severity, check.Summary, (int)check.Duration.TotalMilliseconds),
                            EditorStyles.boldLabel);

                        if (!string.IsNullOrEmpty(check.Details))
                        {
                            EditorGUILayout.LabelField("Details", check.Details, EditorStyles.wordWrappedLabel);
                        }

                        if (!string.IsNullOrEmpty(check.Remediation))
                        {
                            EditorGUILayout.LabelField("Remediation", check.Remediation, EditorStyles.wordWrappedLabel);
                        }
                    }
                }
            }
        }

        private static string FormatReadiness(McpReadiness readiness)
        {
            switch (readiness)
            {
                case McpReadiness.NotChecked:
                    return "Not checked yet";
                case McpReadiness.Degraded:
                    return "Needs attention";
                case McpReadiness.Unavailable:
                    return "Unavailable";
                case McpReadiness.Ready:
                    return "OK";
                default:
                    return readiness.ToString();
            }
        }

        private static McpDiagnosticCheck GetPriorityCheck(IReadOnlyList<McpDiagnosticCheck> checks)
        {
            if (checks == null || checks.Count == 0)
            {
                return null;
            }

            for (var index = 0; index < checks.Count; index++)
            {
                if (checks[index].Severity == McpDiagnosticSeverity.Error)
                {
                    return checks[index];
                }
            }

            for (var index = 0; index < checks.Count; index++)
            {
                if (checks[index].Severity == McpDiagnosticSeverity.Warning)
                {
                    return checks[index];
                }
            }

            return checks[0];
        }

        private static void OpenLogFolder()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            var logRoot = projectRoot != null
                ? Path.Combine(projectRoot.FullName, "Library", "AgentBridge", "logs")
                : string.Empty;

            if (string.IsNullOrEmpty(logRoot))
            {
                return;
            }

            Directory.CreateDirectory(logRoot);
            EditorUtility.RevealInFinder(logRoot);
        }
    }
}
