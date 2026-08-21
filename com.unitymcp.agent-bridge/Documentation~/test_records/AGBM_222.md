---
testId: AGBM_222
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineRuntimeDownload_DoesNotReuseCachedSourceForDifferentCommit
status: active
lastRun: "2026-08-21T12:01:32Z"
lastResult: passed
---

# AGBM_222

## Requirement

A source-only runtime cache must be keyed by the published immutable commit as well as the selected version.

## Assertions

- A stale source ZIP for the same version but a different commit is not reused.
- The downloader fetches the selected commit's source archive and passes that commit to the source build.
