---
testId: AGBM_367
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: AgentExecutionScheduler journal validation
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorExecutionSchedulerTests.cs
testMethod: RestoredDuplicateCommandOrTaskAlias_FailsClosed
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_367

## Requirement
SCHED-G04: corrupt/ambiguous ownership must not authorize new work.

## Risk
Two restored records sharing one command or task alias could reattach the wrong driver and lose exclusive ownership.

## Steps and Assertions
Create two valid waiting records, inject a duplicate command identity or cross-record native/linked task alias into the fake durable store, and restore. Both mutations must expose RecoveryError, refuse claim and reject new admission. No driver is invoked.

## Evidence
integration-recorded.trx: 53 passed; integration-final-full.trx: 349 passed and one platform skip. These are offline lifecycle/journal checks.
