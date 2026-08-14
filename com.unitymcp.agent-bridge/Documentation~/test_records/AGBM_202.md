---
testId: AGBM_202
module: AgentBridgeRuntimeSelection
testType: EditMode
target: McpEditorSettingsStore
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_InvalidChannelFallsBackToExactOnly
status: active
lastRun: "2026-08-09"
lastResult: blocked
---

# AGBM_202

## Requirement

An unsupported runtime channel is removed when an exact runtime version is present.

## Risk

Persisting an invalid channel alongside an exact pin could create ambiguous precedence and unexpected channel resolution.

## Steps

- Save machine mode with exact version `1.2.3` and unsupported channel `unsupported`.
- Load the settings from disk.

## Assertions

- The exact version remains present.
- The unsupported channel is normalized to empty.

## Expected Result

Exact version selection remains deterministic and takes precedence over channel selection.

## Latest Run Result

Not executed: Unity batch compilation stopped on the pre-existing `MonoBehaviourYamlSemanticValidator` resolution error in the workbench-linked package checkout.

## Notes

No network or external channel source is used.
