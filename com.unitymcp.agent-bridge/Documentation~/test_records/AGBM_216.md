---
testId: AGBM_216
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_SourceBuildFailureDoesNotInvokeManagerInstall
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_216

## Requirement

A failed tag-source build must not create or activate a partially built machine runtime.

## Assertions

- The failure produces `source_build_failed` with compiler detail.
- The temporary build artifact is discarded.
- The manager install command is not invoked.
