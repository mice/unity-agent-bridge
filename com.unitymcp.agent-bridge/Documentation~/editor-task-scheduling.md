# Editor task scheduling (Unreleased implementation)

One Editor owns one exclusive execution slot. Compile, EditMode/PlayMode tests, builds, mode changes, project/scene edits and unknown code wait in admission order. Each Editor has its own queue, so work in separate projects can overlap. This implementation remains Unreleased. Current fixed-pair validation covers actual FIFO, deadlines, cancellation, manual conflicts, storage faults and test reload; candidate-specific publication checks remain separate.

`schedulerWaitingCapacity` in Agent Bridge settings defaults to 32 and accepts 1–256. It limits waiting requests, excluding the current owner. `maxConcurrent` remains an intake setting; it does not authorize overlapping asynchronous operations. Disabling the Bridge pauses new dispatch and preserves existing ownership.

The original request timeout includes queue time. A cancelled, expired or rejected waiting task never calls its driver. Native tasks keep their stable Created identity while queued; MCP uses its existing working state. Actual startup receives only the remaining original budget. Querying, reconnecting and changing settings do not renew it.

Logical completion, actual Unity completion and result delivery are independent. Timeout or unsupported cancellation cannot prove Unity stopped. The slot stays occupied until original compile/test completion evidence, positive executor completion, or verified original Editor exit. Results are immutable; delivery retry does not rerun work. The admitted output directory remains authoritative after settings changes.

Optional versioned scheduling observations expose queue position/sequence, capacity, original deadline and connection, blocking reason, active operation, actual occupancy and pending delivery. Native query/result and supported cancellation remain available while work is queued. Ordinary pending commands can be cancelled through the internal task control protocol using their original `commandId`; this is not a new public MCP tool. Started ordinary work cannot promise physical cancellation.

Domain Reload reattaches original evidence and never replays uncertain work. New Editor sessions do not run old waiting requests. Corrupt, missing committed or incompatible journals block conflicting work with diagnostics. Do not delete `Library/AgentBridge/scheduler` to unblock an unknown operation: inspect its original activity first. This journal is version 1, bounded to 16 MiB, with bounded terminal retention. Pending results and actual owners are not eligible for terminal cleanup.

Only code-registered observations/external executors bypass the slot; a `readOnly` argument cannot grant that permission. Existing executor registrations default to exclusive. Unity template runners implement `IAgentUnityExecution` and accept the original deadline; otherwise admission fails before startup. Explicit external report/Pi registrations use the concurrent path. Opaque prepared Unity executors cannot be rebuilt safely after reload: queued tasks fail under their original identity and started tasks retain occupancy until positive completion evidence.

Observed compilation, import, Test Runner activity and Play Mode transitions pause dispatch. Stable mode incompatibility fails at dispatch without switching modes. Detection cannot prevent every manual Unity operation, and synchronous user code can still block the Editor main thread. The scheduler never launches or restarts an Editor.

Fresh identity-verified busy projects may accept bounded work when their status advertises scheduling schema 1 for that same project and Editor session. Discovery still reports recovering and ready=false; this admission eligibility never claims Unity is idle. Legacy, stale, disabled, offline, unknown and mismatched catalog/session endpoints retain explicit rejection.

Test activity detection reads both legacy TestRunnerApi state and persisted API job state. Both must be readable and idle before idle-based recovery release; original RunFinished is direct completion evidence. Unknown Test Framework activity fails closed. These checks observe conflicts before dispatch and cannot preempt a manual action begun afterward.

If an issued compile request times out without an observed epoch, idle flags do not prove it stopped. Inspect original evidence; an explicitly authorized Editor exit/reopen can establish termination. No automatic restart or request replay is performed. After logical timeout removes compiler service state, actual compiler callbacks still retain the admitted queue across settings/reload.
