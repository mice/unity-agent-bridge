# Unity Agent Bridge CLI

This directory is the package marker for the Unity Agent Bridge external CLI.
The package carries build inputs under `Tools~/UnityAgentBridge/src` and build
wrappers under `Tools~/UnityAgentBridge/runtime-build`.

Generated executables are project-local runtime artifacts. Build them from the
Unity MCP Setup window with **Build Local Runtime**; the normal product path is:

```text
<UnityProject>/.unitymcp/runtime/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe
```

## Prerequisites

- Unity Editor 2022.3.
- .NET 8 SDK for Build Local Runtime.
- A running Unity Editor bridge session for the target project

## Run

```powershell
$cli = "<UnityProject>\.unitymcp\runtime\UnityAgentBridge\cli\out\win-x64\unity-agent-bridge.exe"
& $cli ping
& $cli project_info
& $cli compile
& $cli console --type error --type warning --count 20
& $cli self-test --timeout-ms 120000
& $cli bridge-health
```

Use `--project-path` to point at a specific Unity project if auto-detection is not appropriate.

Development equivalents:

```powershell
dotnet run --project ..\..\..\UnityAgentBridge.Cli\UnityAgentBridge.Cli.csproj -- ping
```

Structured test-filter notes:

- no filter arguments: run the full test set for the selected mode
- legacy `filter`: backward-compatible single-string selection
- structured arrays:
  - `--test-name`
  - `--assembly`
  - `--category`
  - `--group`
- structured wildcard values containing `*` or `?` are intentionally rejected

Validation:

```powershell
pwsh Tools\AgentBridge\Verify-UnityTestFilterArgs.ps1
```

Self-test notes:

- `self-test` runs the fixed Agent Bridge suite and can return `running` or `resuming` before the final result.
- outer terminal result is limited to `success` or `failed`.
- `--timeout-ms` is the total suite budget.

Observability notes:

- `bridge-health` reports queue/status health without invoking a Unity tool.
- `bridge-submit-only` writes the command and returns `commandId`.
- `bridge-wait-result` waits for a terminal result with a bounded timeout.
- use `debug_mcp_hang.md` when a command appears stuck after submission.

## Build

```powershell
..\runtime-build\Build-LocalRuntime.ps1 -UnityProjectPath <UnityProject>
```

The build wrapper emits self-contained single-file outputs into
`<UnityProject>/.unitymcp/runtime` and never writes generated executables into
the package directory.
