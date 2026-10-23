---
testId: AGBM_293
module: AgentBridge.MultiProject.Capabilities
testType: DotNet
target: Inner service retains admitted catalog revision validation
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/CatalogEntryPointRegressionTests.cs
testMethod: ScopedInnerServiceRejectsChangedBytesInsteadOfReadingUnscopedCatalog
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_293

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G03: actual ordinary/native task entrypoints validate immutable session capabilities before dispatch and allocation.

## Risk
An outer contract check can be bypassed by an unscoped inner lookup or native task creation that connects before validating the target catalog.

## Steps
Capture production host CatalogDiagnostics for a ready current-process session, change disk catalog bytes and invoke the real scoped inner service. Fixtures publish actual SHA256 catalog revisions and fresh heartbeat identity for the current test process; no Unity process is used.

## Assertions
The inner service rejects PROJECT_CAPABILITY_MISMATCH rather than re-reading an unscoped catalog; no queue/index/task records are created. The migrated test plugin owns its fixture business schema; its reserved-field conflict is TOOL_ROUTING_FIELD_CONFLICT, not a nonexistent built-in test schema mismatch.

## Expected Result
All rejection paths fail closed with no command, task or ownership side effects. Tests use small owned file fixtures and restore registry environment configuration.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned focused verification, 31/31 cases in 1.4121 minutes, with original budgets and assertions. Stable log: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-capability-focused-final.log. The preceding full suite finished with 268 passed, 4 failed and 1 skipped (273 total); two failures involved test heartbeat facilities and two involved SDK initialization. After narrow test fixture fixes, the related focused groups passed. This is not a claim of a new all-passing full suite. Real Unity capability publication tests and the active full A/B lifecycle remain separate pending evidence.