---
testId: AGBM_215
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_ReusesCachedTagSourceWithoutSourceTransfer
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_215

## Requirement

A matching tag source ZIP under `Temp/AgentBridge/<version>/source` must support an offline rebuild.

## Assertions

- The cached source ZIP is reused without a source transfer.
- A manager-compatible runtime ZIP is built for the selected version.
- The verified manager install command runs only after the build succeeds.
