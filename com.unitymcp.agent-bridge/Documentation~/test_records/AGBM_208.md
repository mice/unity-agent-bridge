---
testId: AGBM_208
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_BuiltInCatalogListsPublishedTagsForEmptyManagerRoot
status: active
lastRun: "2026-08-15"
lastResult: passed
---

# AGBM_208

## Requirement

An empty machine runtime root must still expose the published release tags from the assembly-contained catalog.

## Assertions

- The catalog begins with the newest runtime line at `v1.2.12-rc.2` and retains `v1.2.12-rc.1` as the source-fallback fixture.
- Built-in catalog entries pin both published tags to their resolved 40-character commit SHAs.
- Legacy tags before `v1.2.12-rc.1` are not included.
- Empty local storage reports the versions as not installed.
- Each entry retains its direct release-asset URL.
