---
testId: AGBM_330
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: EditorExecutionJournalStoreTests
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorExecutionJournalStoreTests.cs
testMethod: InterruptedFirstWrite_IsNotMistakenForPristineState
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_330

## Requirement
SCHED-G01–G05 as applicable; add-editor-task-scheduling. Preserve actual execution ownership, immutable origins and bounded non-replaying recovery.

## Risk
Logical completion, missing persistence or transport projection could incorrectly authorize conflicting execution or lose a queued request's original identity.

## Steps and Assertions
A retained partial first-write file without primary must produce an explicit unavailable-state error.

## Evidence
Product .NET foundation tests exercise the actual scheduling/lifecycle/store/codec sources through fake clocks and record fixtures. These are not Unity integration results. Raw TRX and source identity are recorded in the workbench scheduling acceptance evidence; Poller/service integration and real fixed A/B acceptance remain separate gates.
