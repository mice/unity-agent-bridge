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

- The catalog contains only the RC3 release target `v1.2.12-rc.3`.
- The built-in catalog entry carries the release tag and direct asset URLs; the final tagged commit SHA is added during release publication.
- Legacy and removed tags before `v1.2.12-rc.3` are not included.
- Empty local storage reports the versions as not installed.
- Each entry retains its direct release-asset URL.
