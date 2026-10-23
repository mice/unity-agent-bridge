---
testId: AGBM_274
module: AgentBridge.MultiProject.Diagnostics
testType: EditMode
target: Test runner diagnostics, retained reports and result projection
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/AgentTaskEditor/EditModeDiagnosticContractTests.cs
testMethod: LowRawReportLimitPreservesHostOwnedReports
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_274

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05 / MP-A04 / MP-A06: per-project diagnostic messages and retained reports remain attributable and readable through ordinary/native task and offline paths.

## Risk
A real assertion is reduced to TestAssertionsFailed, a restored opaque payload loses its report or warning, or one retention owner removes another owner's still-used report.

## Steps
Run the focused EditMode contract test in the current source checkout. All fixtures use owned fake callbacks or small files; they do not create a Unity project or copy a Library cache.

## Assertions
A low Editor raw-report limit removes only its oldest raw report and preserves a separately owned host report even when that host file is old.

## Expected Result
Actual diagnostics and report paths survive the tested boundary; infrastructure/report observation does not falsify test outcomes. This focused coverage does not by itself complete real Codex-session acceptance.

## Latest Run Result
2026-10-23 (Asia/Shanghai): **1 case(s) passed** in root-owned fixed-project Unity run `ae60375f607d45dc91a5aca3b1699a96`. The focused diagnostic contract group finished **8 passed, 0 failed, 0 skipped**.

Evidence: [stable Unity diagnostic contract XML](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/unity-diagnostic-contract.xml). Its UTC interval is 2026-10-22 17:39:39Z–17:39:40Z (2026-10-23 in Asia/Shanghai). This is actual focused EditMode execution; it does not establish real Codex-client or complete multi-project acceptance.

The evidence link resolves in the sibling workbench source checkout; these workbench acceptance artifacts are not bundled into the UPM package.
