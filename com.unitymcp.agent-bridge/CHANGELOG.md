# Changelog

All notable changes to `com.unitymcp.agent-bridge` are documented in this file.

The format follows Keep a Changelog and Semantic Versioning.

## [Unreleased]

## [1.4.3-rc.3] - 2026-10-17

### Fixed

- Deliver completed EditMode test results using the admitted queue path even when Unity unloads transient settings. Retain pending terminal output across write failures and reloads without rerunning tests.
- Preserve one transient default-settings instance through test-runner asset cleanup and release it on reload or Editor exit.

## [1.4.3-rc.2] - 2026-10-16

### Fixed

- Discover the installed immutable tag's runtime from validated Unity Git package identity even when its bundled catalog omits itself. Reconcile source conflicts and reject mismatched immutable cache reuse; honor custom machine roots during installation.
- Filter lower-base RCs after merging every publication source and fallback. Retain same-base candidates, supported stable releases, installed-version management and exact pins; expose catalog/identity diagnostics.

## [1.4.3-rc.1] - 2026-10-16

### Fixed

- Accept genuine compile-tool report references from durable compile and MCP-retained report storage in both template and dedicated analysis entrypoints. Preserve legacy references and citation identity while rejecting ambiguous, malformed, oversized or escaping evidence before runner startup.

### Added

- Add provider-neutral execution observations to task snapshots, persisted results and MCP lifecycle projection. Distinguish admission rejection, process startup, guard readiness, prompt submission and terminal outcome; unsupported legacy observations remain unknown.
- Matching optional Pi plugin source captures actual task-owned child PID/timestamps and retains startup milestones across success, failure, timeout and cancellation. Readiness version probes do not count as execution.

### Release status

- RC candidate only. Stable 1.4.3 requires candidate validation and separate maintainer promotion. Source-only publication; generated runtime/plugin binaries remain local.

## [1.4.2] - 2026-10-15

### Added

- Discover built-in and independent project templates through `unity.agent_task_template.list`; disabled/unavailable definitions remain visible with source, schemas, versions and actionable diagnostics, without requiring Pi.
- Execute registered definitions through task-capable `unity.agent_task_template.run` with strict templateId/inputs admission. Load versioned `ProjectSettings/AgentTaskTemplates.json` manually and reject unknown schemas, conflicting IDs and unsupported executor settings.
- Register provider-neutral executors and explicitly support configured prompts through the optional Pi plugin within captured provider budgets and an empty file-read scope.

### Changed

- Route the two unchanged dedicated analysis tools/IDs through the common admission, executor and output-validation pipeline. Reload configuration for new admissions while existing tasks retain immutable definition/input/authority snapshots and results.
- Keep internal task control out of public MCP and ordinary command discovery. Preserve rejected creation results through the durable dispatch journal without allocating an internal task or replaying work.

### Compatibility

- Rebuild the matching 1.4.2 local runtime and restart MCP. Existing analysis inputs/results and ordinary, modern and legacy task workflows remain compatible; SDK remains 2.2.0.
- Rebuild the optional Pi plugin from workbench v1.4.2 source when using project-defined templates; this source snapshot requires the new Agent Bridge 1.4.2 executor contracts. Configuration UI and live model validation are outside this release.
- Source-only publication continues: no generated executable, DLL or binary archive is uploaded. See `Documentation~/AgentTaskTemplates.md` for configuration and schema limits.

## [1.4.1] - 2026-10-11

### Fixed

- Publish the complete compile structured-result JSON Schema through MCP `outputSchema`, including diagnostic bounds, completeness, report/task metadata and pre-diagnostic failure shapes; synchronize package documentation and embedded runtime resources.
- Return standard immutable MCP task results: business failures complete with `isError=true`, while protocol execution errors remain failed. Validate inputs before allocation/dispatch and prevent ordinary fallback or replay after interrupted creation.
- Retain project-bound task identities, dispatch correlation and terminal reports in Library across host restart and compile domain reload. Bound observation/recovery deadlines, admission, record size and terminal storage with startup/periodic cleanup.
- Preserve unsupported Unity compile/test cancellation as an explicit disposition. Persist accepted cancellation intent so SDK wait cancellation cannot produce duplicate native cancellation or replace a terminal result.
- Return current compiler errors directly with code/message/file/line/column, separate warning/count/epoch metadata, explicit truncation and retained readable reports. Historical Console errors no longer contaminate compile results; timeout/interruption retains partial diagnostics and checks deadlines in completion callbacks as well as Editor updates.
- Reattach supported EditMode runs and expose explicit stale PlayMode recovery when the runner handle is unavailable, without launching a replacement run.
- Prevent concurrent stage-log writes or an unavailable log file from failing tool execution or replacing a task's durable result.

