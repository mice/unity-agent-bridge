# Changelog

All notable changes to `com.unitymcp.agent-bridge` are documented in this file.

The format follows Keep a Changelog and Semantic Versioning.

## [Unreleased]

## [1.2.8] - 2026-07-14

### Fixed

- Restored all default built-in plugin registrations when loading legacy Agent Bridge settings assets, so MCP ping, project info, and other frozen providers are available without recreating settings.
- Restored the governed `mcp__unity__project_get_info` C# MCP catalog entry and aligned the P4 verifier with the canonical project-info tool name.
- Updated the LuaTools runtime payload to Lua GC linter V2.

## [1.2.7] - 2026-07-06

### Added

- Added `UnityMcp.BuiltInPlugins.LuaTools` with dynamic `unity.lua.lint` and `unity.lua.compile` tools backed by the approved Windows x64 `lua-gc-lint.exe` payload.
- Added package and distribution boundaries for `Tools~/UnityAgentBridge/lua-gc-lint/out/win-x64/lua-gc-lint.exe`, including runtime materialization to `.unitymcp/runtime/UnityAgentBridge/lua-gc-lint/out/win-x64/lua-gc-lint.exe`.
- Added documented input, metrics, and report payload schemas for `unity.lua.lint` and `unity.lua.compile`.

### Changed

- Added `AgentBridgeSettings.luaSourceRoots` for no-argument Lua compile roots and validation that Lua paths stay under project `Assets/` or `Packages/`.

## [1.2.6] - 2026-06-26

### Added

- Added `UnityMcp.BuiltInPlugins.MonoBehaviourSemantics` with the dynamic tool `unity.mono.find_script_guid_usages` / `mcp__unity__mono_find_script_guid_usages` for read-only MonoBehaviour script GUID usage discovery.
- Added provider metadata and a `guid_text_scan` MVP provider that scans prefab and scene YAML assets for candidate MonoScript GUID references.
- Added AI Quick Connect actions in the MCP Setup window.

### Changed

- Existing `AgentBridgeSettings` assets now receive the new MonoBehaviour semantics built-in plugin registration when missing, so command list/catalog discovery includes the new tool without recreating settings.

### Fixed

- Fixed managed Codex MCP configuration generation so `unity_agent_bridge` launches with `UNITY_AGENT_BRIDGE_PROJECT_PATH` bound to the current Unity project.

## [1.2.5] - 2026-06-24

### Fixed

- Fixed Build Local Runtime package payload resolution for external Git UPM projects where the Unity project has its own `Tools/` directory and the installed Agent Bridge package lives under `Library/PackageCache`.

## [1.2.4] - 2026-06-24

### Added

- Added Setup window `Build Local Runtime` support that uses .NET 8 SDK and package-contained build inputs to generate the C# MCP CLI and Roslyn compiler proxy under the Unity project `.unitymcp/runtime` directory.

### Changed

- Package source and distribution builds now carry runtime build inputs and wrappers instead of generated `win-x64` executable payloads.
- MCP health/probe diagnostics now report `cliMode=project-local-runtime` for the generated project-local executable.

### Removed

- Removed generated package executable payloads from source Git tracking and removed package-local publish scripts that wrote build outputs back into `Tools~`.

## [1.2.3] - 2026-06-23

### Fixed

- Restored the required package-contained Windows executable payloads to Git UPM release tags so external Unity projects can run Prepare Runtime without local `dotnet publish`, NuGet restore, or maintainer-specific SDK version handling.

### Changed

- Updated repository ignore and release documentation so the required `win-x64` single-file payloads are tracked for tagged package releases while broader generated output remains ignored.

## [1.2.2] - 2026-06-23

### Added

- Added `UnityMcp.BuiltInPlugins.RoslynExecution` and the dynamic MCP tool `mcp__unity__execute_csharp`, gated by explicit project-local enablement and prepared runtime readiness.
- Added the package-contained single-file Roslyn compiler proxy payload at `Tools~/UnityAgentBridge/roslyn-execution/out/win-x64/unity-roslyn-compiler.exe`.

### Changed

- Raised the package minimum Unity version for Roslyn execution releases to `2022.3`.
- Updated package distribution to include built-in plugin source under `BuiltInPlugins/` and the Roslyn compiler proxy payload in Build delivery output.
- Made `UnityMcp.BuiltInPlugins.ProjectInfo` the only project metadata implementation surface and routed CLI `project_info` to `unity.project.get_info`.

### Removed

