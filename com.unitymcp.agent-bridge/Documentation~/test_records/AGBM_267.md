---
testId: AGBM_267
module: AgentBridge.MultiProject
testType: EditMode
target: McpServerProcessProbe / McpMultiProjectConfig
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpProcessAndConfigTests.cs
testMethod: Classify_PathPrefixDoesNotEstablishProjectOwnership
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_267

## Requirement and assertions
A different path sharing the current path prefix cannot establish exclusive ownership.

## Evidence
OpenSpec add-multi-project-routing, MP-G02 / MP-G03; 19/19 focused EditMode cases passed; workbench Temp/AgentBridge-MultiProject/runs/run-46fd038ae3f845c994a3711f5949be8d/unity-routing-editmode.xml.
