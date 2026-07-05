using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace UnityMcp.AgentBridge.Mcp
{
    public sealed class McpStatusSection
    {
        private bool _showDetails;

        public void Draw(McpStatusViewModel viewModel)
        {
            if (viewModel == null || Event.current == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            DrawPrioritySummary(viewModel);

            if (!string.IsNullOrEmpty(viewModel.PriorityMessage))
            {
                EditorGUILayout.HelpBox(viewModel.PriorityMessage, viewModel.PriorityMessageType);
            }

            _showDetails = EditorGUILayout.Foldout(_showDetails, "Show Details", true);
            if (_showDetails)
            {
                DrawRow("Unity Bridge", viewModel.UnityBridgeStatus);
                DrawRow("Unity Project", viewModel.UnityProjectPath);
                DrawRow("Workspace Root", viewModel.WorkspaceRoot);
                DrawRow("Configured Unity Project", viewModel.ConfiguredUnityProjectPath, viewModel.ConfiguredUnityProjectHasIssue, viewModel.ConfiguredUnityProjectIssueTooltip);
                DrawRow("Tools Root", viewModel.ToolsRoot, viewModel.ToolsRootHasIssue, viewModel.ToolsRootIssueTooltip);
                DrawRow("Long-running MCP Server", viewModel.McpServerProcessState, viewModel.McpServerProcessHasIssue, viewModel.McpServerProcessIssueTooltip);
                DrawServerProcessDetails(viewModel.McpServerProcesses);
                DrawRow("Launcher Path", viewModel.LauncherPath);
                DrawRow("MCP Server Root", viewModel.McpServerRoot);
                DrawRow("CLI Root", viewModel.CliRoot);
                DrawRow("CLI", viewModel.CliStatus);
                DrawRow(".NET SDK", viewModel.DotnetVersion);
                DrawRow("MCP Readiness", viewModel.McpReadiness, viewModel.McpReadinessHasIssue, viewModel.McpReadinessIssueTooltip);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawPrioritySummary(McpStatusViewModel viewModel)
        {
            DrawRow("Readiness", viewModel.McpReadiness, viewModel.McpReadinessHasIssue, viewModel.McpReadinessIssueTooltip);
            DrawRow("Current Project", viewModel.UnityProjectPath);
            DrawRow("Workspace", viewModel.WorkspaceRoot);
            DrawRow("Configured Project", viewModel.ConfiguredUnityProjectPath, viewModel.ConfiguredUnityProjectHasIssue, viewModel.ConfiguredUnityProjectIssueTooltip);
            DrawRow("Tools", viewModel.ToolsRoot, viewModel.ToolsRootHasIssue, viewModel.ToolsRootIssueTooltip);
            DrawRow("Long-running MCP Server", viewModel.McpServerProcessState, viewModel.McpServerProcessHasIssue, viewModel.McpServerProcessIssueTooltip);
        }

        private static void DrawServerProcessDetails(IReadOnlyList<McpServerProcessInfo> processes)
        {
            if (processes == null || processes.Count == 0)
            {
                return;
            }

            for (var index = 0; index < processes.Count; index++)
            {
                var process = processes[index];
                DrawRow(
                    "Process " + process.ProcessId,
                    process.ProcessName + " - " + process.MatchKind + " - " + process.MatchReason);
                if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
                {
                    DrawRow("Executable", process.ExecutablePath);
                }

                if (!string.IsNullOrWhiteSpace(process.CommandLineSummary))
                {
                    DrawRow("Command Line", process.CommandLineSummary);
                }

                if (!string.IsNullOrWhiteSpace(process.InspectionError))
                {
                    DrawRow("Inspection", process.InspectionError, true, process.InspectionError);
                }
            }
        }

        private static void DrawRow(string label, string value)
        {
            DrawRow(label, value, false, string.Empty);
        }

        private static void DrawRow(string label, string value, bool hasIssue, string tooltip)
        {
            EditorGUILayout.BeginHorizontal();
            var displayLabel = hasIssue ? label + "*" : label;
            var labelContent = string.IsNullOrEmpty(tooltip)
                ? new GUIContent(displayLabel)
                : new GUIContent(displayLabel, tooltip);
            var valueContent = string.IsNullOrEmpty(tooltip)
                ? new GUIContent(value ?? string.Empty)
                : new GUIContent(value ?? string.Empty, tooltip);
            EditorGUILayout.LabelField(labelContent, GUILayout.Width(180f));
            EditorGUILayout.SelectableLabel(valueContent.text, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }
    }

    public sealed class McpStatusViewModel
    {
        public string UnityBridgeStatus { get; set; } = string.Empty;
        public string UnityProjectPath { get; set; } = string.Empty;
        public string WorkspaceRoot { get; set; } = string.Empty;
        public string ConfiguredUnityProjectPath { get; set; } = string.Empty;
        public string ConfiguredUnityProjectConfigPath { get; set; } = string.Empty;
        public string ToolsRoot { get; set; } = string.Empty;
        public string LauncherPath { get; set; } = string.Empty;
        public string McpServerRoot { get; set; } = string.Empty;
        public string CliRoot { get; set; } = string.Empty;
        public string CliStatus { get; set; } = string.Empty;
        public string DotnetVersion { get; set; } = string.Empty;
        public string McpReadiness { get; set; } = string.Empty;
        public string McpServerProcessState { get; set; } = string.Empty;
        public string PriorityMessage { get; set; } = string.Empty;
        public MessageType PriorityMessageType { get; set; } = MessageType.None;
        public bool ConfiguredUnityProjectHasIssue { get; set; }
        public string ConfiguredUnityProjectIssueTooltip { get; set; } = string.Empty;
        public bool ToolsRootHasIssue { get; set; }
        public string ToolsRootIssueTooltip { get; set; } = string.Empty;
        public bool McpReadinessHasIssue { get; set; }
        public string McpReadinessIssueTooltip { get; set; } = string.Empty;
        public bool McpServerProcessHasIssue { get; set; }
        public string McpServerProcessIssueTooltip { get; set; } = string.Empty;
        public IReadOnlyList<McpServerProcessInfo> McpServerProcesses { get; set; } = new McpServerProcessInfo[0];
    }
}
