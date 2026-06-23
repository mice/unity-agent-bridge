# Unity Agent Bridge

Offline MCP-compatible AI agent bridge for Unity editor automation.

This repository contains the product source for Unity Agent Bridge:

- `com.unitymcp.agent-bridge/` - Unity UPM package.
- `UnityAgentBridge.Cli/` - .NET CLI and MCP server/runtime sources.

The companion private workbench repository is `unity-agent-bridge-workbench`. It hosts Unity validation projects, OpenSpec records, release tooling, and acceptance evidence, and consumes this repository as a git submodule.

## Unity Package

The Unity package ID is:

```text
com.unitymcp.agent-bridge
```

Package-level documentation lives in:

```text
com.unitymcp.agent-bridge/README.md
com.unitymcp.agent-bridge/Documentation~/
```

For local Unity development from the workbench, the package is referenced with a `file:` dependency that points at `unity-agent-bridge/com.unitymcp.agent-bridge`.

## Repository Layout

```text
UnityAgentBridge.Cli/
  UnityAgentBridge.Cli.sln
  UnityAgentBridge.Cli/
  UnityAgentBridge.Mcp/
  UnityAgentBridge.ExternalBridgeClientCore/
  UnityAgentBridge.RoslynCompiler/
  UnityAgentBridge.Cli.Tests/

com.unitymcp.agent-bridge/
  package.json
  Editor/
  Runtime/
  Tests/
  BuiltInPlugins/
  Tools~/
  Documentation~/
```

## Build Notes

The repository tracks source code, Unity package files, tests, scripts, documentation, and the Windows single-file executable payloads required for Git UPM releases.

Published Git UPM tags must include these package-contained executables so Unity projects can run `Prepare Runtime` without building locally, installing a .NET SDK, restoring NuGet packages, or resolving maintainer-specific toolchain versions:

```text
com.unitymcp.agent-bridge/Tools~/UnityAgentBridge/cli/out/win-x64/unity-agent-bridge.exe
com.unitymcp.agent-bridge/Tools~/UnityAgentBridge/roslyn-execution/out/win-x64/unity-roslyn-compiler.exe
```

Maintainers may use the publish scripts under the CLI and Roslyn compiler projects to refresh those payloads before preparing a package distribution. Consumer projects should use the tagged payloads instead of local publish.

## License

Apache License 2.0. See `LICENSE`.
