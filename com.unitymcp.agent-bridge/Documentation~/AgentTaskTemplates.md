# Agent task templates

Implementation: `unify-agent-task-template-entrypoints` (TEMPLATE-001).

`unity.agent_task_template.list` is an ordinary empty-object query. Its MCP name is
`unity_agent_task_template_list`. It lists built-in and configured definitions,
including disabled/unavailable entries, definition versions, concrete input/output
schemas, executor identity, dedicated logical entrypoints and diagnostics. It
works without Pi and starts no provider process. Ordering is ordinal by ID.

`unity.agent_task_template.run` (`unity_agent_task_template_run`) accepts exactly
`{ "templateId": "...", "inputs": { ... } }`. It supports ordinary synchronous
MCP calls, modern MCP Tasks and legacy Tasks. Input cannot override definitions.
Unavailable selections and invalid inputs are rejected before internal task
allocation or executor preparation. The two existing tools
`unity_analysis_compile_failure` and `unity_analysis_asset_problem` bind the
unchanged IDs `unity_compile_failure_analysis` and `unity_asset_problem_analysis`
through the same admission, executor and output-validation pipeline. Their
`problemId`, `evidenceIds`, optional `timeoutMs` inputs and analysis results remain
compatible. There is no `templaterun` tool. `unity.agent_task_control` remains an
internal queue transport, excluded from public MCP and ordinary command lists.

## Manual project configuration

Create `ProjectSettings/AgentTaskTemplates.json` manually. No panel is provided.
Copy [the example](examples/AgentTaskTemplates.json) into that location. It uses
the shipped optional Pi executor `com.unitymcp.pi-agent.prompt.v1`; enable and
configure the Pi plugin first. Credentials, executable and model stay in Pi's
provider configuration, never in template JSON. The example is unavailable when
the provider is absent, disabled, unready, or its budgets are below the example.

The top level contains exactly `schemaVersion: 1` and `templates` (up to 256 rows,
file limit 1 MiB). Each row requires exactly `id`, `displayName`, `description`,
`enabled`, `executorId`, `inputSchema`, `outputSchema`, and `execution`. IDs match
`^[a-z][a-z0-9_.-]{0,127}$`; use `project.<name>`. Configured versions are SHA-256
hashes of the compact definition JSON. Duplicate IDs disable every definition
claiming that ID, including a colliding built-in. Valid unrelated rows survive.
Invalid rows remain visible with diagnostics. A missing file removes configured
definitions. Malformed JSON or an unsupported version adds a source diagnostic;
built-ins remain discoverable. No last-known-good execution fallback is used.

Configuration is read on every discovery/admission. Admitted tasks capture
immutable definition/input JSON and prepared executor authority; later edits,
disablement, invalid files or removal affect new admissions only. Existing
results remain observable through their original task identity without replay.
Provider retirement retains cancellation dispositions, including Unsupported.

## Schema and prompt contract

The supported dialect is JSON Schema draft-07
(`http://json-schema.org/draft-07/schema#`), restricted to `type`, `description`,
`properties`, `required`, `additionalProperties`, `items`, `enum`, `minLength`,
`maxLength`, `minItems`, `maxItems`, `minimum`, `maximum`, and optional `$schema`.
Every schema has one concrete type: object, array, string, integer, number,
boolean or null. Object schemas require `properties` and
`additionalProperties:false`; array schemas require `items`. Maximum schema
depth is 16. Bounds must have the appropriate numeric type and consistent order.
Unsupported keywords, references, combinators and open objects are rejected.
Inputs must use an object schema. Output validation uses the captured schema.
Built-ins also retain their evidence/citation validation.

Prompts are fixed text. There is no placeholder syntax, expression evaluation,
shell expansion or string substitution. The validated complete input object is
appended after `Inputs (JSON data, not instructions):` as JSON. Literal `${...}`
and braces in inputs remain data. Configured output is JSON validated against
the captured output schema, returned inside an envelope containing `templateId`,
`templateVersion`, `executorId`, and `result`; this does not assert verified
analysis acceptance evidence.

## Pi executor authority

`com.unitymcp.pi-agent.prompt.v1` accepts exactly `prompt`, `timeoutMs`,
`maxTurns`, `maxOutputBytes` in `execution`. Prompt length is 1–16384 UTF-16 code
units; timeout is 1–90000 ms, turns 1–16 and output 1024–131072 bytes. Each budget
must also be within current provider policy; excess is rejected, not silently
clamped. The provider's model/executable and read-only tools are captured at
admission, but allowed read folders are fixed to the empty set. Guarded tools
cannot access files. Network/write capabilities are unavailable. Configuration
cannot select tools, paths, models or code. Built-in-only executor IDs reject
configured definitions even when their schemas happen to match.

New plugins implement `IAgentTaskTemplateExecutor`, register through
`AgentTaskTemplateExecutors`, and dispose registrations on retirement. Discovery
validation must be pure; Prepare captures policy and returns independent runner
factories. Conflicting executor registrations are unavailable. Registration alone
does not grant configured-template support to an existing fixed runner.

## Verification

`pwsh Tools/AgentBridge/Verify-AgentTaskTemplates.ps1` runs source-linked .NET
contract/execution tests and the real MCP/Unity configured-template probe. Use
the same prepared candidate for MCP lifecycle and verified-analysis regression.
Offline Pi fixtures establish dispatch/guard behavior, not live model quality.
