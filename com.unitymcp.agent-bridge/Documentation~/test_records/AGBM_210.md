---
testId: AGBM_210
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_DownloadsChecksumAndInvokesVerifiedManagerInstall
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_210

## Requirement

A missing published machine runtime must be downloaded and installed through the existing verified manager flow.

## Assertions

- The checksum sidecar and ZIP artifact are requested directly.
- The manager install command receives the archive path and expected SHA-256.
- The successful result identifies the selected exact version.
