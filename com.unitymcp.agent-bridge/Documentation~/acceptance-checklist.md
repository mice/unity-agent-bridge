# EMCP Acceptance Checklist

This checklist is the package-level reference for `EMCP-AC-001..013`. Each item maps to the primary evidence path that an acceptance agent should cite in a formal report.

| Acceptance Id | Requirement Summary | Primary Evidence Pointer |
|---|---|---|
| `EMCP-AC-001` | The window opens from the menu and shows bridge, executable runtime, runtime binding, `.NET`, CLI, and MCP status without destabilizing the Editor. | `Documentation~/AgentBridge/acceptance/EMCP-P2-20260606.md`, `Documentation~/AgentBridge/acceptance/EMCP-P4-20260606.md` |
| `EMCP-AC-002` | Codex and Claude Code project configuration can be previewed, applied, removed, copied, and revealed. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md`, `com.unitymcp.agent-bridge/Documentation~/integration-guide.md` |
| `EMCP-AC-003` | `.codex/config.toml` managed-block apply/remove is atomic and `codex resume` preserves session continuity. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |
| `EMCP-AC-004` | Runtime preparation and diagnostics require the packaged executable payload and must not depend on `node_modules` or `npm install`. | `Documentation~/AgentBridge/acceptance/CSHARP-MCP-COMPATIBILITY-20260613.md`, `com.unitymcp.agent-bridge/Documentation~/integration-guide.md` |
| `EMCP-AC-005` | Quick Diagnostics shows `MCP001` through `MCP011` and maps `Ready`, `Degraded`, and `Unavailable` correctly. | `Documentation~/AgentBridge/acceptance/EMCP-P4-20260606.md` |
| `EMCP-AC-006` | The user can copy a redacted report and open `Library/AgentBridge/logs/`; the report includes versions, paths, results, and durations. | `Documentation~/AgentBridge/acceptance/EMCP-P4-20260606.md`, `com.unitymcp.agent-bridge/Documentation~/integration-guide.md` |
| `EMCP-AC-007` | User-global Codex and Claude configuration stays unchanged; only project-level config is mutated. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |
| `EMCP-AC-008` | Install, probe, and version checks keep the Editor responsive; closing the window or reloading the domain leaves no update-callback residue. | `Documentation~/AgentBridge/acceptance/EMCP-P2-20260606.md`, `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |
| `EMCP-AC-009` | v1.0 tool names, `ToolResult`, command schema, and queue protocol remain frozen; the UI does not read or write the command queue directly. | `Documentation~/AgentBridge/acceptance/EMCP-P0-20260606.md` |
| `EMCP-AC-010` | EditMode tests cover config parsing, status mapping, version decisions, command construction, and diagnostics aggregation. | `Documentation~/AgentBridge/acceptance/EMCP-P1-20260606.md`, `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md`, `Documentation~/AgentBridge/acceptance/EMCP-P4-20260606.md` |
| `EMCP-AC-011` | A pre-existing Codex session can be resumed after applying project MCP config, and `/mcp` shows `unity_agent_bridge`. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |
| `EMCP-AC-012` | Auto-launch is off by default and, when enabled, starts the preferred client exactly once per editor session without launching in batchmode or tests. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |
| `EMCP-AC-013` | Project-level `.mcp.json` lets Claude Code list Unity MCP tools and successfully ping or fetch project info. | `Documentation~/AgentBridge/acceptance/EMCP-P3-20260606.md` |

## Package Distribution Cross-Reference

Package distribution acceptance for `extract-agent-bridge-package` is tracked in repository-level evidence, not package-shipped reports.

| Package Phase | Evidence Pointer |
|---|---|
| `PKG-P0` | `Documentation~/AgentBridge/acceptance/PKG-P0-20260607.md` |
| `PKG-P1` | `Documentation~/AgentBridge/acceptance/PKG-P1-20260607.md` |
| `PKG-P2` | `Documentation~/AgentBridge/acceptance/PKG-P2-20260607.md` |
| `PKG-P3..P7` | `Documentation~/AgentBridge/acceptance/PKG-Pn-yyyyMMdd.md` |
