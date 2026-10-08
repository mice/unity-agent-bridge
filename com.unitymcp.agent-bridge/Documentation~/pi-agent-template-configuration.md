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

Only these read-only tools can be selected. Shell, PowerShell, edit, write, network access, extensions, skills, sessions, and raw Pi arguments are not configurable through this surface.

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

The turn budget is validated and displayed as policy metadata; the current Pi CLI adapter does not count or terminate model turns. Timeout and returned-output limits are enforced. Use those enforced limits to bound elapsed time and the returned result size.

Environment variables `UNITY_AGENT_BRIDGE_PI_PATH` and `UNITY_AGENT_BRIDGE_PI_MODEL` remain available for machine-level launch diagnostics and take precedence at the Editor bridge boundary; they do not expand the template capability or request schema.
