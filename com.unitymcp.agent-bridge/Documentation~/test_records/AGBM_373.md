---
testId: AGBM_373
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: McpTaskAdapter.Unwrap
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/SchedulerTransportTests.cs
testMethod: PreflightModeRejectionPreservesEnvelopeDiagnosticInsteadOfMetricsOnly
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_373

## Requirement
SCHED-G01–G05 as applicable; add-editor-task-scheduling.

## Risk
An observation, transport adaptation, manual operation or storage error must not authorize conflicting execution or overwrite original outcomes.

## Steps and Assertions
Actual mode rejection metrics must not replace the outer error envelope. Preserve errorCode, summary and structured mode data.

## Evidence
Preflight diagnostic red: one failure plus one unchanged control case passed. Corrected transport/task/connection scope passed 41/41.
