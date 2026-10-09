# Pi Agent template configuration

## Where to configure it

Open **Tools → Unity Agent Bridge → MCP Setup & Diagnostics**, expand **Pi Agent / Verified Templates**, edit the Pi runner fields, then select **Save / Validate**. The project-local JSON is stored at:

```text
Library/AgentBridge/mcp-editor-settings.json
```

The default policy is:

| Setting | Default |
| --- | --- |
| Pi schema (`piSchemaVersion`) | `1.0` |
| Executable | `pi` |
| Model | `deepseek/deepseek-flash` |
| Timeout | `120000` ms |
| Max turns | `8` |
| Max output | `65536` bytes |
| Tools | `read`, `grep`, `find`, `ls` |

Only these read-only tools can be selected. Shell, PowerShell, edit, write, network access, user extensions, skills, sessions, and raw Pi arguments are not configurable through this surface. The bridge internally loads its fixed package-owned budget guard while keeping extension discovery disabled.

## Built-in verified templates

The catalog is generated from the same Specialist factories used by execution:

- `unity_compile_failure_analysis` — `unity-programmer`, `compile-report` evidence.
- `unity_asset_problem_analysis` — `unity-asset-analyst`, `asset-report` evidence.

Both templates read bounded project folders (`Assets`, `Packages`, `ProjectSettings`, and `Documentation~`), deny generated folders (`Library`, `Temp`, `Logs`, and `Build`), have no write permission, disable network access, and validate structured JSON output with evidence citations.

The catalog displays the effective specialist, evidence type, executable/model, read-only Pi tools, paths, budgets, write/network policy, and validation state. The tool set is the intersection of project settings and the template's abstract capabilities; budgets use the lower project/template limit. With the default settings both templates have a 6-turn policy budget, 131072 evidence bytes, 32768 output bytes, and a 90000 ms timeout ceiling. Template identity and permissions are not editable in the UI.

Older settings files that omit Pi fields receive safe defaults. Explicit unknown tools, empty tool lists, invalid budgets, unsupported Pi schemas, forbidden Pi fields, and malformed JSON remain unavailable with `PI_CONFIGURATION_INVALID`; they never silently become an executable default policy. Use **Save / Validate** after correcting the displayed values, or **Reset Safe Defaults**, to repair the file. Invalid saves leave the existing file unchanged.

## Delegation flow

The public task-capable MCP tools are `unity_analysis_compile_failure` and `unity_analysis_asset_problem`. The Editor bridge uses the internal `unity.agent_task_control` command to create/query/result/cancel their fixed `analysis_compile_failure` and `analysis_asset_problem` tasks. Generic `agent.run` remains absent from the MCP catalog.

1. Invoke the fixed MCP tool with `problemId`, `evidenceIds`, and optional `timeoutMs`, requesting task execution. The coordinator maps this to internal `create` with the fixed analysis task type.
2. Unity validates the request, loads the evidence bundle, resolves the registered template, and starts a project-local AgentTask.
3. The Editor bridge starts Pi with the validated project runner settings and the template's read-only capability.
4. Query the returned MCP task identity through the task lifecycle (`tasks/get`; the retained `tasks/result` adapter retrieves a terminal result). Internally this uses `query`/`result`.
5. `tasks/cancel` requests process termination for a running task through internal `cancel`.

For example, the payload shape is:

```json
{
  "problemId": "compile-001",
  "evidenceIds": ["report-001"],
  "timeoutMs": 90000
}
```

Extra fields such as `model`, `tools`, `prompt`, `working_directory`, or raw Pi flags are rejected as forbidden overrides. An invalid local configuration or unregistered template fails before Pi starts and is shown in the setup window and task error.

The project timeout is bounded to 1000–900000 ms, turns to 1–64, and output to 1024–1048576 bytes. The closed analysis request permits a timeout of 1–90000 ms; execution takes the minimum of the request, project, and template ceilings, including request timeouts below 1000 ms.

The turn budget is a hard ceiling on Pi provider invocations: the fixed guard runs before `before_provider_request`, admits at most the effective project/template minimum, and terminates before invocation N+1 with `PI_TURN_LIMIT_EXCEEDED`. Pi retries consume the same counter; session events do not reset it. Compaction is cancelled because Pi 0.85.1 uses a separate summarizer outside that hook. This counts Pi provider invocations, not lower-level HTTP transport retries inside a provider SDK call.

Hard enforcement currently supports the verified Pi **0.85.1** baseline. The adapter probes the configured launcher before model execution and reports `PI_TURN_LIMIT_UNSUPPORTED` for other/unknown versions. It uses closed RPC mode, sends the task only after a nonce/budget-matching guard handshake, returns only final assistant text, and closes the process after completion. Missing/invalid guard readiness and malformed protocol output fail closed; partial text never becomes successful analysis after a budget failure. No text-mode fallback is used.

Repeatable local checks: `pwsh Tools/AgentBridge/Verify-PiTurnBudget.ps1` in the workbench runs the offline Node guard tests and an installed-Pi compatibility smoke without sending a model task. Unity fixtures cover the runner, RPC projection, real Node child pipes, and both verified task entry points.

Environment variables `UNITY_AGENT_BRIDGE_PI_PATH` and `UNITY_AGENT_BRIDGE_PI_MODEL` remain available for machine-level launch diagnostics and take precedence at the Editor bridge boundary; they do not expand the template capability or request schema.

## Executed read-folder policy — 2026-10-10

Effective read/deny folders are now captured into the fixed guard's process policy. Readiness uses protocol 2 with the SHA-256 of that policy, plus the existing nonce and request ceiling. No prompt is sent with missing/mismatched scope readiness. Directory rules remain template-owned and are not additional editable settings or public request fields.

The registered read/grep/find/ls variants validate logical project-relative and physical resolved targets. Outside-project, parent traversal, denied folders and unsafe explicit links terminate the task with `PI_PATH_ACCESS_DENIED`. Root searches/listings show only permitted branches; recursive searches filter every descendant and exclude unsafe links. Inside-scope links are usable, with cycles bounded.

Scoped tool operations are bounded to 8192 visited entries, depth 64, 1 MiB per file, 16 MiB scanned text and 32 KiB returned data. Truncated results are marked. grep uses JavaScript line regex/literal matching; common glob patterns are supported. Discovery omits .git/node_modules and does not claim full native utility/gitignore semantics. Read accepts text and small images; larger/binary files return tool errors.

This application-level guard applies to the four mediated tools on a trusted Pi 0.85.1 installation and stable tree. It is not an OS sandbox, and does not govern Pi bootstrap/config/auth/context reads or claim protection from hostile concurrent filesystem changes. External physical UPM aliases are denied; the host can still provide bounded deterministic evidence.

Repeatable checks: `Verify-PiReadPathScope.ps1`, `Verify-PiTurnBudget.ps1` and the 15-case `Verify-VerifiedAnalysisEndToEnd.ps1`. Scope tests and installed-Pi SDK/tool checks execute without provider prompts.
