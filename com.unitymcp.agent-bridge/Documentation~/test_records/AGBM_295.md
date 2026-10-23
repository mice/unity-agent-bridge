---
testId: AGBM_295
module: AgentBridge.MultiProject.Discovery
testType: DotNet
target: Independent project discovery remains concurrent
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/ProjectDiscoveryConcurrencyTests.cs
testMethod: BlockedProjectObservationDoesNotSerializeAnIndependentProject
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_295

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G04: concurrent routing observations preserve each project's ordered immutable session snapshot without globally serializing unrelated projects.

## Risk
Concurrent reads of one project can interleave cache validation and publication, falsely reject a fresh observation or let an older snapshot replace a newer one.

## Steps
Hold project A inside its injected reader and start a separate long-running observation of project B. Release A in finally. A fixed injected clock and in-memory registry avoid filesystem, Editor and scheduling-delay fixtures.

## Assertions
B returns ready before A is released; all event waits and cleanup joins are bounded to prevent deadlock. Production freshness and stale-state checks remain unchanged.

## Expected Result
Per-project observation is serialized across read, validation and publication, while independent projects remain concurrent.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned focused verification of all ProjectDiscovery, ProjectIdentity, CatalogScope and CatalogEntryPoint tests: 51/51 passed in 32.4125 seconds, outer process 21431 exit 0. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-observation-final.log. This focused run does not replace the recorded full-suite outcome. This independent concurrency defect is not claimed as the cause of run-0e96789527ac40a8883a9f916e77aa0a; that real run used serial RPCs and requires its own diagnostic evidence.