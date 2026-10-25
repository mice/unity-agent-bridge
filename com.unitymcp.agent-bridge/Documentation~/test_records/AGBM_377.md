---
testId: AGBM_377
module: AgentBridge.EditorScheduling
testType: DotNetContract
target: Mutable settings affect only future admission limits
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorSchedulingActorTests.cs
testMethod: MutableSettingsLifetime_AppliesToNewAdmissionWithoutChangingOldDeadline
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_377

## Requirement
SCHED-G02/G04: current limits apply at new admission; previously admitted deadlines never renew.

## Steps and Assertions
Configure a short limit, admit a blocked request, mutate the live settings object to a larger valid limit without rebuilding the actor, and admit another request. The second receives the new finite limit while the first deadline stays identical. Reproduced during actual lifecycle reload initialization.

## Evidence
The old admission limit failed the regression (mutable-settings-red.trx); corrected actor scope passed 32/32 (admission-settings-green.trx). Current-source full/live lifecycle remains in progress.

Final regression: full .NET 368 passed, one platform skip; real lifecycle run-9c3f8f86db1c4bfeaae80024775c213d passed. Final multi-project protocol run-6cd6f125cbba47728d6845356d670814 passed 27 cases after the callback-root follow-up. Exact scope is in workbench EDITOR-TASK-SCHEDULING-20261025.md.
