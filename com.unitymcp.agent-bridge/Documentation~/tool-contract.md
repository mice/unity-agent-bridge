# Tool Contract

## Shared Protocol

All tools consume an `AgentCommand` envelope:

```json
{
  "schemaVersion": "1.0",
  "commandId": "cmd_001",
  "tool": "unity.ping",
  "timeoutMs": 5000,
  "createdAt": "2026-06-06T00:00:00Z",
  "args": {}
}
```

All tools return a `ToolResult` envelope:

```json
{
  "schemaVersion": "1.0",
  "commandId": "cmd_001",
  "tool": "unity.ping",
  "success": true,
  "status": "success",
  "startedAt": "2026-06-06T00:00:01Z",
  "finishedAt": "2026-06-06T00:00:01Z",
  "durationMs": 12,
  "summary": "pong",
  "errors": [],
  "warnings": [],
  "logs": [],
  "metrics": {},
  "changedFiles": [],
  "reportPath": "Library/AgentBridge/reports/ping/cmd_001.json"
}
```

Status values used by V1:

- `pending`
- `running`
- `resuming`
- `success`
- `failed`
- `timeout`
- `blocked`
- `unsupported`
- `invalid_args`
- `exception`
- `cancelled`

Rules:

- `schemaVersion` mismatch on the command returns `unsupported` or `invalid_args` before tool execution.
- `args` and `metrics` are JSON objects, not double-encoded strings.
- `changedFiles` exists for protocol stability; V1 built-in tools return an empty array.
- `reportPath` is populated when a report file is written.

## Built-In Tools

### `unity.ping`

- Args schema: `schemas/unity.ping.args.schema.json`
- Side effect: `read`
- Typical success summary: `pong`
- Metrics:
  - `unityVersion`
  - `isCompiling`

### `unity.project.get_info`

- Args schema: inline plugin schema, empty object
- Side effect: `read`
- Typical success summary: `Project info collected.`
- Metrics:
  - `unityVersion`
  - `projectPath`
  - `activeBuildTarget`
  - `isCompiling`
  - `isUpdating`
  - `isPlaying`
  - `activeScene`

### `unity.compile`

- Args schema: `schemas/unity.compile.args.schema.json`
- Side effect: `validate`
- Reload behavior: yes; result may pass through `running` / `resuming`
- Typical terminal statuses:
  - `success`
  - `failed`
  - `timeout`
  - `exception`
- Metrics:
  - `contractVersion = compile.v1`
  - `errorCount`
  - `warningCount`
  - `diagnosticSampleCount`
  - `diagnosticSamples[]`
  - `isCompiling`
  - `observedEpochAtStart`
  - `targetEpoch`
  - `completedEpoch`
  - `compileEpoch`
  - `lifecycleStage`
  - `timeoutReason`
  - `reloadRestored`
  - `unknownEpochRestored`
  - `lastTransition`
  - `lastTransitionAtUtc`
  - `details`
  - `followUp`
- Errors:
  - compiler diagnostics populate bounded `errors[].code/file/line/column`
- Follow-up policy:
  - clean success with zero errors and zero warnings returns `followUp.recommended = false`
  - failed compile or warning-bearing compile recommends `unity.read_report` before any manual console read
- Report detail:
  - compile reports contain full diagnostics and lifecycle evidence for the executed compile operation

### `unity.get_console`

- Args schema: `schemas/unity.get_console.args.schema.json`
- Side effect: `read`
- Args:
  - `types: error[] | warning[] | info[]`
  - `count?: integer`
- Metrics:
  - `requestedTypes`
  - `requestedCountPerType`
  - `results[]`
- Each `results[]` item contains:
  - `type`
  - `returnedCount`
  - `entries[]`
- Each `entries[]` item contains:
  - `condition`
  - `stackTrace`
  - `type`
  - `timestamp`

### `unity.assetdatabase_search`

- Args schema: `schemas/unity.assetdatabase_search.args.schema.json`
- Side effect: `read`
- Required args:
  - `query`
- Optional args:
  - `folders`
  - `offset`
  - `limit`
  - `includeDetails`
- Deterministic defaults and bounds:
  - `offset = 0`
  - `limit = 20`
  - `includeDetails = false`
  - negative `offset` returns `invalid_args`
  - zero, negative, or `> 200` `limit` returns `invalid_args`
  - invalid numeric inputs are not clamped
