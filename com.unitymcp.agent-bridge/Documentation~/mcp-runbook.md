# EMCP Acceptance Runbook

This runbook is the package-facing entry point for acceptance agents validating the Editor MCP increment and the package-distribution migration.

## Preconditions

- Work from the repository root.
- The current package source of truth is `com.unitymcp.agent-bridge/`. The historical embedded path `UnityMCP/Packages/com.unitymcp.agent-bridge/` is no longer the editable package root.
- Confirm the status table in `Documentation~/Unity_Agent_Bridge_Editor_MCP_ImplementationPlan.md` is at `AwaitingAcceptance` or `Passed` for the phase you are about to verify.
- Use Unity `2022.3.53f1` for the current validation lane.
- Preserve `Temp/EMCP-P*` output directories until the acceptance report is written.
- For repository workflow and release-gate checks, confirm `openspec --version` is available and meets the minimum supported version `1.4.1`.
- For package distribution validation, also confirm `Documentation~/Unity_Agent_Bridge_Package_Distribution_ImplementationPlan.md` is at `AwaitingAcceptance` or `Passed` for the active `PKG-Pn` gate.

## OpenSpec Gate

Run the OpenSpec gate in addition to existing engineering verifiers. It does not replace them.

```powershell
pwsh Tools/AgentBridge/Verify-OpenSpec.ps1
```

Expected behavior:

- reports `assert:` lines for OpenSpec CLI availability, strict validation success, and no unarchived blocker changes
- reports `error:` lines when OpenSpec is missing, below minimum version, strict validation fails, an active change is missing `proposal.md`, blocker-change metadata is invalid, or an unarchived blocker remains
- does not call Unity verifiers
- does not convert engineering-gate success into OpenSpec-gate success

## Phase Verifier Order

Run the phase verifiers in order. Stop at the first non-zero exit code and record the failing phase in the acceptance report.

```powershell
pwsh Tools/AgentBridge/Verify-OpenSpec.ps1
pwsh Tools/AgentBridge/Verify-EMCP-P0.ps1
pwsh Tools/AgentBridge/Verify-EMCP-P1.ps1
pwsh Tools/AgentBridge/Verify-EMCP-UI.ps1
pwsh Tools/AgentBridge/Verify-EMCP-P3.ps1
pwsh Tools/AgentBridge/Verify-EMCP-P4.ps1
pwsh Tools/AgentBridge/Verify-EMCP-P5.ps1
```

## Expected Evidence

| Phase | Required Evidence |
|---|---|
| `OpenSpec` | `Documentation~/AgentBridge/acceptance/OS-2-yyyyMMdd.md` |
| `EMCP-P0` | `Documentation~/AgentBridge/acceptance/EMCP-P0-yyyyMMdd.md` |
| `EMCP-P1` | `Documentation~/AgentBridge/acceptance/EMCP-P1-yyyyMMdd.md`, `Temp/EMCP-P1/test-results.xml` |
| `EMCP-P2` | `Documentation~/AgentBridge/acceptance/EMCP-P2-yyyyMMdd.md`, UI smoke XML, manual window screenshots |
| `EMCP-UI` | `Documentation~/AgentBridge/acceptance/EMCP-UI-yyyyMMdd.md`, UI integration verifier output, manual readiness report |
| `EMCP-P3` | `Documentation~/AgentBridge/acceptance/EMCP-P3-yyyyMMdd.md`, `Temp/EMCP-P3/test-results.xml`, backup/log assertions |
| `EMCP-P4` | `Documentation~/AgentBridge/acceptance/EMCP-P4-yyyyMMdd.md`, `Temp/EMCP-P4/test-results.xml`, `Temp/EMCP-P4/unity-editmode.log` |
| `EMCP-P5` | `Documentation~/AgentBridge/acceptance/EMCP-P5-yyyyMMdd.md` |

## Manual Acceptance Checklist

Use `Documentation~/acceptance-checklist.md` as the canonical `EMCP-AC-001..013` checklist. The acceptance report should cite the checklist rows actually exercised in the current run.

## Package Distribution Notes

- Package distribution acceptance evidence is written to `Documentation~/AgentBridge/acceptance/PKG-Pn-yyyyMMdd.md`.
- Package distribution verifiers and build scripts live under `Tools/AgentBridge/`.
- Current package-install documentation is rooted at `com.unitymcp.agent-bridge/Documentation~/`.
- This workflow validates package reuse readiness only. It does not create tags, push remotes, or publish a registry package.

## Agent Bridge Self-Test

Use self-test when you need a structured health sweep of the Agent Bridge path instead of a single point check.

CLI:

```powershell
dotnet run --project UnityAgentBridge.Cli/UnityAgentBridge.Cli/UnityAgentBridge.Cli.csproj -- self-test --project-path D:\Path\To\UnityProject --timeout-ms 120000
```

MCP:

```text
mcp__unity__agent_bridge_self_test({ "timeoutMs": 120000 })
```

Contract notes:

- `timeoutMs` is the total suite budget.
- operation cases can first return `running` or `resuming`.
- terminal outer result is only `success` or `failed`.
- case-level details live in `metrics.cases[]`.

First-version fixed suite cases:

- `ping`
- `project_info`
- `console`
- `static_method_ok`
- `static_method_not_allowed`
- `unsupported_tool`
- `editmode_minimal`
- `diagnostic_scene`

Use self-test when:

- diagnostics are ambiguous and you need an end-to-end suite result
- validating a new environment before collecting acceptance evidence
- checking whether CLI, MCP, facade dispatch, and deferred test execution still align after changes

Prerequisite:

- self-test requires a live bridge heartbeat for the bound Unity project
- if `unity_bridge_health` reports no status file, no fresh `heartbeatAgeMs`, or a stale lifecycle state, treat self-test failure as a bridge-readiness problem first

