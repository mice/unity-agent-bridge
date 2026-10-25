---
testId: AGBM_376
module: AgentBridge.EditorScheduling
testType: DotNetContract
target: Preserve typed compile timeout diagnostics under original deadline
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorSchedulingActorTests.cs
testMethod: NativeCompileDeadline_AdvancesOriginalDriverBeforeGenericFailure
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_376

## Requirement
SCHED-G02/G03; original compile terminal contract and independent physical ownership.

## Steps and Assertions
Expire the original admitted deadline before the operation's own update callback. Preserve the original typed compile diagnostics/report projection, advance only the original native driver, keep actual ownership while compilation remains active, and never start following work. The previous scheduler produced generic AGENT_TASK_TIMEOUT and lost the typed payload (red 2/2).

## Evidence
Red: workbench Temp/editor-task-scheduling/typed-compile-timeout-red.trx. Actor scope now passes 31/31 (typed-compile-timeout-final-green.trx). Full and actual lifecycle validation remain pending.

Final regression: full .NET 368 passed, one platform skip; real lifecycle run-9c3f8f86db1c4bfeaae80024775c213d passed. Final multi-project protocol run-6cd6f125cbba47728d6845356d670814 passed 27 cases after the callback-root follow-up. Exact scope is in workbench EDITOR-TASK-SCHEDULING-20261025.md.