- Metrics:
  - `query`
  - `folders`
  - `totalCount`
  - `returnedCount`
  - `offset`
  - `limit`
  - `truncated`
  - `nextOffset`
  - `results[]`
- Each `results[]` item contains:
  - `index`
  - `guid`
  - `name`
  - `locator`
  - `path`
  - `kind`
  - `type`
  - `extension`
  - `isFolder`
- Notes:
  - complete match sets are sorted by Unity asset path using ordinal comparison before paging
  - default metrics remain lightweight; detailed asset records are written to `reportPath`
  - `includeDetails = true` writes bounded dependency and sub-asset samples to the payload, each capped at 20 entries with explicit truncation flags

### `unity.get_editor_state`

- Args schema: `schemas/unity.get_editor_state.args.schema.json`
- Side effect: `read`
- Optional args:
  - none
- Metrics:
  - `editorState`
  - `runtimeMode`
  - `isCompiling`
  - `isUpdating`
  - `isPlaying`
  - `isPlayingOrWillChangePlaymode`
  - `activeScene`
  - `loadedScenes`
- Notes:
  - `editorState.activityState` is one of `idle`, `compiling`, `updating`, or `compiling_and_updating`
  - `editorState.sceneMutation.blockers` may include `compiling`, `updating`, `entering_play_mode`, `play_mode`, `exiting_play_mode`, `dirty_scene`, or `untitled_dirty_scene`
  - the tool always writes a `reportPath`

### `unity.open_scene`

- Args schema: `schemas/unity.open_scene.args.schema.json`
- Side effect: `mutate`
- Required args:
  - `scenePath`
- Optional args:
  - `mode`
  - `setActive`
  - `saveModifiedScenes`
- Deterministic defaults and bounds:
  - `mode = single`
  - `setActive = true`
  - `saveModifiedScenes = false`
  - only `single` and `additive` are valid `mode` values
  - invalid `mode` returns `invalid_args` and is not replaced with the default
- Metrics:
  - `scenePath`
  - `mode`
  - `setActive`
  - `savedModifiedScenes`
  - `alreadyLoaded`
  - `openedScene`
  - `activeScene`
  - `loadedScenes`
  - `editorState`
- Typical terminal statuses:
  - `success`
  - `invalid_args`
  - `blocked`
- Notes:
  - `scenePath` must resolve to an `Assets/**/*.unity` asset
  - dirty scenes block by default; `saveModifiedScenes = true` saves saveable dirty scenes non-interactively before opening
  - untitled dirty scenes return `blocked`; the tool does not show save prompts or invent scene paths
  - additive open is idempotent when the target scene is already loaded
  - the tool writes a `reportPath` for terminal results, including `invalid_args` and `blocked`

### `unity.get_hierarchy`

- Args schema: `schemas/unity.get_hierarchy.args.schema.json`
- Side effect: `read`
- Optional args:
  - `locator`
  - `maxDepth`
  - `limit`
  - `includeComponents`
- Deterministic defaults and bounds:
  - omitted, `null`, or empty `locator` is treated as `currentScene`
  - `maxDepth = 4`
  - `limit = 150`
  - `includeComponents = false`
  - negative `maxDepth` returns `invalid_args`
  - zero, negative, or `> 5000` `limit` returns `invalid_args`
  - invalid numeric inputs are not clamped
- Metrics:
  - `contractVersion = hierarchy.v2`
  - `target`
  - `rootCount`
  - `nodeCount`
  - `returnedNodeCount`
  - `truncated`
  - `limit`
  - `maxDepth`
  - `visitedCount`
  - `nodes[]`
  - `details`
  - `followUp`
- Each `nodes[]` item contains:
  - `nodeIndex`
  - `parentIndex`
  - `name`
  - `locator`
  - `path`
  - `scenePath`
  - `instanceId`
  - `depth`
  - `siblingIndex`
  - `activeSelf`
  - `activeInHierarchy`
  - `childCount`
  - `componentCount`
  - `isPrefabInstance`
  - `prefabAssetPath`
  - `components`
  - `componentsTruncated`
  - `hasMissingScripts`
