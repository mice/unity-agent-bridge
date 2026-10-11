---
testId: AGBM_233
category: AGBM_P3
target: UnityMcp.AgentBridge.Mcp.GrokProjectConfigWriter
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpProcessAndConfigTests.cs
---

# AGBM_233

## Purpose

Verify that MCP Apply refreshes the managed launcher settings without deleting client-owned permission fields on the managed server entry.

## Expected Evidence

- `GrokProjectConfigWriter_Apply_PreservesPermissionFieldsOnManagedEntry` passes.
- `enabled` and `approval_mode` remain after Apply.
- The stale managed command is replaced.
