---
testId: AGBM_258
module: AgentBridgeMachineRuntime
testType: EditMode
target: MachineRuntimeLocator; MachineRuntimeDownloader
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/RuntimeReleaseDiscoveryTests.cs
testMethod: MalformedMachineRowDoesNotPoisonValidRowsAndDuplicatesMerge
status: active
lastRun: "2026-10-16"
lastResult: passed
---

# AGBM_258

## Requirement
RUNTIME-CATALOG-001; `fix-runtime-release-discovery` trusted identity, post-merge RC eligibility and immutable runtime management.

## Assertions
`MalformedMachineRowDoesNotPoisonValidRowsAndDuplicatesMerge` covers its named discovery/cache failure mode using isolated deterministic fixtures. Identity conflicts must not fabricate or overwrite a release; filtering must preserve existing installations and pins. All fixtures are cleaned up.

## Latest Run Result
Passed in the 32-case runtime selection/discovery lane. See `Documentation~/AgentBridge/acceptance/RUNTIME-RELEASE-DISCOVERY-20261016.md` for exact commands and broader regression evidence. Fixture tests make no network calls and do not modify production tags or user projects.
