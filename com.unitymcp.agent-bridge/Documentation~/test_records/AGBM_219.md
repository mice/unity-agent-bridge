---
testId: AGBM_219
module: AgentBridgeMachineRuntime
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_SourceOnlyReleaseBuildsFromTagWithoutBinaryRequest
status: active
lastRun: ""
lastResult: unknown
---

# AGBM_219

## Requirement

A published source-only runtime must install by downloading the immutable tag source and building locally, with no binary release request.

## Assertions

- The source-only install succeeds through the existing verified source-build flow.
- No binary checksum request is made.
- The only artifact download is the selected tag source archive.
- The installation summary does not report a binary-release failure.
