---
id: AGBM_206
module: AgentBridge.Mcp
target: MachineRuntimeLocator
testFile: Packages/com.unitymcp.agent-bridge/Tests/Editor/Mcp/MachineRuntimeSelectionTests.cs
testMethod: MachineSelection_ListsPublishedVersionsWithLocalInstallState
category: AGBM_RuntimeSelection
---

## Intent

The setup panel must offer valid published release tags from the new runtime line while clearly distinguishing a locally installed runtime from one that still requires download.

## Preconditions

The manager root contains published release manifests under `releases/<version>` and only one matching installed runtime under `versions/<version>`.

## Assertions

- Valid published releases are listed in descending semantic-version order.
- Releases older than `1.2.12-rc.2` are excluded.
- The release tag and artifact URL are retained for the UI.
- The installed state is true only when the matching local runtime passes validation.
- A manifest whose declared version differs from its release directory is excluded.
