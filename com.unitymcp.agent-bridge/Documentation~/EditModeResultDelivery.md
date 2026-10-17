# EditMode result delivery

## Patch — 2026-10-17

An ordinary `unity_tests_run_edit_mode` call waits for the original command's outbox result. A persisted task state of `Completed / Succeeded` alone does not prove that transport delivery succeeded. Before this fix, an unloaded transient settings object could prevent queue completion while the run was removed; an output exception could also leave delivery marked complete prematurely.

Admission now captures the queue root independently of Unity settings lifetime. Completion persists the terminal ToolResult before transport delivery; it retires the run only after the original queue accepts that result. A failed output write remains pending. Reload recovery delivers the retained terminal outcome with its original command identity and test metrics, without executing tests again. Legacy retained task responses can also supply the terminal payload when a pending run record survives.

Missing-settings defaults are reused and protected from Unity's test-runner asset unloading. They are destroyed on assembly reload or Editor exit. Persistent settings assets are not modified.

Ordinary calls retain `success`/`failed` terminal status. MCP Tasks retain the existing `completed` lifecycle status and express assertion failure through resultKind/success/isError. Cancellation support and timeout behavior are unchanged; main-thread blocking can still delay Unity-side deadline checks.

For diagnosis, compare the original processing/outbox command, `Library/AgentBridge/agent-task-results`, retained EditMode delivery records and `Temp/AgentBridge/logs/mcp-server.log`. `AGENT_TASK_EDITMODE_DELIVERY_PENDING` means a terminal result exists but output must be retried. Do not launch duplicate tests to repair result delivery. Old rc.2 commands whose recovery records were already lost cannot be mapped to a result by guessing test names or timestamps.

Temporary validation projects belong outside imported package content, or within a Unity-ignored tilde directory. A repository `.gitignore` does not exclude assets from Unity import. Workbench and AssetToolPackage validation allocators reject unsafe ordinary package Temp paths before creating a project.
