---
testId: AGBM_208
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_BuiltInCatalogListsPublishedTagsForEmptyManagerRoot
status: active
lastRun: "2026-08-23"
lastResult: passed
---

# AGBM_208

## Requirement

An empty machine runtime root must still expose the published release tags from the assembly-contained catalog.

## Assertions

- The catalog contains `v1.2.12-rc.4`, `v1.2.12-rc.3`, and `v1.2.12-rc.2`, ordered newest first.
- The RC4 entry carries the canonical binary artifact and tag-source URLs and may leave its commit SHA empty before tag publication.
- The RC3 source-only entry carries no binary asset URL and a direct immutable commit archive plus SHA.
- The RC2 entry carries its final tagged commit SHA.
- Empty local storage reports the versions as not installed.
- Source-capable entries retain their direct tagged-source archive URL.