- Notes:
  - `currentScene` enumerates only the active scene
  - additive loaded scenes require explicit `Assets/<scene>.unity` locators
  - bare scene root locators may return `success` with `rootCount = 0`, `nodeCount = 0`, and an empty `nodes` array
  - `selection:active` that does not resolve to a GameObject returns `invalid_args` and does not fall back to `currentScene`
  - supported locator forms are `currentScene`, `currentScene#A/B`, `Assets/X.unity`, `Assets/X.unity#A/B`, `Assets/X.prefab`, `Assets/X.prefab#A/B`, `selection:active`, and `instance:<id>`
  - `includeComponents = true` adds bounded component summaries with `index` and nullable `type` only; each node returns at most 8 summaries and uses `componentsTruncated` when additional components exist
  - default node summaries expose `componentCount` and `hasMissingScripts` without returning heavy component identity by default
  - the hierarchy contract version changed from `hierarchy.v1` to `hierarchy.v2`; callers should migrate any schema assumptions about component summaries and default bounds
  - the tool writes a `reportPath` for terminal results, including `invalid_args`
  - reports are complete only within the applied bounds and explicitly expose bounded completeness state

### `unity.get_selection_info`

- Args schema: `schemas/unity.get_selection_info.args.schema.json`
- Side effect: `read`
- Optional args:
  - `includeDetails`
- Metrics:
  - `selectionCount`
  - `active`
  - `counts`
  - `items[]`
- `counts` contains:
  - `assets`
  - `sceneObjects`
  - `components`
  - `other`
- Each `items[]` item contains:
  - `index`
  - `kind`
  - `name`
  - `locator`
  - `type`
- Notes:
  - the tool always writes a `reportPath`
  - payload identity records include `path`, `assetPath`, `hierarchyPath`, `scenePath`, `instanceId`, `guid`, `globalObjectId`, `isPersistent`, `isPrefabInstance`, and `prefabAssetPath`
  - component properties are intentionally not expanded by this tool

### `unity.get_gameobject_component_info`

- Args schema: `schemas/unity.get_gameobject_component_info.args.schema.json`
- Side effect: `read`
- Optional args:
  - `locator`
  - `componentName`
  - `componentIndex`
  - `propertyMode`
  - `propertyLimit`
  - `arrayElementLimit`
  - `stringMaxLength`
- Deterministic defaults and bounds:
  - omitted `locator` is treated as `selection:active`
  - `propertyMode = debug`
  - only `debug` and `serialized` are valid `propertyMode` values
  - invalid `propertyMode` returns `invalid_args` and is not replaced with the default
  - `propertyLimit = 200`
  - `arrayElementLimit = 20`
  - `stringMaxLength = 300`
  - `propertyLimit = 0` and `arrayElementLimit = 0` are valid
  - negative or out-of-range numeric values return `invalid_args`
  - invalid numeric inputs are not clamped
- Metrics:
  - `mode`
  - `target`
  - `componentQuery`
  - `componentCount`
  - `matchedCount`
  - `propertyCount`
  - `returnedPropertyCount`
  - `truncated`
  - `components[]`
  - `details`
  - `followUp`
- `mode` values:
  - `component_list`
  - `component_inspect`
- Each `components[]` item contains:
  - `index`
  - `name`
  - `type`
  - `scriptPath`
  - `propertyCount`
  - `returnedPropertyCount`
- Notes:
  - valid targets return `success` even when `componentName` matches zero components; use `matchedCount = 0`
  - list mode and inspect mode both write detailed payloads to `reportPath`
  - inspect mode keeps full serialized property records in the report payload rather than in ToolResult metrics
  - inspect mode may recommend `unity.read_report` when full property detail is useful
  - component payload records include `assemblyName`, `scriptGuid`, `scriptPath`, `properties`, and per-component truncation state
  - each serialized property record includes `path`, `propertyType`, `type`, `isUnityObject`, `isNull`, `isContainer`, and `value`
  - primitive leaves use machine-readable JSON primitives in `value`
  - Unity object references use bounded object summaries in `value` with `name`, `path`, `guid`, `instanceId`, and `isDestroyed`
  - serialized structs, classes, arrays, lists, and dictionary-like containers use `isContainer = true` and `value = null`, with child entries representing contents

### `unity.read_report`

- Args schema: `schemas/unity.read_report.args.schema.json`
- Side effect: `read`
- Required args:
  - `reportPath`
- Optional args:
  - `jsonPointer`
  - `offset`
  - `limit`
  - `maxBytes`
- Deterministic defaults and bounds:
  - `jsonPointer = ""` selects the full report root
  - `offset = 0`
  - `limit = 100`
  - maximum `limit = 500`
  - `maxBytes = 65536`
  - maximum `maxBytes = 262144`
  - invalid numeric inputs return `invalid_args`
