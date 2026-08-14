---
testId: AGBM_201
module: AgentBridgeRuntimeSelection
testType: EditMode
target: McpEditorSettingsStore
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_RoundTripsExactVersionAndChannel
status: active
lastRun: "2026-08-09"
lastResult: blocked
---

# AGBM_201

## Requirement

Machine runtime mode, exact version, channel, and machine cache root persist through the editor settings store.

## Risk

Settings normalization or schema changes could silently discard a project's runtime selection and send the launcher to a different version.

## Steps

- Save machine mode with exact version `1.2.3`, channel `preview`, and a padded machine root.
- Load the settings from disk.

## Assertions

- Machine mode, exact version, channel, and normalized machine root round-trip unchanged.

## Expected Result

The loaded settings retain the selected machine runtime identity.

## Latest Run Result

Not executed: Unity batch compilation stopped on the pre-existing `MonoBehaviourYamlSemanticValidator` resolution error in the workbench-linked package checkout.

## Notes

The test remains deterministic and uses only a temporary settings file.