- Removed the legacy/core `unity.project_info` / `mcp__unity__project_info` tool names. Existing MCP callers should use `mcp__unity__project_get_info`.

## [1.2.1] - 2026-06-15

### Added

- Added `UnityMcp.Plugin.Abstractions` plus explicit `AgentBridgeSettings.pluginRegistrations` support for asmdef and managed DLL plugin discovery.
- Added Unity-side plugin discovery, schema resolution, and project-local plugin catalog export to `Library/AgentBridge/plugin-catalog.json`.
- Added the built-in asmdef sample plugin `UnityMcp.BuiltInPlugins.ProjectInfo`, exposing `unity.project.get_info` to Unity and `mcp__unity__project_get_info` to MCP.
- Added Unity EditMode plugin discovery coverage `AGB_156..AGB_160` and acceptance evidence for package-contained MCP plugin discovery.

### Changed

- Updated the C# MCP server to merge valid dynamic plugin tools with built-in tools and forward dynamic calls through the existing bridge queue.
- Updated package documentation, integration guidance, tool contract notes, and plugin discovery architecture guidance for the `1.2.1` release target.

## [1.1.7] - 2026-06-15

### Added

- Added governed `unity.read_report` support across the Unity package, external CLI, and C# MCP catalog for bounded Agent Bridge report reads.
- Added token-friendly acceptance evidence and repository-root test-record coverage for hierarchy, component-info, compile follow-up, report-reader behavior, and runtime test record path resolution.

### Changed

- Updated `unity.get_hierarchy` to the `hierarchy.v2` contract with default bounds `maxDepth=4`, `limit=150`, compact node summaries, and bounded follow-up/report guidance.
- Updated `unity.get_gameobject_component_info` and `unity.compile` to emit structured `details` / `followUp` metadata with report-first guidance and clean-compile no-follow-up behavior.
- Updated documentation governance and package-facing docs so schemas remain authoritative under `com.unitymcp.agent-bridge/Documentation~/schemas` and test records remain authoritative under `Documentation~/AgentBridge/test_records/`.

## [1.1.6] - 2026-06-14

### Added

- Added durable compile lifecycle epoch tracking for `unity.compile`, including reload-safe target correlation and staged compile lifecycle logs.
- Added structured stale-state diagnostics and project-binding evidence through compile metrics, bridge status, and `unity_bridge_health`.
- Added governed acceptance evidence and synchronized the `harden-unity-compile-state` OpenSpec change into the main observability specification.

### Changed

- Hardened `unity.compile` completion semantics so commands finalize only from matching lifecycle completion or deterministic timeout.
- Hardened status publication so `lastError` is no longer polluted by successful tool summaries.
- Updated observability documentation, tool contract, verifier output, and acceptance guidance for compile lifecycle and stale-state diagnosis.

### Notes

- Unity `2021` verification remains deferred; the current mandatory validation lane is Unity `2022.3.x`.

### Changed

- Extracted `com.unitymcp.agent-bridge` to the repository-root package source-of-truth and switched `UnityMCP` to consume it via `file:../../com.unitymcp.agent-bridge`. References: `PKG-FR-001`, `PKG-FR-002`
- Added package-distribution build and verification flow around `Build/com.unitymcp.agent-bridge/`, `Build-PackageDistribution.ps1`, and `Verify-Package-Distribution.ps1`. References: `PKG-FR-010`, `PKG-AC-015`
- Added historical external validation evidence, including archived MCP error matching and shared launcher binding coverage for direct MCP startup. References: `PKG-FR-009`, `PKG-FR-011`, `PKG-AC-013`, `PKG-AC-016`

### Notes

- This package-distribution change does not create a public release, registry publish, or new release tag.
- Unity `2021` verification remains deferred; the current mandatory validation lane is Unity `2022.3.x`.

## [1.1.5] - 2026-06-13

### Added

- Added the shared C# MCP host runtime under `UnityAgentBridge.Cli/UnityAgentBridge.Mcp` and archived the `rewrite-mcp-server-in-csharp` OpenSpec change.
- Added package-owned `Runtime/SharedProtocolCore` plus compatibility fixtures and verifiers for MCP catalog, response envelopes, and published runtime startup.

### Changed

- Replaced the TypeScript MCP runtime with the self-contained `unity-agent-bridge[.exe] mcp-server` host and shared `ExternalBridgeClientCore` path.
- Updated runtime preparation, managed client configuration, diagnostics, package distribution, and Git UPM verification to the prepared C# executable runtime flow.
- Updated Unity MCP diagnostics so prepared C# runtime payloads report authoritative runtime binding and server files status without false `MCP004`/`MCP006` failures.

