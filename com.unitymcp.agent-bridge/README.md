# Unity Agent Bridge

`com.unitymcp.agent-bridge` is an Editor-only Unity package that provides the local Unity automation bridge, the frozen v1.0 tool surface, the v1.1 MCP Setup & Diagnostics workflow, and the v1.2 plugin discovery and Roslyn execution loop.

## Scope

- Unity validation lane: `2022.3.x`
- Declared package compatibility: `2022.3+`
- Runtime support: none; this package is Editor-only
- External requirements for MCP workflows: a supported MCP client such as Codex or Claude Code. The product CLI binary is self-contained and shipped in release tags; `.NET` is needed only for maintainer development builds and source-level diagnostics.

## Install

### Local file dependency

For repository-local development in `unity-agent-bridge-workbench/UnityMCP/Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unitymcp.agent-bridge": "file:../../../unity-agent-bridge/com.unitymcp.agent-bridge"
  },
  "testables": [
    "com.unitymcp.agent-bridge"
  ]
}
```

For another Unity project outside this repository, point the `file:` dependency at the package root directory itself:

```json
{
  "dependencies": {
    "com.unitymcp.agent-bridge": "file:D:/Path/To/com.unitymcp.agent-bridge"
  }
}
```

Do not copy the package into `Assets/`.

### Git UPM dependency

Documented release-style Git UPM usage:

```json
{
  "dependencies": {
    "com.unitymcp.agent-bridge": "git+https://github.com/mice/unity-agent-bridge.git?path=/com.unitymcp.agent-bridge#v1.2.3"
  }
}
```

Release validation for `v1.2.3` requires the tag, `package.json` version, `CHANGELOG.md` entry, and package-contained Windows executable payloads to align. The release tag carries the required `win-x64` executables so external Unity projects do not need local publish, NuGet restore, or maintainer SDK version alignment.

## First Use

1. Open the Unity project and wait for package resolve and script compilation.
2. Open `Edit -> Project Settings -> Agent Bridge` and create the settings asset if needed.
3. Open `Tools -> Unity Agent Bridge -> MCP Setup & Diagnostics`.
4. Confirm bridge, executable runtime, runtime binding, `.NET` SDK, CLI, and MCP status.
5. Review project-level client config before applying changes.
6. Run Quick Diagnostics when validating the local environment.

MCP setup resolves the external CLI in this order: `UNITY_AGENT_BRIDGE_CLI_PATH`, config `cliPath`, package `Tools~/UnityAgentBridge/cli/out/<rid>/unity-agent-bridge[.exe]`, Console App dev fallback, then direct queue migration fallback. `unity_bridge_health` reports `resolvedCliPath`, `cliMode`, and `cliWarnings`.

## Plugin Discovery

Version `1.2.x` supports Unity-side plugin discovery for explicitly registered asmdef and managed DLL providers.

- Plugin authors compile against `com.unitymcp.plugin-abstractions`, which provides the `UnityMcp.Plugin.Abstractions` assembly and `UnityMcp.Plugin` namespace.
- Projects that install Agent Bridge from local Git/file dependencies must also make `com.unitymcp.plugin-abstractions` resolvable until a registry-based dependency flow is available.
- Registration lives in `AgentBridgeSettings.pluginRegistrations`.
- Discovery inspects only enabled registrations; unregistered provider assemblies are ignored.
- Valid plugin tools are exported to `Library/AgentBridge/plugin-catalog.json`.
- The external MCP server reads that catalog and exposes dynamic `mcp__unity__*` tools without loading Unity plugin assemblies directly.
- Built-in/core tools cannot be overridden by plugins; plugin-owned tools appear only through the exported catalog.

Project metadata is plugin-owned. Keep `UnityMcp.BuiltInPlugins.ProjectInfo` enabled to expose `unity.project.get_info` and `mcp__unity__project_get_info`; the legacy/core `unity.project_info` and `mcp__unity__project_info` names are not shipped as aliases.

Roslyn execution is available only on Unity `2022.3.x` for this release line. The package ships the external compiler proxy at `Tools~/UnityAgentBridge/roslyn-execution/out/win-x64/unity-roslyn-compiler.exe`, and `mcp__unity__execute_csharp` stays hidden until the project explicitly enables Roslyn execution and Prepare Runtime materializes that payload into `.unitymcp/runtime/`.

## Documentation Index

- `Documentation~/integration-guide.md`: installation, Unity project wiring, CLI usage, and MCP setup workflow
- `Documentation~/mcp-runbook.md`: acceptance and verifier runbook for EMCP and package distribution changes
- `Documentation~/acceptance-checklist.md`: `EMCP-AC-001..013` checklist plus package distribution evidence cross-reference
- `Documentation~/tool-contract.md`: frozen tool contract for the bridge and CLI surface
- `Documentation~/integration-guide.md#plugin-discovery`: plugin registration model, catalog path, and schema source rules
- `Documentation~/schemas/`: authoritative public package schemas that ship with this package

## TestId And Tombstone Rules

- `AGB_*` belongs to the v1.0 bridge baseline. `AGBM_*` belongs to the v1.1 Editor MCP subsystem.
- Test IDs are append-only. Deleted or retired IDs are never reused.
- Any deleted `AGBM_###` test must be recorded in `Documentation~/Unity_Agent_Bridge_Editor_MCP_ImplementationPlan.md` section `7. TestId Tombstone`.
- Any deleted `AGB_###` test must be recorded in the corresponding v1.0 implementation-plan tombstone table.
- A replacement test must allocate a new ID and a new record under repository evidence path `Documentation~/AgentBridge/test_records/`.
- Renaming a test method does not permit reassigning its historical TestId to another behavior.

## Documentation Maintenance Rules

- Keep package-facing integration guidance under `Documentation~/`.
- Keep repository acceptance reports under `Documentation~/AgentBridge/acceptance/`.
- Update the relevant implementation-plan status row in the same change that adds or revises acceptance evidence.
