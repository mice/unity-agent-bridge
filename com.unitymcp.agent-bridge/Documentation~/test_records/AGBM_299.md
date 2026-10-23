---
testId: AGBM_299
module: AgentBridge.MultiProject
testType: EditMode
target: McpServerProcessProbe / McpMultiProjectConfig
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpProcessAndConfigTests.cs
testMethod: StopCurrentProjectServers_PreservesMultiProjectHost
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_299

## Requirement and assertions
Command-line and validated process-lease modes exclude shared hosts from project Stop while blocking replacement of their runtime.

## Evidence
OpenSpec add-multi-project-routing, MP-G02 / MP-G03; 19/19 focused EditMode cases passed; workbench Temp/AgentBridge-MultiProject/runs/run-46fd038ae3f845c994a3711f5949be8d/unity-routing-editmode.xml.

## Permanent-ID correction
AGBM_299 is allocated to this multi-project Stop test. The original 2026-10-23 execution recorded category AGBM_266 before the collision with the existing workbench release-governance ID was discovered. The unchanged XML identifies this test by method StopCurrentProjectServers_PreservesMultiProjectHost (two parameter cases, both Passed); that historical result maps to current source ID AGBM_299. This metadata correction was not followed by a new Unity execution. The existing workbench AGBM_266 release-governance record remains unchanged.
