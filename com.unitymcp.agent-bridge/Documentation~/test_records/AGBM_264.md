---
testId: AGBM_264
module: AgentBridge.RuntimeInstall
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_BuildFailurePreservesBothStreamsAndDoesNotInstall
status: active
lastRun: "2026-10-17"
lastResult: passed
---

# AGBM_264

## Requirement and assertions
Primary build failures remain visible alongside secondary cleanup output; manager installation does not run.

## Evidence
Change `fix-source-install-preflight-and-workspace`. Existing fixture lifecycle removes its own temporary directory. Fake process/network seams avoid real install side effects.
