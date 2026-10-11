---
testId: AGBM_243
module: AgentBridge.PiConfiguration
testType: EditMode
target: PiAgentRunnerProjectSettings / PiAgentTemplateCatalog
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/AgentBridgeMcpSetupWindowTests.cs
testMethod: PiConfigurationValidator_RejectsUnsafeTool
status: active
lastRun: "2026-10-08T22:14:02+08:00"
lastResult: passed
---

# AGBM_243

## Requirement

The configure-pi-agent-templates change requires invalid project policies to remain visible and prevent process startup, while the catalog reflects the same bounded policy as execution.

## Risk

Silent fallback or mismatched display can hide an invalid execution policy.

## Steps

Run `PiConfigurationValidator_RejectsUnsafeTool` with a deterministic settings file or fake process; no Pi service or network is used.

## Assertions

The assertions in `PiConfigurationValidator_RejectsUnsafeTool` verify the identified configuration, catalog, or startup boundary.

## Expected Result

All assertions pass without starting a real Pi process.

## Latest Run Result

Passed in Unity 2022.3.53f1 batchmode EditMode on 2026-10-08 (0.000716 seconds); the focused suite passed 68/68. See CONFIGURE-PI-AGENT-TEMPLATES-20261008 acceptance evidence.
