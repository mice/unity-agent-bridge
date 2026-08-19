---
testId: AGBM_221
module: AgentBridgeMcpSetup
testType: EditMode
target: AgentBridgeMcpSetupWindow
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/AgentBridgeMcpSetupWindowTests.cs
testMethod: SourceOnlyPublishedRuntimeIsEligibleForDownloadAndInstall
status: active
lastRun: ""
lastResult: unknown
---

# AGBM_221

## Requirement

The Download & Install action must remain available for a published source-only runtime with a valid immutable source identity.

## Assertions

- A source-only release with source URL and 40-character commit SHA is eligible.
- A source-only release missing the commit SHA remains disabled.
