---
testId: AGBM_203
module: AgentBridgeRuntimeSelection
testType: EditMode
target: McpEditorSettingsStore
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_RepeatedSaveRemainsSingular
status: active
lastRun: "2026-08-09"
lastResult: blocked
---

# AGBM_203

## Requirement

Repeatedly saving the same machine runtime selection produces one managed value and does not duplicate the settings entry.

## Risk

Non-idempotent persistence could grow or corrupt project-local configuration during repeated setup actions.

## Steps

- Save the same exact version and channel twice.
- Read the serialized settings file.

## Assertions

- The serialized settings contain one `runtimeVersion` property with value `1.2.3`.

## Expected Result

Repeated setup inputs produce a stable, singular settings document.

## Latest Run Result

Not executed: Unity batch compilation stopped on the pre-existing `MonoBehaviourYamlSemanticValidator` resolution error in the workbench-linked package checkout.

## Notes

The test uses a temporary file and cleans it up during teardown.
