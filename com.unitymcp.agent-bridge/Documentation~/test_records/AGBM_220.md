---
testId: AGBM_220
module: AgentBridgeMachineRuntime
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_StaleSourceOnlyManifestUsesVerifiedPackagedCatalog
status: active
lastRun: "2026-08-21T12:01:32Z"
lastResult: passed
---

# AGBM_220

## Requirement

A stale machine-local source-only release manifest must not override the verified packaged source identity when it lacks a valid commit SHA.

## Assertions

- A source-built manifest is recognized as source-only, so it has no binary asset URL.
- A missing local commit SHA is replaced by the packaged RC3 commit identity.
- The current and previous RC choices remain available.
