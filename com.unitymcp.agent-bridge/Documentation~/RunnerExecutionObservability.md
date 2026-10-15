# Runner execution observations

Introduced by `fix-analysis-evidence-and-runner-observability`, candidate `1.4.3-rc.1`.

Provider readiness is an installation/configuration check. It is not evidence that Pi has started an analysis or received a prompt. Use the same task's execution metadata to verify actual work.

## MCP access

- `unity_agent_task_template_run` and both dedicated analysis bindings retain their existing inputs and business outputs.
- The compatibility `tasks/result` response exposes an `execution` object for an active or terminal task. Modern SDK `tasks/get` carries the active observation JSON in `statusMessage` prefixed with `Execution: `; its terminal structured result also contains the observation. Synchronous analysis responses retain it alongside lifecycle metadata, outside validated analysis output.
- Rejected evidence returns `creationRejected`, no AgentTask ID and `execution.stage=not_started`, `processStarted=false`.

```json
{
  "execution": {
    "schemaVersion": 1,
    "providerId": "com.unitymcp.pi-agent",
    "stage": "terminal",
    "processStarted": true,
    "processStartedAt": "2026-10-16T00:40:11.4895989+08:00",
    "processId": 9308,
    "guardReadyAt": "2026-10-16T00:40:12.728551+08:00",
    "promptSubmittedAt": "2026-10-16T00:40:12.728551+08:00",
    "terminalAt": "2026-10-16T00:40:25.7269015+08:00",
    "exitCode": 0
  }
}
```

This example is retained evidence of one real Pi run, not a current process listing. Timestamps are ISO 8601 instants with offsets. Nullable identity/exit fields mean the adapter has no confirmed value. A PID is historical correlation data, not authority to terminate a potentially reused process ID.

## Interpretation

| Stage | Meaning |
| --- | --- |
| `not_started` | No task-owned analysis child started, including admission rejection. |
| `starting` | Runner startup is being attempted; child creation is not confirmed. |
| `process_started` | Adapter confirmed the task-owned analysis child, independently of any version probe. |
| `guard_ready` | Fixed guard acknowledged captured scope/budget policy. |
| `prompt_submitted` | Prompt write and flush succeeded after the guard handshake. |
| `terminal` | Runner finished; retained milestones show how far it got. Read task state/error for success, failure, cancellation or timeout. |
| `unknown` | Legacy provider/record or unavailable observation; neither startup nor absence is established. |

Prompt submission does not prove that an external model request succeeded. Successful task completion and validated cited output provide separate execution evidence. Observations contain no prompt, raw arguments, environment, credential or configuration payload. Queries do not retry tasks.

## Compile evidence references

Pass the real compile tool's project-relative `reportPath` directly as an element of `inputs.evidenceIds`. Approved report roots are `Library/AgentBridge/compile-reports`, retained results in `Library/AgentBridge/mcp-task-reports`, and legacy `Temp/AgentBridge/reports`. Legacy basename/command-ID references work only when unambiguous. Absolute/traversal/link escapes, invalid contracts/diagnostics and oversized reports are rejected before runner startup.

An empty diagnostics array is complete evidence; it does not establish a compile failure. No manual registration, report copying or invented evidence IDs are required. Missing provider remains an explicit readiness failure.

Build matching RC host/runtime and Pi plugin source; Pi plugin `0.1.1-rc.1` requires core `1.4.3-rc.1`. Generated packages remain local. Existing 1.4.2 tags are unchanged.
