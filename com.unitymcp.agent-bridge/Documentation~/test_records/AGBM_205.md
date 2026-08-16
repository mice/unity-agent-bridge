---
testId: AGBM_205
module: AgentBridgeRuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_ListsOnlyUsableInstalledVersionsInDescendingOrder
status: active
lastRun: ""
lastResult: not_run
---

# AGBM_205

## Requirement

Machine runtime selection exposes only complete, verified cache versions and presents newer exact versions first.

## Risk

The setup panel could offer an incomplete cache directory that cannot launch.

## Assertions

- Only semantic version directories with a matching release manifest and runtime executable are returned.
- Stable releases sort ahead of equivalent prereleases.
- Higher numeric versions sort before lower versions.

## Expected Result

The setup panel has a safe source for its installed-version picker.
