---
testId: AGBM_280
module: AgentBridge.MultiProject.History
testType: DotNet
target: Retained report path budget and command/source isolation
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/McpMultiProjectHistoryTests.cs
testMethod: RetainedReportsRemainReadableWithinMonoPathBudgetAndIsolatedAcrossCommandsAndSources
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_280

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05 / MP-A04 / MP-A06: ordinary/native retained diagnostics remain isolated to the admitted project and remain available through retained history without replacing existing terminal results.

## Risk
The real fixed-B ordinary lifecycle produced an existing 291-character report path. Unity Mono accepted File.Exists but File.ReadAllText failed; redundant command and source digests made a readable report inaccessible to the existing single-project reader.

## Steps
Run the named focused .NET regression using small owned file fixtures. It matches the fixed B root length (113 characters), retains same-named reports from two source directories for two commands, removes Temp, and opens the retained reports through a fresh history instance.

## Assertions
All four command/source combinations retain independent report paths and contents. Each absolute retained path is below the 260-character legacy Mono path budget. After Temp deletion, every report remains readable and retained command lookup preserves its report reference. The implementation uses one complete SHA-256 over a structured command/source pair; the assertions exercise accessibility and isolation rather than a particular digest value.

## Expected Result
Project-local retained reports preserve readable diagnostics, command/source ownership and immutable terminal history. Fixtures create no Unity project or Library copy and launch no Editor.

## Latest Run Result
2026-10-23 (Asia/Shanghai): this named regression passed in the root-owned full run, `Temp/AgentBridge-MultiProject/dotnet-report-path-final.log`. The full run finished with 257 passed, 1 failed and 1 skipped (259 total); its sole failure was the existing SDK host-restart client initialization case. An unchanged focused retry of both report regressions and that SDK case passed 3/3 in 19.715 seconds (`Temp/AgentBridge-MultiProject/dotnet-report-path-focused-retry.log`). This is individual regression success and successful focused verification, not an all-passing full suite claim. Real Unity reader compatibility and the full fixed-B lifecycle are validated separately and remain pending their reviewed evidence.

## Final verification
The subsequent full suite passed 260 tests, failed none and skipped one platform case (261 total, 1.5686 minutes); stable workbench evidence: Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-codec-final.log. The final fixed-B lifecycle passed all 23 scenarios, including the three-case Unity reader and ordinary/modern overflow report reads. Evidence: the sibling lifecycle-final/lifecycle-summary.json. Earlier failures and focused retries remain historical evidence.
