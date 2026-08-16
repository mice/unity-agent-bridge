---
testId: AGBM_204
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_FlatReleaseRuntimeResolvesExecutableAndLauncher
status: active
lastRun: "2026-08-09"
lastResult: blocked
---

# AGBM_204

## Requirement

The machine locator resolves the published flat Windows runtime layout and rejects path-like runtime versions.

## Risk

A release artifact could be installed successfully but reported missing by Unity diagnostics, or an invalid version value could escape the version cache.

## Steps

- Create a temporary `versions/1.2.3/runtime/win-x64` payload, version launcher, stable manager shim, and release manifest.
- Resolve the runtime executable and launcher from machine settings.
- Attempt resolution with `../escape` as the version.

## Assertions

- The flat runtime executable resolves to its absolute path.
- The selected version launcher resolves before the stable manager shim.
- The path-like version resolves to an empty path.

## Expected Result

Published runtime layout and version validation remain aligned with the manager contract.

## Latest Run Result

Not executed: Unity batch compilation stopped on the pre-existing `MonoBehaviourYamlSemanticValidator` resolution error in the workbench-linked package checkout.

## Notes

The fixture is temporary and contains no executable process or network access.