- Metrics:
  - `contractVersion = report_read.v1`
  - `reportPath`
  - `jsonPointer`
  - `offset`
  - `limit`
  - `maxBytes`
  - `selectedIsArray`
  - `returnedCount`
  - `totalCount`
  - `nextOffset`
  - `truncated`
  - `byteCount`
  - `items`
  - `value`
- Notes:
  - reads are restricted to Agent Bridge JSON reports under the resolved report root
  - absolute paths, parent traversal, non-JSON files, and out-of-root paths return `invalid_args`
  - byte-limit overflow returns a terminal tool failure rather than partial JSON
  - the tool does not call Unity scene, asset, selection, compile, or object-inspection APIs

### `unity.run_static_method`

- Args schema: `schemas/unity.run_static_method.args.schema.json`
- Side effect: whitelist-controlled
- Required args:
  - `typeName`
  - `methodName`
- Optional args:
  - `parameters`
- Typical terminal statuses:
  - `success`
  - `invalid_args`
  - `unsupported`
  - `timeout`
  - `exception`
- Metrics:
  - `whitelistId`
  - `typeName`
  - `methodName`
  - `hadParameters`
- Notes:
  - only methods listed in `AgentBridgeSettings.allowedStaticMethods` can run
  - `TargetInvocationException` is unwrapped into `errors[]`

### `unity.run_diagnostic`

- Args schema: `schemas/unity.run_diagnostic.args.schema.json`
- Side effect: `read`
- Required args:
  - `diagnosticType`
  - `targetPath`
- V1 supported path:
  - `scene`
- V1 known-but-not-integrated types return `unsupported`
- Metrics:
  - `diagnosticType`
  - `targetPath`
  - `supported`
  - `assetGuid`
  - `assetType`
  - `exists`
  - `fileSizeBytes`
  - `dependencyCount`
  - `dependencySample`
  - `integrationPoint`
  - `note`

### `unity.run_editmode_tests`

- Args schema: `schemas/unity.run_editmode_tests.args.schema.json`
- Side effect: `validate`
- Optional args:
  - `filter`
- Metrics:
  - `tests[]`
  - `coverage`
- Each `tests[]` item contains:
  - `testId`
  - `fullName`
  - `outcome`
  - `durationMs`
  - `category`
  - `recordPath`
- Coverage payload in V1:
  - `enabled = false`
  - `lineCoverage = null`
  - `threshold = null`
  - `passed = null`

### `unity.run_playmode_tests`

- Args schema: `schemas/unity.run_playmode_tests.args.schema.json`
- Side effect: `validate`
- Optional args:
  - `filter`
- Reload behavior: yes; PlayMode enter/exit uses persisted run state
- Additional blocked cases:
  - Editor already in PlayMode
  - Editor compiling at launch time
- Metrics shape matches `unity.run_editmode_tests`

### `unity.agent_bridge_self_test`

- Args schema: `schemas/unity.agent_bridge_self_test.args.schema.json`
- Side effect: `validate`
- Optional args:
  - `includeEditModeCase?: boolean`
  - `includeDiagnosticCase?: boolean`
  - `continueOnFailure?: boolean`
  - `timeoutMs?: integer`
- Typical non-terminal statuses:
  - `running`
  - `resuming`
- Typical terminal statuses:
  - `success`
  - `failed`
- Metrics:
  - `suiteVersion`
  - `overallPassed`
  - `caseCount`
  - `passedCount`
  - `failedCount`
  - `cancelledCount`
  - `cases[]`
- Each `cases[]` item contains:
  - `id`
  - `scenario`
  - `tool`
  - `expectedStatus`
  - `actualStatus`
  - `passed`
  - `summary`
  - `durationMs`
  - `startedAt`
  - `finishedAt`
  - `reportPath`
  - `warnings[]`
  - `errors[]`
  - `metrics`
- Notes:
  - outer terminal result is intentionally constrained to `success` or `failed`
  - `timeoutMs` is the total suite budget, not a single case timeout
  - operation cases may first return `running` or `resuming` while waiting for outbox terminal results
  - first-version fixed case matrix:
    - `ping`
    - `project_info`
    - `console`
    - `static_method_ok`
    - `static_method_not_allowed`
    - `unsupported_tool`
    - `editmode_minimal`
    - `diagnostic_scene`

