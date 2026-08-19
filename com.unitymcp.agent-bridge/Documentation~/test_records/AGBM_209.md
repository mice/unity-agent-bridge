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
- `ListPublishedVersions` returns the RC3 and RC2 release targets, ordered newest first.
- Older removed runtime tags are absent.
