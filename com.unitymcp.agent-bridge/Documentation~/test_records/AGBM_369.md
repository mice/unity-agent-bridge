---
testId: AGBM_369
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: EditorTaskScheduling API TestJobData activity
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorSchedulingActorTests.cs
testMethod: ManualApiTestJobWithIdleLegacyFlag_BlocksFirstDispatch
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_369

## Requirement
SCHED-G01/G03/G04: actual Test Runner jobs retain ownership and block conflicting dispatch.

## Risk
Test Framework 1.1.33 IsRunActive reads the legacy RunData flag, which can be false while API TestJobData isRunning is true. Treating legacy idle as actual completion prematurely releases the slot.

## Steps and Assertions
Run `ManualApiTestJobWithIdleLegacyFlag_BlocksFirstDispatch` with the actual actor and independent fake legacy/job states. An API job must block dispatch or retain the original execution owner until the persisted job holder also reports no running jobs. Fake clocks make the check deterministic.

## Evidence
Both new tests failed against the original detector (api-run-ownership-red.trx: 0 passed, 2 failed). After the fix, the full actor scope passed 29/29 (api-run-ownership-green.trx). A live fixed-A test previously reached test-start with no scheduler owner, triggering this regression; real rerun remains separate.
