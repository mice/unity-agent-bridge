---
testId: AGBM_291
module: AgentBridge.MultiProject.Capabilities
testType: DotNet
target: Ordinary service admission rejects stale or incompatible target catalogs
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/CatalogEntryPointRegressionTests.cs
testMethod: OrdinaryToolEntryRejectsUnboundCatalogBeforeAnyDispatch
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_291

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G03: actual ordinary/native task entrypoints validate immutable session capabilities before dispatch and allocation.

## Risk
An outer contract check can be bypassed by an unscoped inner lookup or native task creation that connects before validating the target catalog.

## Steps
Call the real multi-project McpServerService.CallToolAsync with changed catalog bytes, a missing target test tool, and a reserved projectId business field conflict. Fixtures publish actual SHA256 catalog revisions and fresh heartbeat identity for the current test process; no Unity process is used.

## Assertions
Each case returns its specific capability/tool/schema error before any queue command, task ledger or route index allocation. The migrated test plugin owns its fixture business schema; its reserved-field conflict is TOOL_ROUTING_FIELD_CONFLICT, not a nonexistent built-in test schema mismatch.

## Expected Result
All rejection paths fail closed with no command, task or ownership side effects. Tests use small owned file fixtures and restore registry environment configuration.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned focused verification, 31/31 cases in 1.4121 minutes, with original budgets and assertions. Stable log: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-capability-focused-final.log. The preceding full suite finished with 268 passed, 4 failed and 1 skipped (273 total); two failures involved test heartbeat facilities and two involved SDK initialization. After narrow test fixture fixes, the related focused groups passed. This is not a claim of a new all-passing full suite. Real Unity capability publication tests and the active full A/B lifecycle remain separate pending evidence.