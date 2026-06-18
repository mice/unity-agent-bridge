# AGBM_180

- testMethod: RunAsync_ProbePingBlocked_IncludesLayeredHealthDetails
- phase: EMCP-P4
- category: AGBM_P4

## Purpose

Verify that MCP setup diagnostics surface layered bridge lifecycle details when probe ping is blocked.

## Setup

- Use a fake process runner.
- Return a completed MCP probe payload where `pingResult` is blocked and `healthResult.structuredContent` reports `lifecycleState`, `healthReason`, `recommendedActionCode`, `recommendedAction`, and `toolExecution`.

## Assertions

- `MCP010` is `Error`.
- `MCP010.Details` includes `lifecycleState=degraded`.
- `MCP010.Details` includes `healthReason=ProjectMismatch`.
- `MCP010.Details` includes `recommendedActionCode=UpdateConfig`.
- `MCP010.Details` includes `toolExecution=BlockedBeforeDispatch`.

## Risk Covered

The setup panel could otherwise show only a generic ping failure and hide the project-binding or reconnect guidance needed to recover the MCP session.
