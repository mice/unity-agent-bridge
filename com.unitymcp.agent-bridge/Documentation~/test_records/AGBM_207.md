---
testId: AGBM_207
module: AgentBridgeRuntimeSelection
testType: EditMode
target: McpPathResolver
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpPathResolverTests.cs
testMethod: MachineMode_UninstalledSelectedVersion_DoesNotFallbackToProjectRuntime
status: active
lastRun: "2026-08-15"
lastResult: passed
---

# AGBM_207

## Requirement

A selected machine-runtime version is authoritative. Until it is downloaded locally, the project-local runtime must not be used as a fallback.

## Assertions

- Runtime mode remains `machine` and exposes the selected version.
- MCP server root, CLI root, and launcher path are unavailable when that version is not installed.
- A project-local runtime presence cannot satisfy machine mode.