## MCP Bridge Observability

Use observability tools when an MCP request appears to hang and you need segmented diagnosis instead of one opaque timeout.

Available MCP tools:

- `mcp_echo`
- `unity_bridge_health`
- `unity_bridge_submit_only`
- `unity_bridge_wait_result`

For External CLI validation, `unity_bridge_health` and MCP probe output must include the resolver decision fields:

- `resolvedCliPath`
- `cliMode`
- `cliWarnings`

Expected package-contained runtime mode is `package-binary`. `env-override` and `config-override` are valid when intentionally configured. `console-app-dev` and `direct-queue-fallback` must be recorded as fallback evidence and must not be treated as final external Git UPM release evidence unless the acceptance report explicitly calls out the reason.

Recommended sequence:

1. `mcp_echo`
2. `unity_bridge_health`
3. `unity_bridge_submit_only`
4. `unity_bridge_wait_result`

Codex-specific startup failure triage:

1. If Unity-side diagnostics pass but Codex reports `connection closed: initialize response`, inspect the workspace `.codex/config.toml` first.
2. The managed launcher path should point at `<UnityProject>/.unitymcp/runtime/AgentBridge/Start-UnityAgentBridge-Mcp.cmd`.
3. If the config still points at a stale project-local `Tools/AgentBridge/Start-UnityAgentBridge-Mcp.cmd`, treat that as a client-config drift issue, not a Unity bridge failure.
4. Re-run `Apply MCP Client Config` before collecting more MCP-side evidence.

Prepared runtime drift triage:

1. If `.unitymcp/runtime/UnityAgentBridge/cli/` still contains unexpected legacy files after `Prepare Runtime`, inspect `ToolsRoot` and any project-local `Tools/UnityAgentBridge` or `Tools/AgentBridge` directories.
2. Package-contained runtime preparation is expected to copy from `Packages/com.unitymcp.agent-bridge/Tools~`.
3. Stale project-local tools content can override the package payload and invalidate external release verification.

Diagnosis guide:

- `Documentation~/AgentBridge/debug_mcp_hang.md`

Manual acceptance guide:

- `Documentation~/AgentBridge/acceptance/mcp_bridge_observability_manual.md`

Lifecycle interpretation:

1. `lifecycleState = ready`
   - Bridge heartbeat is fresh.
2. `lifecycleState = starting`
   - Wait for the Unity bridge heartbeat to initialize.
3. `lifecycleState = stale`
   - Restart Unity or reconnect the MCP server.
4. `lifecycleState = reconnect-required`
   - Treat the current stdio session as invalid and reconnect.
5. `lifecycleState = shutting-down`
   - Wait for shutdown to finish, then reconnect.

## Token-Friendly MCP Usage

When using `mcp__unity__get_hierarchy`, `mcp__unity__get_gameobject_component_info`, or `mcp__unity__compile`, inspect `structuredContent.metrics.details` and `structuredContent.metrics.followUp` before issuing another broad Unity query.

Recommended pattern:

1. Start with bounded tool calls.
2. Use `followUp.options[0]` as the primary manual suggestion.
3. If the suggestion is `mcp__unity__read_report`, reuse the provided `reportPath` and `jsonPointer`.
4. Avoid `mcp__unity__get_console` after a clean `mcp__unity__compile` result with `followUp.recommended = false`.

Example MCP sequence:

```text
mcp__unity__get_hierarchy({ "locator": "currentScene" })
mcp__unity__get_gameobject_component_info({ "locator": "currentScene#Main Camera" })
mcp__unity__read_report({ "reportPath": "...", "jsonPointer": "/components" })
```

## MCP Session Scope

The MCP client binding is session-scoped.

- `Run Quick Diagnostics` validates the currently open Unity project inside that Editor.
- Codex `mcp__unity__*` calls validate whichever Unity project was bound when that Codex session started.
- An already-running Codex session does not automatically retarget when you close one Unity project and open another.

Acceptance implication:

- If a phase requires MCP-based evidence for a specific Unity project, start a fresh Codex session bound to that same active project before collecting `mcp__unity__project_get_info`, `mcp__unity__get_console`, or similar tool output.
- When collecting console evidence, prefer multi-type calls so one MCP round-trip can capture both `error` and `warning` buckets.
- Do not treat CLI output or a different Codex session's MCP output as equivalent to the required MCP-session evidence.
- When multiple Unity projects exist in the same repository, record which launcher/config source selected the project:
  - explicit launcher argument
  - `UNITY_AGENT_BRIDGE_PROJECT_PATH`
  - project-local MCP settings / `ToolsRoot`

This behavior is the current architecture of the local MCP workflow. It adds operator setup cost, but it is not classified as a product defect.

## Removed Codex Launcher

`Start-Codex-With-UnityMcp.cmd` has been removed from the supported workflow.

- Use project-local MCP settings and the direct MCP launcher as the main path.
- Persist `ToolsRoot` from the MCP Setup window when the tools live outside the Unity project root.

## Report Template

Capture these fields in the acceptance report:

- phase and verifier command
- Unity version
- result: `PASS` or `FAIL`
- evidence paths
- any `assert:` lines emitted by the verifier
- manual observations if the phase requires UI or client interaction

## Failure Handling

- Do not continue to the next phase after a failing verifier.
- Record the first failing command, exit code, and the final relevant log lines.
- Move the phase row in the status table back to `Doing` or `Failed` with a concrete note.
- If `Verify-OpenSpec.ps1` reports missing CLI, low version, missing proposal files, invalid proposal metadata, or active blocker changes, record that as an OpenSpec gate failure or unavailable state; do not rewrite it as an engineering-verifier result.
