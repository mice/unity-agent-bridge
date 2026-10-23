---
testId: AGBM_278
module: AgentBridge.MultiProject.Discovery
testType: DotNet
target: Host discovery and persisted instance evidence
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/ProjectDiscoverySnapshotTests.cs
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_278

## Requirement
OpenSpec add-multi-project-routing MP-G01 / MP-G02 / MP-G04: retain project attribution, explicit unavailable diagnostics and reliable online/offline evidence.

## Assertions
Corrupt registration siblings are explicitly diagnosed, and readiness uses fresh root flags. Fixtures use owned temporary files and injected process evidence; no Editor or host process is launched.

## Latest Run Result
2026-10-23 (Asia/Shanghai): the new Discovery regression group passed **22 cases** in the prior root-owned .NET run. That whole-suite run **failed overall: 253 passed, 3 failed, 1 skipped (257 total)**; these focused passes do not make that suite successful. Workbench log: `Temp/AgentBridge-MultiProject/dotnet-discovery-final.log`. The subsequent root-owned full run completed successfully: **256 passed, 0 failed, 1 skipped (257 total)** in 2.4948 minutes; log `Temp/AgentBridge-MultiProject/dotnet-final-reviewed.log`, completed 2026-10-23 02:42:53 Asia/Shanghai. This supersedes the earlier failed whole-suite result while retaining its history.

The root-reviewed real Codex-client core scenario passed separately. Original A instance `23756:639282902577577320` was observed exiting using its original process handle at `2026-10-22T18:33:19.0405749Z`; the same MCP connection `2efd7ddab2854d70b24d88b835011f82` subsequently reported A as `offline / EDITOR_EXITED`, B as `online / READY`, and read A's retained historical report. This is actual graceful-exit discovery evidence, not merely an injected process-provider assertion.

Evidence: [root-reviewed client completion](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-complete.json), [original process exit observation](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-A-exit-observation.json), [client final observations](../../../../unity-agent-bridge-workbench/Documentation~/AgentBridge/acceptance/multi-project-20261023/final-codex-run/codex-final.json). The client's own final JSON left overall `passed` null pending parent interval review; the separate completion file records root's reviewed `passed: true`. This record covers Discovery; it does not declare the entire release or remaining validation complete.
