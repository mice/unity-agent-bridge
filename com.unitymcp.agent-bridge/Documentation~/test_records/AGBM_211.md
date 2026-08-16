---
testId: AGBM_211
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_InvalidBinaryChecksumBuildsFromTagSource
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_211

## Requirement

When the published binary checksum is unavailable or malformed, the downloader must fall back to the immutable tag source instead of installing an unverified binary.

## Assertions

- Source-build and manager scripts are resolved from the Unity package even when the selected machine runtime is already installed.

- The malformed checksum prevents the binary ZIP transfer.
- The source URL and pinned commit metadata drive a local source build.
- The manager installs only the locally built, version-matched runtime ZIP.