### Compatibility

- Keep SDK 2.2.0, existing public tool inputs, ordinary/down-level calls and legacy endpoints. Limit modern Tasks to compile, test and enabled fixed analysis tools; quick queries and helpers remain ordinary.
- Optional Pi keeps its existing plugin version and installation boundary. Rebuild the local runtime from matching core source. See `Documentation~/mcp-tasks-1.4.1.md` for retention, cancellation and result handling.

## [1.4.0] - 2026-10-11

### Added

- Runtime-neutral Agent Runtime contracts and Fake Runner coverage, composed with AgentTaskCore lifecycle and stable task identities.
- Intelligent-analysis schema, template registry and bounded capability validation, plus the Unity programmer and Unity asset analyst Specialists.
- Fixed verified compile-failure and asset-problem analysis tools with closed problem/evidence/timeout inputs, cited structured output and synchronous/MCP task execution.
- Optional source-built `com.unitymcp.pi-agent` managed-DLL UPM plugin with explicit installation/enablement and a reusable provider seam for future execution adapters.

### Changed

- **Breaking installation change:** Agent Bridge alone no longer contains Pi or advertises its analysis tools. Build/install and enable the separate Pi plugin, and update the MCP runtime from matching core source; ordinary core tools remain available without it.
- Move Pi configuration and effective-template UI into the plugin. Valid legacy `pi*` settings migrate atomically and once to `Library/AgentBridge/plugins/pi-agent/settings.json`; explicit new settings take precedence and invalid policy remains unavailable. Unrelated/opaque core settings are preserved.
- Use `deepseek/deepseek-flash` as the safe default model. Settings only reduce fixed-template tools and budgets; callers cannot override model, prompt, paths or tools.
- Enforce a hard provider-request ceiling including retries, cancel uncounted compaction and apply captured logical/physical read scope to read/grep/find/ls, including links and recursive searches.
- Preserve matching providers and tasks during plugin refresh. Disable/removal/readiness loss withdraws execution and cancels owned processes while existing terminal task identities/results remain queryable in the current Editor domain.

### Fixed

- Correct verified-analysis string payload dispatch, SDK task binding and terminal failure/cancellation projection.
- Reject malformed structured claims and unknown citations without filtering them into successful output.
- Preserve known guard failures during process cleanup and retain bounded, credential-redacted model diagnostics.
- Handle concurrent bridge-heartbeat reads with bounded retries and shared-read access.

Pi integration remains an initial capability with two fixed templates; a broader useful workflow chain and a Codex CLI adapter are future work. DeepSeek live compile/asset/cancellation acceptance is recorded separately from offline plugin extraction regressions. Source-only distribution continues; generated executables and DLL artifacts are built locally and are not hosted release assets. Pi plugin 0.1.0 requires Agent Bridge 1.4.0 and an MCP runtime built from matching source. MCP SDK 2.x long-task optimization belongs to the following version.

## [1.3.1] - 2026-09-16

### Fixed

- Preserve client-owned MCP permission fields when Apply refreshes the managed bridge configuration.
- Keep the Unity package, source-only runtime build inputs, and installed machine runtime as separate delivery layers.

## [1.3.0] - 2026-09-14

### Added

- Promote the accepted AgentTask compile and PlayMode task workflows to the stable release channel.

## [1.3.0-rc.2] - 2026-09-14

### Fixed

- Align RC release identity with the immutable product commit and manifest.

## [1.3.0-rc.1] - 2026-09-14

### Added

- Add compile and PlayMode AgentTask execution through the MCP task control surface.

## [1.2.12-rc.12] - 2026-09-14

### Fixed

- Include AgentTaskCore and AgentTaskEditor assemblies required by AgentBridge.Editor.
## [1.2.12-rc.11] - 2026-09-13

### Fixed

- Align published runtime catalog entries with immutable RC tags for setup panel selection.

## [1.2.12-rc.10] - 2026-09-13

### Fixed

- Resolve the machine runtime Roslyn compiler payload from the selected runtime version during fallback initialization.

## [1.2.12-rc.8] - 2026-09-13

### Added

