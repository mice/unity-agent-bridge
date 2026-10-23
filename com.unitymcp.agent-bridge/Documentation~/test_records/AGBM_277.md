---
testId: AGBM_277
module: AgentBridge.MultiProject.Diagnostics
testType: EditMode
target: Test runner diagnostics, retained reports and result projection
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/AgentTaskEditor/EditModeDiagnosticContractTests.cs
testMethod: SingleProjectReportReaderReadsRawAndOwnedCopiedReport
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_277

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05 / MP-A04 / MP-A06: per-project diagnostic messages and retained reports remain attributable and readable through ordinary/native task and offline paths.

## Risk
A real assertion is reduced to TestAssertionsFailed, a restored opaque payload loses its report or warning, or one retention owner removes another owner's still-used report.

## Steps
Run the focused EditMode contract test in the current source checkout. All fixtures use owned fake callbacks or small files; they do not create a Unity project or copy a Library cache.

## Assertions
The actual single-project reader accepts standard raw/copy report payloads, while command-history metadata filenames remain outside its allowed report scope.

## Expected Result
Actual diagnostics and report paths survive the tested boundary; infrastructure/report observation does not falsify test outcomes. This focused coverage does not by itself complete real Codex-session acceptance.

## Latest Run Result
2026-10-23 (Asia/Shanghai): **2 case(s) passed** in root-owned fixed-project Unity run `ae60375f607d45dc91a5aca3b1699a96`. The focused diagnostic contract group finished **8 passed, 0 failed, 0 skipped**.

Evidence: [stable Unity diagnostic contract XML](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/unity-diagnostic-contract.xml). Its UTC interval is 2026-10-22 17:39:39Z–17:39:40Z (2026-10-23 in Asia/Shanghai). This is actual focused EditMode execution; it does not establish real Codex-client or complete multi-project acceptance.

The evidence link resolves in the sibling workbench source checkout; these workbench acceptance artifacts are not bundled into the UPM package.

## Compact retained report compatibility (2026-10-23)
The fixed-B legacy lifecycle exposed a 291-character retained report path that existed on disk but failed during Unity File.ReadAllText. New reports use one full SHA-256 of a structured command/source identity, with an r- prefix; the fixed B root remains unchanged. A third case accepts the compact report name while preserving old owned report names and rejecting command metadata. All three cases passed in the final fixed-B lifecycle run run-11c7d6df660f49c78132a77cd67f57bb, whose complete 23-case matrix passed at 2026-10-22T19:31:59.190768Z. Evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/lifecycle-final/lifecycle-summary.json and lifecycle-events.json. The prior two-case result above remains historical.
