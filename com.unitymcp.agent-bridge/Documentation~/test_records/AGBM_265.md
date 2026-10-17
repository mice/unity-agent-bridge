---
testId: AGBM_265
module: AgentBridge.RuntimeSelection
testType: EditMode
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_StableHidesSameBaseRcWithoutChangingInstalledPins
status: active
lastRun: "2026-10-17"
lastResult: passed
---

# AGBM_265

## Requirement and assertions
Stable packages exclude same-base RCs after catalog and machine-manifest merge. Prerelease packages retain eligible same-base RCs. Lower RCs remain hidden, higher candidates and older stable versions remain, and installed RC management/exact pins still resolve without directory deletion or selection mutation.

## Evidence
Change publish-1-4-3-and-retire-rcs; two parameterized cases.
