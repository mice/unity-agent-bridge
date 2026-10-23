---
testId: AGBM_282
module: AgentBridge.MultiProject.History
testType: DotNet
target: Exact durable task codec recovery of nested compiler diagnostics
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/AgentTaskDurablePayloadRegressionTests.cs
testMethod: DurableCompilePayloadPreservesNestedJsonWindowsPathsAndUnicodeExactly
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_282

## Requirement
OpenSpec add-multi-project-routing MP-G04 / MP-G05: durable native task diagnostics remain readable after recovery and preserve their standard result contract.

## Risk
The fixed-B legacy lifecycle retained a valid Unity durable response, but chained codec unescaping corrupted nested JSON containing Windows paths before host projection. The adapter could no longer parse the business payload, leaving only DomainFailure and its summary.

## Steps
Write the real retained envelope shape with a serialized response containing a compile ToolResult and nested metricsObjectJson. Read it from disk through the codec. Also round-trip request strings with trailing backslash/quote, literal Unicode escapes, Windows path segments beginning r/n/t, control characters, Chinese text and emoji.

## Assertions
Recovered result reason and opaque business payload match the original strings exactly. Both business JSON and nested metrics JSON parse and preserve executable identity. Trailing slash/quote cannot consume the following request field.

## Expected Result
The decoder preserves exact JSON string semantics and diagnostics reach the existing projection unchanged. The fix does not relax projection assertions. Fixtures use only small owned files and launch no Unity Editor.

## Latest Run Result
2026-10-23 (Asia/Shanghai): this regression passed in the root-owned full .NET suite: 260 passed, 0 failed and 1 skipped (261 total), completed in 1.5686 minutes. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-codec-final.log. Original real legacy durable and corrupted host JSON evidence is saved under workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/lifecycle-codec-failure. This record does not claim that the full real lifecycle has passed.