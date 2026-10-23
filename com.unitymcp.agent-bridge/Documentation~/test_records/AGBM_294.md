---
testId: AGBM_294
module: AgentBridge.MultiProject.Discovery
testType: DotNet
target: Same-project discovery read/validate/publication is serialized
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/ProjectDiscoveryConcurrencyTests.cs
testMethod: SameProjectObservationsSerializeReadValidationAndSnapshotPublication
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_294

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G04: concurrent routing observations preserve each project's ordered immutable session snapshot without globally serializing unrelated projects.

## Risk
Concurrent reads of one project can interleave cache validation and publication, falsely reject a fresh observation or let an older snapshot replace a newer one.

## Steps
Hold revision N inside the injected reader, start a second observer and wait for its blocked/completed thread state rather than sleeping. Release N, verify N then N+1 readiness, and finally supply stale N. A fixed injected clock and in-memory registry avoid filesystem, Editor and scheduling-delay fixtures.

## Assertions
The second reader cannot enter until the first completes; both valid observations remain ready, N+1 stays authoritative and stale N remains SESSION_REVISION_REGRESSED. Production freshness and stale-state checks remain unchanged.

## Expected Result
Per-project observation is serialized across read, validation and publication, while independent projects remain concurrent.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned focused verification of all ProjectDiscovery, ProjectIdentity, CatalogScope and CatalogEntryPoint tests: 51/51 passed in 32.4125 seconds, outer process 21431 exit 0. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-observation-final.log. This focused run does not replace the recorded full-suite outcome. This independent concurrency defect is not claimed as the cause of run-0e96789527ac40a8883a9f916e77aa0a; that real run used serial RPCs and requires its own diagnostic evidence.