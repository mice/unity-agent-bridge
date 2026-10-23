---
testId: AGBM_281
module: AgentBridge.MultiProject.History
testType: DotNet
target: Compatibility of existing retained terminal report references
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/McpMultiProjectHistoryTests.cs
testMethod: PreviouslyRetainedLongReportReferencesRemainImmutableAndReadableLocally
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_281

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05 / MP-A04 / MP-A06: ordinary/native retained diagnostics remain isolated to the admitted project and remain available through retained history without replacing existing terminal results.

## Risk
Changing the new report filename layout could rewrite an immutable saved terminal, duplicate its report, or strand older two-digest report paths.

## Steps
Seed a supported retained command record and its old two-digest report filename in an isolated file fixture. Attempt to save a conflicting result under the same command, then read the original terminal and report through the .NET local history API.

## Assertions
The original report reference and contents remain readable. A conflicting later save cannot replace the saved terminal, rewrite the old report path, or create a second report; exactly the original record and report remain. This compatibility assertion does not claim that historical overlong paths are readable by Unity Mono.

## Expected Result
Project-local retained reports preserve readable diagnostics, command/source ownership and immutable terminal history. Fixtures create no Unity project or Library copy and launch no Editor.

## Latest Run Result
2026-10-23 (Asia/Shanghai): this named regression passed in the root-owned full run, `Temp/AgentBridge-MultiProject/dotnet-report-path-final.log`. The full run finished with 257 passed, 1 failed and 1 skipped (259 total); its sole failure was the existing SDK host-restart client initialization case. An unchanged focused retry of both report regressions and that SDK case passed 3/3 in 19.715 seconds (`Temp/AgentBridge-MultiProject/dotnet-report-path-focused-retry.log`). This is individual regression success and successful focused verification, not an all-passing full suite claim. Real Unity reader compatibility and the full fixed-B lifecycle are validated separately and remain pending their reviewed evidence.

## Final verification
The subsequent full suite passed 260 tests, failed none and skipped one platform case (261 total, 1.5686 minutes); stable workbench evidence: Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-codec-final.log. The final fixed-B lifecycle passed all 23 scenarios, including the three-case Unity reader and ordinary/modern overflow report reads. Evidence: the sibling lifecycle-final/lifecycle-summary.json. Earlier failures and focused retries remain historical evidence.
