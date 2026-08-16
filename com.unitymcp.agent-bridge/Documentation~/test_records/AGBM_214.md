---
testId: AGBM_214
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_RejectsMismatchedProjectCacheWithoutOverwriteOrNetwork
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_214

## Requirement

A cached runtime ZIP whose embedded version differs from the selected version must be preserved and rejected.

## Assertions

- The mismatch reports `cached_artifact_version_mismatch`.
- No network or manager call occurs.
- The mismatched cache file is not overwritten or deleted.
