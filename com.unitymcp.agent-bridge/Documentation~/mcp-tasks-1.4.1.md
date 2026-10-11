# MCP Tasks and compiler diagnostics in 1.4.1

The public compile tool remains `unity_project_compile` (bridge operation `unity.compile`) with an empty input object. Read `structuredContent.errors` directly: each compiler entry contains its code, message and available file, line and column. `metrics` contains error/warning counts, target/completed compile epochs, diagnostic completeness and truncation. Errors and warnings are separate; historical Console messages do not affect this operation's result.

MCP `tools/list` publishes its `outputSchema`, identical to [unity.compile.result.schema.json](schemas/unity.compile.result.schema.json). It validates the ordinary result's `structuredContent` and the same payload inside a completed task result. The initial task handle and JSON-RPC errors are separate envelopes. Failures before compiler diagnostics are available use an explicit unsuccessful branch, so clients must not infer a clean compile from missing counters.

Inline errors and warnings are bounded at 20 entries each; combined diagnostic samples are also bounded at 20. When truncated, call `unity_report_read` with the returned `reportPath` to inspect the full compiler report. Console is optional history inspection, not a prerequisite for locating compiler errors. A timeout/interruption preserves collected diagnostics and sets `diagnosticsComplete=false`.

## Calling and observing tasks

SDK 2.2.0 remains pinned. Modern Tasks use protocol 2026-07-28 with the per-request `io.modelcontextprotocol/tasks` extension. Compile, EditMode/PlayMode tests and enabled fixed analysis tools can return a durable task handle. Quick queries, bridge submit/wait/control and unspecified plugin tools return ordinary results. Down-level clients keep ordinary calls. Legacy `tasks/create` and `tasks/result` are compatibility endpoints, not the modern result contract.

For modern tasks, poll `tasks/get` until terminal. A successful **or unsuccessful business operation** is `completed`; its `result` is a standard CallToolResult and `isError` distinguishes success from compiler/test/analysis failure. `failed` carries a JSON-RPC execution error. Public invalid arguments are rejected before allocation or Unity dispatch. Interactive task input is unsupported.

Compile and Unity Test Runner cancellation is unsupported: the server returns `AGENT_TASK_CANCEL_UNSUPPORTED` and the operation remains queryable. Cancellable providers can accept cancellation; acceptance does not prove the underlying operation has already stopped. Caller wait cancellation or timeout does not cancel Unity execution. Repeated terminal reads return the committed result without contacting Unity.

## Recovery and retention

Task records, mappings, dispatch journals and owned report copies live under project-local `Library/AgentBridge`. Valid legacy Temp records are imported once. Deleting Library deletes recovery evidence; do not treat Library deletion as a supported recovery operation.

Dispatch is correlated before enqueue. After interruption the host observes the original dispatch or reports an unknown outcome; it does not replay compilation/tests. Compile reload recovery keeps its AgentTask, command and epoch. A restarted Editor process reports an interrupted compile with incomplete diagnostics. EditMode reattaches only the original running job GUID through the pinned Unity Test Framework 1.1.33 job registry; if that internal registry is unavailable, recovery fails closed. A missing runner or an unavailable PlayMode handle returns `AGENT_TASK_RECOVERY_STALE` rather than starting another run.

Maintenance runs at host startup and every 30 seconds. MCP terminal retention is seven days, with at most 1,000 terminal records and a 256 MiB owned-storage budget. Active tracking is preserved, with 128 active admissions, a 4 MiB record limit and 16 MiB report-copy limit. The default host execution window is 15 minutes; explicit native timeouts receive 30 seconds of observation grace (24-hour cap). Continuous observation failure has a five-minute recovery window; each query is bounded to five seconds. Storage pressure can evict older terminal results before seven days.

MCP cleanup deletes only task-owned retained copies. It does not delete original/shared reports. Native compile caches have separate seven-day/count/size maintenance and retain reports referenced by surviving native results. `reportRetention.available=false` explicitly identifies a report that could not be retained.

Optional Pi remains a separately installed plugin with its existing version and input schema. Rebuild the local runtime from the matching core source when upgrading. Source tags/archives remain the distribution boundary; generated runtime executables are local build outputs and are not hosted release assets.
