---
testId: AGBM_269
module: AgentBridge.MultiProject
testType: EditMode
target: McpServerProcessProbe / McpMultiProjectConfig
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpMultiProjectConfigTests.cs
testMethod: ManagedMigration_PreservesUnrelatedServersAndSections
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_269

## Requirement and assertions
Managed legacy configuration migration preserves other servers and a recoverable backup.

## Evidence
OpenSpec add-multi-project-routing, MP-G02 / MP-G03; 19/19 focused EditMode cases passed; workbench Temp/AgentBridge-MultiProject/runs/run-46fd038ae3f845c994a3711f5949be8d/unity-routing-editmode.xml.