## CLI and MCP Mapping

CLI subcommands:

- `ping -> unity.ping`
- `project_info -> unity.project.get_info`
- `compile -> unity.compile`
- `console -> unity.get_console`
- `assetdatabase_search -> unity.assetdatabase_search`
- `get_editor_state -> unity.get_editor_state`
- `open_scene -> unity.open_scene`
- `get_hierarchy -> unity.get_hierarchy`
- `read_report -> unity.read_report`
- `get_selection_info -> unity.get_selection_info`
- `get_gameobject_component_info -> unity.get_gameobject_component_info`
- `run-static -> unity.run_static_method`
- `diagnostic -> unity.run_diagnostic`
- `test-edit -> unity.run_editmode_tests`
- `test-play -> unity.run_playmode_tests`
- `self-test -> unity.agent_bridge_self_test`

MCP tool names:

- `mcp__unity__ping`
- `mcp__unity__project_get_info`
- `mcp__unity__compile`
- `mcp__unity__get_console`
- `mcp__unity__assetdatabase_search`
- `mcp__unity__get_editor_state`
- `mcp__unity__open_scene`
- `mcp__unity__get_hierarchy`
- `mcp__unity__read_report`
- `mcp__unity__get_selection_info`
- `mcp__unity__get_gameobject_component_info`
- `mcp__unity__run_static_method`
- `mcp__unity__run_diagnostic`
- `mcp__unity__run_editmode_tests`
- `mcp__unity__run_playmode_tests`
- `mcp__unity__agent_bridge_self_test`

## GameObject Locator Contract

GameObject-oriented tools use a single canonical `locator` string field.

Supported forms:

- `selection:active`
- `instance:<id>`
- `currentScene#A/B`
- `Assets/X.prefab`
- `Assets/X.prefab#A/B`
- `Assets/Scenes/X.unity#A/B`

Rules:

- hierarchy paths use `/` as the segment separator
- hierarchy segments are exact GameObject names compared ordinally
- leading slash, trailing slash, empty segments, and `#` inside hierarchy segments return `invalid_args`
- no hierarchy escape syntax is supported in v1
- GameObject names containing `/` or `#` are not addressable by hierarchy locators in v1
- active and inactive GameObjects both participate in hierarchy lookup
- duplicate hierarchy matches resolve to the first deterministic pre-order traversal match
- scene asset locators only resolve scenes already open or loaded in the current Editor process
- `instance:<id>` locators are only valid within the current Editor process

ToolResult and report examples for the asset query tools must preserve the same protocol split as existing tools: compact `metrics` in the envelope, large identity/detail payloads behind `reportPath`.

## MCP Bridge Observability Tools

### `mcp_echo`

- execution path: local MCP server only
- does not touch the Unity queue
- intended use: distinguish local MCP transport failure from Unity bridge failure

### `unity_bridge_submit_only`

- execution path: MCP bridge wrapper
- required args:
  - `tool`
- optional args:
  - `args`
  - `timeoutMs`
- result:
  - returns `commandId`
  - does not wait for Unity terminal result

### `unity_bridge_wait_result`

- execution path: MCP bridge wrapper
- required args:
  - `commandId`
  - `timeoutMs`
- result:
  - waits for `outbox/{commandId}.result.json`
  - returns `timeout` instead of waiting forever

### `unity_bridge_health`

- execution path: MCP bridge wrapper
- result fields:
  - `queueRoot`
  - `inboxCount`
  - `processingCount`
  - `outboxRecentCount`
  - `statusFileExists`
  - `heartbeatAgeMs`
  - `lifecycleState`
  - `healthReason`
  - `reconnectRequired`
  - `recommendedActionCode`
  - `recommendedAction`
  - `toolExecution`
  - `currentCommandId`
