---
testId: AGBM_304
module: AgentBridge.MultiProject
testType: DotNetUnit
target: Project maintenance observes each original task once
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/McpMultiProjectConnectionTests.cs
testMethod: MaintenanceRoutesEachActiveHistoryOnceAcrossConnectionScopes
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_304

## Requirement
MP-G06 with MP-G02/MP-G04/MP-G05: new work uses the current MCP connection while original task, Editor, project and report identities remain immutable.

## Steps and Assertions
Cache multiple historical/current connection scopes. Count protocol observations during one maintenance cycle; validate original persistent bindings, public task count and zero create replays.

The fixture uses isolated protocol files and an in-process queue responder; it does not launch or import a Unity project. Process identity is the test process. Registry environment and owned temporary files are restored in cleanup.

## Evidence
Repair execution is tracked by OpenSpec add-multi-project-routing tasks 7.5–7.12. Final regression and fixed A/B acceptance passed; fixture behavior remains distinct from actual Unity and Codex evidence.


2026-10-23: final regression fixture on unchanged product HEAD reproduced five connection-origin failures (the current-first control passed). With the connection-scope and maintenance repair, all six connection regression cases passed within the 118/118 focused suite. Raw logs/TRX and source hashes are retained for the repair acceptance record.

Complete CLI suite: 287 passed, one platform-condition skip, zero failures. All 26 real protocol checks and one actual Codex session passed root review. Durable evidence is retained in the workbench Documentation~/AgentBridge/acceptance/MULTI-PROJECT-CONNECTION-REPAIR-20261023.md.
