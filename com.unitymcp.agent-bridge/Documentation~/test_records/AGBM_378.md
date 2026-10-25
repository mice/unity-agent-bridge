---
testId: AGBM_378
module: AgentBridge.EditorScheduling
testType: DotNetContract
target: Compiler callbacks retain admitted queue after timeout and reload
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/EditorSchedulingActorTests.cs
testMethod: TimedOutCompileCallback_KeepsOriginalQueueAcrossSettingsAndReload
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_378

## Requirement
SCHED-G03/G04: actual epoch evidence must retain the original queue independently of logical result and settings.

## Risk
Logical timeout removes the service-local compiler operation before compilationFinished. Falling back to the default/current queue loses the original custom queue's end evidence and leaves occupancy unresolved.

## Steps and Assertions
Admit compile under a custom queue, persist its epoch, logically time out, change settings and reload the actor. Compiler callbacks still resolve the original admitted queue. Matching epoch completion then releases physical ownership and clears the callback queue reference. This is an actor/fake-boundary contract, not real custom-queue Unity acceptance.

## Evidence
Current actor scope passed 33/33 (callback-original-root-green.trx). Real default-queue lifecycle passed before this callback-root follow-up; final multi-project cold compilation exercises the resulting assemblies.
