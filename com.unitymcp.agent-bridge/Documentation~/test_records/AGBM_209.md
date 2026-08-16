---
testId: AGBM_209
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_MissingDefaultReleasesDirectoryUsesPublishedCatalog
status: active
lastRun: "2026-08-15"
lastResult: passed
---

# AGBM_209

## Requirement

The public version-enumeration entry point must use the packaged catalog when the default manager root has no `releases` directory.

## Assertions

- No local `releases` directory exists.
- `ListPublishedVersions` returns only the supported published tag `v1.2.12-rc.2`.
- Legacy and removed runtime tags are absent.
