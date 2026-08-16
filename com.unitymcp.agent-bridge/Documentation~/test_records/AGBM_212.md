---
testId: AGBM_212
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_ReportsBinaryAndSourceTransferFailureWithoutInstalling
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_212

## Requirement

If both the published binary and its tag source are unavailable, the transfer failure must remain visible without partially installing a version.

## Assertions

- The source transfer failure produces `source_download_failed` with the HTTP detail.
- The manager install command is not invoked.
