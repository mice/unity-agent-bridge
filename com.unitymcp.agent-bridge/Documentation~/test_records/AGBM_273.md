---
testId: AGBM_273
module: AgentBridge.MultiProject.Diagnostics
testType: DotNet
target: Test runner diagnostics, retained reports and result projection
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/McpTestDiagnosticReportTests.cs
testMethod: OrdinaryAndTaskResultsRetainDiagnosticsAndOfflineReport
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_273

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05 / MP-A04 / MP-A06: per-project diagnostic messages and retained reports remain attributable and readable through ordinary/native task and offline paths.

## Risk
A real assertion is reduced to TestAssertionsFailed, a restored opaque payload loses its report or warning, or one retention owner removes another owner's still-used report.

## Steps
Run the focused DotNet contract test in the current source checkout. All fixtures use owned fake callbacks or small files; they do not create a Unity project or copy a Library cache.

## Assertions
Successful and failed native/ordinary results use the same generic diagnostic/report projection; retained report survives original-file removal and a changed current Editor session.

## Expected Result
Actual diagnostics and report paths survive the tested boundary; infrastructure/report observation does not falsify test outcomes. This focused coverage does not by itself complete real Codex-session acceptance.

## Latest Run Result
2026-10-23 (Asia/Shanghai): both DataRow cases (`succeeded=true` and `false`) passed in the root agent's full .NET suite: **232 passed, 0 failed, 1 skipped, 233 total**. The unrelated skipped case was `UnityEditorList_OnUnsupportedPlatformReturnsDeterministicFailure`.

Evidence: [full .NET run log](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-final.log). This records actual coordinated execution, not a claim that the entire multi-project feature or real-client acceptance passed.

The evidence link resolves in the sibling workbench source checkout; these workbench acceptance artifacts are not bundled into the UPM package.
