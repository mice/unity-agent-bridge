---
testId: AGBM_221
module: AgentBridgeMcpSetup
testType: EditMode
target: AgentBridgeMcpSetupWindow
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/AgentBridgeMcpSetupWindowTests.cs
testMethod: SourceOnlyPublishedRuntimeIsEligibleForDownloadAndInstall
status: active
lastRun: "2026-08-21T12:01:45Z"
lastResult: passed
---

# AGBM_221

## Requirement

The Download & Install action must remain available for a published source-only runtime with a valid immutable source identity.

## Assertions

- A source-only release with source URL and 40-character commit SHA is eligible.
- A source-only release missing the commit SHA remains disabled.
