---
testId: AGBM_298
module: AgentBridge.MultiProject.Discovery
testType: DotNet
target: Only native sharing conflicts qualify for heartbeat retry
testFile: UnityAgentBridge.Cli/UnityAgentBridge.Cli.Tests/ProjectDiscoveryReadRetryTests.cs
testMethod: CorruptMissingAndNonSharingFailuresAreNotRetried
status: active
lastRun: "2026-10-23"
lastResult: passed
---

# AGBM_298

## Requirement
OpenSpec add-multi-project-routing MP-G02 / MP-G03: current complete heartbeat evidence determines readiness; transient atomic-publication sharing conflicts may be retried within a bounded observation, without restoring readiness from history.

## Risk
Windows heartbeat replacement can temporarily deny a reader, while arbitrary retry or stale-cache fallback could hide corrupt/missing evidence or authorize an unavailable instance.

## Steps
Inject malformed JSON, missing file and a non-sharing IO HRESULT in separate data rows. Tests use the real injected read boundary with a fixed clock and in-memory registry; no filesystem project or process is created.

## Assertions
Each failure performs one read only and remains explicitly unreadable/notready. Invalid evidence cannot turn into a success. Production freshness and 200ms/10ms retry limits are unchanged. Existing root snapshot flag tests remain part of the related discovery suite.

## Expected Result
Current complete evidence is re-read after a transient sharing conflict; sustained or invalid evidence remains unavailable.

## Latest Run Result
2026-10-23 (Asia/Shanghai): passed in root-owned discovery/identity/catalog focused verification, 58/58 cases in 6.4797 seconds; outer process 58938 exit 0. Stable evidence: workbench Documentation~/AgentBridge/acceptance/multi-project-20261023/dotnet-status-read-final.log. Source review passed. The Windows-only 200ms retry window is not a hard IO wall-clock guarantee; 10s freshness remains unchanged. No new full-suite or complete real A/B run success is claimed.