---
testId: AGBM_263
module: AgentBridge.RuntimeInstall
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_PreflightFailureStopsBeforeSourceTransferAndManager
status: active
lastRun: "2026-10-17"
lastResult: passed
---

# AGBM_263

## Requirement and assertions
Failed source prerequisite admission prevents source transfer/build and manager installation and retains remediation.

## Evidence
Change `fix-source-install-preflight-and-workspace`. Existing fixture lifecycle removes its own temporary directory. Fake process/network seams avoid real install side effects.
