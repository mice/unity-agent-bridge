---
testId: AGBM_279
module: AgentBridge.MultiProject.Discovery
testType: DotNet
target: Host discovery and persisted instance evidence
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/ProjectDiscoveryLifecycleTests.cs
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_279

## Requirement
OpenSpec add-multi-project-routing MP-G01 / MP-G02 / MP-G04: missing status retains attribution and only verified original process exit proves offline.

## Assertions
Missing/unreadable status uses the verified original PID and creation time across host reconnects. Dead, live and unobservable process providers exercise distinct outcomes. Corrupt observations, invalid identity and unregistered candidates cannot invent offline evidence. Timestamp strings preserve all seven fractional digits and Z. Tests use small owned files and injectable process evidence.

## Latest Run Result
2026-10-23 (Asia/Shanghai): the new Discovery regression group passed **22 cases** in the prior root-owned .NET run. That whole-suite run **failed overall: 253 passed, 3 failed, 1 skipped (257 total)**; these focused passes do not make that suite successful. Workbench log: `Temp/AgentBridge-MultiProject/dotnet-discovery-final.log`. The subsequent root-owned full run completed successfully: **256 passed, 0 failed, 1 skipped (257 total)** in 2.4948 minutes; log `Temp/AgentBridge-MultiProject/dotnet-final-reviewed.log`, completed 2026-10-23 02:42:53 Asia/Shanghai. This supersedes the earlier failed whole-suite result while retaining its history.

The root-reviewed real Codex-client core scenario passed separately. Original A instance `23756:639282902577577320` was observed exiting using its original process handle at `2026-10-22T18:33:19.0405749Z`; the same MCP connection `2efd7ddab2854d70b24d88b835011f82` subsequently reported A as `offline / EDITOR_EXITED`, B as `online / READY`, and read A's retained historical report. This is actual graceful-exit discovery evidence, not merely an injected process-provider assertion.

Evidence: [root-reviewed client completion](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-complete.json), [original process exit observation](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-A-exit-observation.json), [client final observations](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-final.json). The client's own final JSON left overall `passed` null pending parent interval review; the separate completion file records root's reviewed `passed: true`. This record covers Discovery; it does not declare the entire release or remaining validation complete.
