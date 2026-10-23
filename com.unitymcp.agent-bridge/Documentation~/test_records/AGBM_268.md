---
testId: AGBM_268
module: AgentBridge.MultiProject
testType: EditMode
target: McpServerProcessProbe / McpMultiProjectConfig
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpMultiProjectConfigTests.cs
testMethod: GeneratedConfig_PreservesWorkspaceSeparatelyFromProject
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_268

## Requirement and assertions
Direct and launcher configurations carry a connection workspace and a separate project hint; direct hosts expose the multi-project command-line marker.

## Evidence
OpenSpec add-multi-project-routing, MP-G02 / MP-G03; 19/19 focused EditMode cases passed; workbench Temp/AgentBridge-MultiProject/runs/run-46fd038ae3f845c994a3711f5949be8d/unity-routing-editmode.xml.
