---
testId: AGBM_283
module: AgentBridge.MultiProject.History
testType: DotNet
target: Recovered durable compile diagnostics reach native MCP standard results
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/AgentTaskDurablePayloadRegressionTests.cs
testMethod: RecoveredCompilePayloadReachesAdapterAndStandardResultWithoutDiagnosticLoss
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_283

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05: durable native task diagnostics remain readable after recovery and preserve their standard result contract.

## Risk
The fixed-B legacy lifecycle retained a valid Unity durable response, but chained codec unescaping corrupted nested JSON containing Windows paths before host projection. The adapter could no longer parse the business payload, leaving only DomainFailure and its summary.

## Steps
Recover the disk-backed terminal response through the codec, publish it through a small owned file queue, and query a persisted task binding through the real McpTaskAdapter. Convert the projection to the standard MCP result.

## Assertions
The adapter retains the exact business payload. The standard result remains an error with CS1029 and the complete diagnostic message, compile.v1 metrics and executable identity, and the original MCP task ID. No projection assertion is weakened.

## Expected Result
The decoder preserves exact JSON string semantics and diagnostics reach the existing projection unchanged. The fix does not relax projection assertions. Fixtures use only small owned files and launch no Unity Editor.

## Latest Run Result
2026-10-23 (Asia/Shanghai): this regression passed in the root-owned full .NET suite: 260 passed, 0 failed and 1 skipped (261 total), completed in 1.5686 minutes. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-codec-final.log. Original real legacy durable and corrupted host JSON evidence is saved under workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/lifecycle-codec-failure. This record does not claim that the full real lifecycle has passed.