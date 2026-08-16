---
testId: AGBM_213
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_UsesMatchingProjectCacheWithoutNetwork
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_213

## Requirement

A matching runtime ZIP below `Temp/AgentBridge/<version>` must support offline installation.

## Assertions

- The cached manifest version matches the selected version.
- No checksum or ZIP network request occurs.
- The manager receives the cached archive and its locally computed SHA-256.
- The cached file remains available after installation.
