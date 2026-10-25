---
testId: AGBM_372
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: Busy project discovery and capability admission
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/CatalogScopeRegressionTests.cs
testMethod: VerifiedBusySchedulerRetainsCatalogIdentityAndRejectsLegacyOrRevisionChanges
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_372

## Requirement
SCHED-G01/G03/G05; bounded scheduling admission preserves verified project/session/capability authority while Unity is temporarily busy.

## Risk
Rejecting all recovering projects bypasses the scheduler, while treating unknown/legacy snapshots as queue-capable weakens identity and execution boundaries.

## Steps and Assertions
An explicitly scheduler-capable busy session accepts its original capability snapshot; legacy busy and changed revision remain rejected. Fixtures use record directories only; no Unity project is created.

## Evidence
Initial new scope: busy-routing-red.trx had four failures and five negative-control passes. The first corrected broader scope passed 40/40. Final variant coverage is rerun and recorded separately; actual manual import acceptance remains a separate live gate.
