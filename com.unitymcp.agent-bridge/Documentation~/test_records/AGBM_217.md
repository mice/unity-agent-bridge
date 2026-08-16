---
testId: AGBM_217
module: AgentBridgeRuntimeSelection
testType: EditMode
target: McpDiagnosticsRunner
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/McpDiagnosticsTests.cs
testMethod: RunAsync_MachineRuntimeWithCompiledRoslyn_DoesNotRequireBuildSource
status: active
lastRun: "2026-08-16"
lastResult: passed
---

# AGBM_217

## Requirement

An installed prebuilt machine runtime must be diagnosed from its selected binary payload and must not require package build-source files inside the machine version directory.

## Assertions

- MCP011 reports that Roslyn build input is not applicable for the prebuilt machine runtime.
- MCP012 resolves the Roslyn compiler from the selected machine version.
- The absence of `UnityAgentBridge.RoslynCompiler.csproj` does not produce a diagnostic error.
