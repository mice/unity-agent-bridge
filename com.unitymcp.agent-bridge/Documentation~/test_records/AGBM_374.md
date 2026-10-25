---
testId: AGBM_374
module: AgentBridge.EditorScheduling
testType: DotNetUnit
target: McpTaskAdapter.Unwrap
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/SchedulerTransportTests.cs
testMethod: TypedControlMetricsStillPreserveStableTaskAndScheduling
status: active
lastRun: "2026-10-25"
lastResult: passed
---

# AGBM_374

## Requirement
SCHED-G01–G05 as applicable; add-editor-task-scheduling.

## Risk
An observation, transport adaptation, manual operation or storage error must not authorize conflicting execution or overwrite original outcomes.

## Steps and Assertions
Typed control metrics continue to expose their original AgentTask identity, state and queued scheduling observation.

## Evidence
Preflight diagnostic red: one failure plus one unchanged control case passed. Corrected transport/task/connection scope passed 41/41.