- `currentStage`
- `isCompiling`
- `isUpdating`
- `isPlaying`
- `projectPath`
- `currentCompileEpoch`
- `activeTargetEpochs`
- `activeCompileCommandIds`
- `compileLifecycleStage`
- `compileLastTransition`
- `compileLastTransitionAtUtc`
- `compileTimeoutReason`
- `stalePrimaryClassification`
- `staleEvidencePriorityPath`
- `staleHeartbeatAgeMs`
- `staleConfiguredProjectPath`
- `staleDetectedProjectPath`
- `staleProjectBindingKind`
- `staleRuntimeIdentity`
- `lastError`
- lifecycle interpretation:
  - `lifecycleState` is a compact server availability state: `starting`, `ready`, `degraded`, `stopping`, or `stopped`
  - `healthReason` explains degraded evidence, such as `UnityUnavailable`, `ProjectMismatch`, `RuntimePathMismatch`, `BridgeQueueUnavailable`, `ShutdownRequested`, or `None`
  - `recommendedActionCode` gives machine-readable recovery guidance, such as `Retry`, `Reconnect`, `RestartUnity`, `UpdateConfig`, `StopServer`, or `None`
  - `recommendedAction` gives the human-readable recovery message
  - `toolExecution` is `Allowed`, `BlockedBeforeDispatch`, or `RetryableTimeout`
  - `reconnectRequired` remains for compatibility and is derived from the recommended action / execution decision
  - stale sessions, project mismatches, and reconnect-required conditions are represented as `degraded` plus reason/action details rather than as top-level lifecycle states
- `statusPath`

## Plugin Discovery Contract

Plugin-provided MCP tools are dynamic rather than hard-coded in the package tool list.

Boundary note for the plugin abstraction layer:

- `AgentCommand` and `ToolResult` are the shared bridge protocol envelopes used by the Unity host and external CLI/MCP layers.
- `UnityMcp.Plugin.Abstractions` should expose plugin-local context and result contracts instead of re-exporting those host protocol envelopes.
- The Unity host adapter is responsible for mapping host protocol requests and results at the plugin boundary.
- `UnityMcp.Plugin.Abstractions` does not reference `UnityMcp.AgentBridge.SharedProtocolCore`, `UnityEngine`, or `UnityEditor`.
- Plugin tools should use `UnityMcpToolStatus` constants for status values.
- The Unity host adapter normalizes plugin results before they become shared `ToolResult` envelopes:
  - null plugin results become failed tool results
  - empty status values are derived from the plugin `Success` flag
  - empty summaries receive stable defaults
  - invalid or non-object `MetricsObjectJson` values become `{}` with a warning
- Project metadata callers should use the plugin-owned `mcp__unity__project_get_info` tool. The legacy/core `mcp__unity__project_info` and `unity.project_info` names are removed from the default shipped surface and are not aliased.

Rules:

- Unity-side plugin discovery is driven only by `AgentBridgeSettings.pluginRegistrations`.
- The Unity host exports valid plugin tools to `Library/AgentBridge/plugin-catalog.json`.
- The MCP server reads that catalog and merges valid entries with built-in MCP tools.
- Missing, invalid, stale, or partially rejected plugin catalogs must not remove built-in MCP tools.
- Built-in bridge tools always win conflicts over plugin tools.

Plugin catalog entry contract:

- `pluginId`
- `pluginVersion`
- `assemblyName`
- `bridgeTool`
- `mcpName`
- `title`
- `description`
- `defaultTimeoutMs`
- `allowedRuntimeModes`
- `sideEffect`
- `mayTriggerDomainReload`
- `inputSchemaJson`

Schema source rules:

- `InlineJson`
  - export the declared JSON object directly after validation
- `AssetPath`
  - resolve from the active Unity project under `Assets/...`
- `PackagePath`
  - resolve from the active Unity project under `Packages/...`
- `EmbeddedResource`
  - resolve from the providing assembly resource stream

Plugin discovery proof:

- ProjectInfo validates the plugin-owned project metadata path through `unity.project.get_info` / `mcp__unity__project_get_info`
- generic dynamic catalog behavior remains covered by non-project-info plugin tools such as EditorBasics or test fixtures
- plugin tools still cannot override framework-owned names such as `unity.ping` or `unity.compile`

## Stage Logging Contract

Log schema requirements:

- `logVersion = 1.0`
- `schemaVersion = 1.0`
- `timestamp`
- `stage`
- `commandId`
- `tool`
- `status`
- `message`

MCP stages:

- `mcp.received`
- `mcp.write_command`
- `mcp.wait_result`
- `mcp.read_result`
- `mcp.return_response`

Unity stages:

- `unity.poller.pickup`
- `unity.tool.start`
- `unity.tool.finish`
- `unity.write_result`
- `compile_requested`
- `compile_started`
- `assembly_finished`
- `compile_finished`
- `compile_restored`
- `compile_timeout`
- `compile_result_written`
