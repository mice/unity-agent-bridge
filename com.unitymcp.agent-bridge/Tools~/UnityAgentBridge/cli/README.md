# Unity Agent Bridge CLI

This directory contains the package-contained Unity Agent Bridge external CLI.

The product path is the published `unity-agent-bridge[.exe]` binary under `out/<rid>/`.

## Prerequisites

- Normal binary usage: no .NET SDK/runtime install is required.
- Development publish/build: .NET SDK with `net8.0` support.
- A running Unity Editor bridge session for the target project

## Run

```powershell
.\out\win-x64\unity-agent-bridge.exe ping
.\out\win-x64\unity-agent-bridge.exe project_info
.\out\win-x64\unity-agent-bridge.exe compile
.\out\win-x64\unity-agent-bridge.exe console --type error --type warning --count 20
.\out\win-x64\unity-agent-bridge.exe run-static UnityMcp.AgentBridge.AgentBridgeStaticMethodSelfTests.SelfTestOk
.\out\win-x64\unity-agent-bridge.exe diagnostic scene Assets/Scenes/AppMain.unity
.\out\win-x64\unity-agent-bridge.exe test-edit
.\out\win-x64\unity-agent-bridge.exe test-edit UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests.DemoEditModeProbe_Passes
.\out\win-x64\unity-agent-bridge.exe test-edit --test-name UnityMcp.AgentBridge.Tests.AgentBridgeEditModeProbeTests.DemoEditModeProbe_Passes
.\out\win-x64\unity-agent-bridge.exe test-edit --group UnityMcp.AgentBridge.Tests
.\out\win-x64\unity-agent-bridge.exe test-play UnityMcp.AgentBridge.Tests.AgentBridgePlayModeProbeTests.DemoPlayModeProbe_PassesAfterOneFrame
.\out\win-x64\unity-agent-bridge.exe self-test --timeout-ms 120000
.\out\win-x64\unity-agent-bridge.exe bridge-health
.\out\win-x64\unity-agent-bridge.exe bridge-submit-only unity.ping --timeout-ms 15000
.\out\win-x64\unity-agent-bridge.exe bridge-wait-result <commandId> --timeout-ms 15000
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

## Publish

```powershell
./publish.ps1
```

`publish.ps1` emits self-contained single-file outputs from `UnityAgentBridge.Cli` with `PublishAot=false`.

Outputs are written to `./out/<rid>/unity-agent-bridge[.exe]`.
