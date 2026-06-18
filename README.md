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

The repository tracks source code, Unity package files, tests, scripts, and documentation. Generated build outputs are ignored.

Package-contained executables under these paths are release artifacts and are not committed to source control:

```text
com.unitymcp.agent-bridge/Tools~/UnityAgentBridge/cli/out/
com.unitymcp.agent-bridge/Tools~/UnityAgentBridge/roslyn-execution/out/
```

Use the publish scripts under the CLI and Roslyn compiler projects to regenerate release payloads when preparing a package distribution.

## License

Apache License 2.0. See `LICENSE`.
