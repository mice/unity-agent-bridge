---
testId: AGBM_289
module: AgentBridge.MultiProject.Capabilities
testType: DotNet
target: Immutable capability snapshots reject changed contracts
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/CatalogScopeRegressionTests.cs
testMethod: ChangedCatalogBytesRejectOriginalSnapshotAndNewDigestAllowsNewContract
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_289

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G03: capability validation uses immutable admitted project/session/revision and the exact catalog byte snapshot before dispatch.

## Risk
Disk catalogs can outlive Editor sessions or change between reads; stale contracts must not authorize tools in a replacement session.

## Steps
Publish string then integer schema bytes under the same session; use old then newly computed SHA256 revision.

## Assertions
Old snapshot is rejected; the new matching digest admits the integer contract without allocating a transport queue. Fixtures compute SHA256 from actual catalog bytes and invoke the production host context validator; no fake successful revision bypass is used.

## Expected Result
Valid immutable bindings admit their own catalog; stale bindings fail explicitly. Tests use only small owned file fixtures and launch no Editor or MCP process.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned focused verification, 31/31 cases in 1.4121 minutes, with original budgets and assertions. Stable log: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-capability-focused-final.log. The preceding full suite finished with 268 passed, 4 failed and 1 skipped (273 total); two failures involved test heartbeat facilities and two involved SDK initialization. After narrow test fixture fixes, the related focused groups passed. This is not a claim of a new all-passing full suite. Real Unity capability publication tests and the active full A/B lifecycle remain separate pending evidence.