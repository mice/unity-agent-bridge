---
testId: AGBM_218
module: AgentBridgeMcpSetup
testType: EditMode
target: AgentBridgeMcpSetupWindow
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/AgentBridgeMcpSetupWindowTests.cs
testMethod: RuntimeCompatibility_PrereleasesRequireExactPackageAndRuntimeIdentity
status: active
lastRun: "2026-08-18T15:36:19Z"
lastResult: passed
---

# AGBM_218

## Requirement

Prerelease package and runtime versions require an exact identity match before MCP readiness is reported as ready.

## Assertions

- Matching prerelease identities are ready.
- Differing prerelease identities are blocking and name the exact-match rule.
- Stable patch-version differences retain warning-only compatibility behavior.