### Removed

- Removed the package-contained TypeScript MCP server sources, Node runtime payload path, and redundant `UnityAgentBridge.Cli.exe` / package payload test-project artifacts from the delivered runtime.

## [1.1.4] - 2026-06-11

### Added

- Added read-only asset query tools for AssetDatabase search, Editor selection inspection, and GameObject component/property inspection.
- Added MCP wrappers for `mcp__unity__assetdatabase_search`, `mcp__unity__get_selection_info`, and `mcp__unity__get_gameobject_component_info`.
- Added package schemas and tool-contract documentation for the new asset query tools.

### Changed

- Refined GameObject component property payloads to use a machine-readable `value` model with explicit `propertyType`, `type`, `isUnityObject`, `isNull`, and `isContainer` semantics.

## [1.1.3] - 2026-06-10

### Added

- Added package-contained external `unity-agent-bridge[.exe]` CLI binaries under `Tools~/UnityAgentBridge/cli/out/<rid>/`.
- Added `net8.0` Console App source at `UnityAgentBridge.Cli/` with parser, queue, lifecycle, health, JSON output, and exit-code components, plus `UnityAgentBridge.Cli.sln`.
- Added MCP CLI resolver modes for env override, config override, package binary, Console App dev, and direct queue fallback.
- Added `resolvedCliPath`, `cliMode`, and `cliWarnings` diagnostics to MCP health/probe responses.

### Changed

- MCP bridge tools now resolve and spawn the external CLI by default, with TypeScript direct queue behavior retained only as an explicit migration fallback.
- `publish.ps1` now publishes the Console App product binary instead of the file-based script.

## [1.1.0] - 2026-06-06

### Added

- Editor-only `Editor/Mcp/` subsystem for local MCP settings, discovery, diagnostics, launcher scripts, and setup window workflows. References: `ADR-E01`, `ADR-E02`, `ADR-E03`, `ADR-E04`, `ADR-E05`, `ADR-E06`
- Project-local Codex and Claude Code configuration writers with managed block / structured merge flows and `.mcp.json` backup behavior. References: `ADR-E07`, `ADR-E13`
- `AsyncProcessRunner`, explicit cancellation-mode guardrails, editor lifecycle tokens, and auto-launch state machine hardening. References: `ADR-E11`, `ADR-E12`
- `MCP001` through `MCP011` quick diagnostics, readiness aggregation, redacted copy-report output, and watchdog-protected Unity verifier automation. References: `ADR-E08`, `ADR-E09`, `ADR-E10`, `ADR-E14`
- Package-level integration guide, acceptance runbook, and `EMCP-P0..P6` phase verifiers for the Editor MCP increment.

### Hardened

- `CancellationMode=Unspecified` is rejected at runtime instead of silently defaulting.
- Broken project `.mcp.json` files are backed up under `Library/AgentBridge/backups/` before replacement and logged via `mcp_json_parse_failed`.
- Project launchers resolve local `node_modules/tsx/dist/cli.mjs` and do not rely on `npx`.
- Auto-launch session state writes only occur on detached success paths.

## [1.0.0] - 2026-06-06

### Added

- UPM package layout with Editor-only asmdefs and zero Player runtime footprint. References: `ADR-001`, `ADR-002`
- File-queue bridge with `schemaVersion`, unified `ToolResult`, orphan recovery, FIFO queue behavior, and domain-reload-safe compile/test flows. References: `ADR-001`, `ADR-003`, `ADR-008`, `ADR-009`
- Built-in V1 tools:
  - `unity.ping`
  - `unity.project_info`
  - `unity.compile`
  - `unity.get_console`
  - `unity.run_static_method`
  - `unity.run_diagnostic`
  - `unity.run_editmode_tests`
  - `unity.run_playmode_tests`
- .NET 10 file-based CLI with frozen exit-code mapping and cross-RID publish matrix. References: `ADR-005`
- Optional TypeScript MCP server that only spawns the CLI. References: `ADR-006`
- Settings, logs, metrics, and integration-facing documentation. References: `ADR-007`, `ADR-010`

### Notes

- `Library/AgentBridge/` is the authoritative location for logs, metrics, and reports.
- `Temp/AgentBridge/` remains the session-scoped queue root.
- Maintainer-owned release actions such as tagging remain outside Implementer Agent scope.
