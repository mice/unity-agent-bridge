---
testId: AGBM_358
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: EditorTaskScheduling
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorSchedulingActorTests.cs
testMethod: UnityTemplate_IsQueuedWithCapturedInputsAndOriginalDeadline
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_358

## Requirement
SCHED-G01–G05; add-editor-task-scheduling.

## Risk
Incorrect admission or recovery could start conflicting Unity operations, replay work or lose immutable delivery.

## Steps and Assertions
Run `UnityTemplate_IsQueuedWithCapturedInputsAndOriginalDeadline` using the actual Editor actor, journal and task lifecycle with fake Unity/process boundaries and a virtual clock. Assert driver invocations, durable ownership or original output as specified by this case.

## Evidence
`actor-latest.trx`: 27 passed, zero failed/skipped. This is an offline contract test; it does not establish real Unity event ordering or fixed A/B acceptance.