- Introduce the AgentTask core, Unity Editor task execution service, MCP task control tools, and task-backed EditMode test execution.
- Preserve legacy EditMode test filters and result projections while exposing task lifecycle, recovery, timeout, and domain-failure evidence.

## [1.2.12-rc.7] - 2026-09-03

### Fixed

- Make source-only machine runtime installation resolve PowerShell and .NET executables from Unity's process environment and standard Windows installation paths.
- Align the source runtime catalog with the immutable rc.7 source commit.

## [1.2.12-rc.6] - 2026-09-01

### Fixed

- Map all eight frozen Android Debugging external-plugin bridge names to their canonical MCP names in both the Unity catalog producer and package-contained MCP runtime.
- Keep the two shared mapper source copies identical and preserve the conservative generic future-tool grammar.
- Resolve PowerShell and .NET executables from the Unity process environment when building a machine runtime from tag source.
- Fall back to standard Windows installation paths so source-only runtime installation does not depend on Unity inheriting the interactive shell PATH.

## [1.2.12-rc.5] - 2026-08-29

### Changed

- Breaking: extracted Lua lint and compile tools into the explicitly enabled `com.unitymcp.lua-gc-lint` package while preserving their bridge and MCP names.
- Removed the built-in LuaTools source, Agent Bridge Lua source-root setting, packaged linter payload, and project-local runtime copy step.

## [1.2.12-rc.4] - 2026-08-23

### Fixed

- Resolve the Roslyn compiler from the effective project-local or selected machine runtime without falling back across runtime modes.
- Keep Roslyn invocation source, assemblies, reports, and temporary output under the invoking Unity project's `Temp/AgentBridge` tree.
- Require and diagnose the canonical machine-runtime Roslyn compiler payload at `runtime/win-x64/unity-roslyn-compiler.exe`.

## [1.2.12-rc.3] - 2026-08-18

### Fixed

- Persist Lua and Roslyn execution reports with the same `reportPath` returned in tool metrics.
- Block prerelease package/runtime identity mismatches before MCP readiness is reported as ready.
- Preserve structured `unity.get_hierarchy` failures for invalid `dontDestroyOnLoad` locators.
- Consolidate hierarchy discovery on `unity_hierarchy_get` and the `hierarchy.v1` contract; the former v2 label is not supported.
- Accept JSON Schema `type` arrays, including nullable hierarchy and component query arguments, before MCP tool dispatch.
- Key cached source-only runtime archives by immutable commit SHA so an RC tag republish cannot reuse stale source.

## [1.2.12-rc.2] - 2026-08-16

### Added

- Added a published machine-runtime version picker with install-state labels and direct download progress in setup Step 1.
- Added verified project-cache reuse and tag-source build fallback for release assets that are unavailable.

### Fixed

- Made machine-runtime diagnostics and preparation follow the selected machine version and report incomplete Roslyn source payloads.

## [1.2.12-rc.1] - 2026-08-15

### Changed

- Breaking: replaced all legacy `mcp__unity__*` MCP names with stable domain-first `unity_<domain>_<verb>[_<detail>]` names; no compatibility aliases are provided.
- Added the shared MCP name mapper for existing bridge commands and future `unity.<domain>.<verb>[.<detail>]` plugin commands.

### Added

- Added an optional `query_only` execution policy for `unity.execute_csharp` / `mcp__unity__execute_csharp`.
- Query-only policy rejects known project mutations and escape-hatch APIs before compiler invocation while preserving trusted mode as the default for existing callers.

## [1.2.11] - 2026-08-06

### Fixed

- Fixed hierarchy metrics serialization by keeping follow-up argument objects as CLR dictionaries instead of nested Unity Newtonsoft `JObject` values.

## [1.2.10] - 2026-08-05

### Fixed

- Fixed `unity.get_hierarchy` report serialization for `dontDestroyOnLoad` results containing nested arrays and follow-up JSON objects.
- Added explicit EditMode and PlayMode coverage for Play Mode-only `DontDestroyOnLoad` discovery and empty root behavior.

## [1.2.9] - 2026-08-05

### Added

- Added DontDestroyOnLoad hierarchy discovery and locator support for special runtime scenes, including deterministic root ordering and inactive object coverage.
- Added focused EditMode and PlayMode coverage for DontDestroyOnLoad hierarchy queries and locator behavior.

### Changed

- Updated hierarchy and component query reports, schemas, and integration documentation to expose special-scene metadata and main-stage classification.

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

