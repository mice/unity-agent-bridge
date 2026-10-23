---
testId: AGBM_286
module: AgentBridge.MultiProject.Capabilities
testType: EditMode
target: UI discovery cannot republish the active runtime catalog
testFile: Tests/Editor/Core/PluginDiscoveryTests.cs
testMethod: InspectionDiscovery_DoesNotOverwriteActiveCatalogOrClaimPublication
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_286

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G03: capability publication belongs to the admitted Editor session and installed registry, with fail-closed routing before dispatch.

## Risk
A failed publish, changed disk catalog or UI inspection could otherwise attest bytes that do not describe the poller's installed tools.

## Steps
Seed exact catalog bytes and invoke discovery with publishCatalog:false.

## Assertions
Catalog bytes remain identical and inspection returns capability revision missing.

## Expected Result
Only a successful runtime publication supplies an immutable capability revision. Small owned file fixtures are cleaned; settings objects and pollers are disposed. Existing asset metadata is preserved.

## Latest Run Result
2026-10-23 (Asia/Shanghai): this exact filtered Unity test passed in fixed-project run-732b01c2fdf04db387f9cd9daa0558d2. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/capability-probe-read-failure/evidence.json. All three publication tests and ordinary concurrent diagnostics passed before the seventh-case boundary, where the Python probe encountered PermissionError reading the atomically replaced heartbeat during controlled catalog-change checks. The overall run failed; no full lifecycle or actual-client completion is claimed. Restoration evidence records all A 11 / B 12 controlled file preimages matching.

## Latest capability/read-fix acceptance
2026-10-23: this exact filtered Unity regression also passed in final run `run-b806667827d747aca79dfc3a87c8dfbf`. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/final-capability-codex-run/protocol-evidence.json and codex-complete.json. All 17 protocol cases and the actual Codex recording passed root and independent review; outer process 49656 exit 0, A 17 / B 12 preimages match and owned processes were reclaimed. The previous probe-read failure remains preserved as historical evidence. This result does not claim a new all-passing full .NET suite or authorize publication.
