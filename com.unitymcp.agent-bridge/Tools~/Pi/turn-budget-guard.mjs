import { writeSync } from "node:fs";

export const markerPrefix = "AGB_PI_GUARD:";
export const budgetExitCode = 78;

export default function installTurnBudgetGuard(pi) {
    const nonce = process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE;
    const maxTurns = Number(process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS);
    if (!/^[a-f0-9]{32}$/.test(nonce ?? "") || !Number.isInteger(maxTurns) || maxTurns < 1 || maxTurns > 64) {
        process.exit(79);
    }

    let requests = 0;
    const signal = (type) => writeSync(2, markerPrefix + JSON.stringify({
        type, protocolVersion: 1, nonce, maxTurns, requests
    }) + "\n");

    pi.on("before_provider_request", () => {
        if (requests >= maxTurns) {
            // Pi catches ordinary handler exceptions. Even a broken stderr pipe must not let dispatch continue.
            try { signal("limit"); }
            finally { process.exit(budgetExitCode); }
        }
        requests++;
    });
    pi.on("session_start", () => signal("ready"));
    // Pi 0.85.1 compaction uses a separate summarizer outside before_provider_request.
    // Refuse compaction so that it cannot introduce an uncounted model request.
    pi.on("session_before_compact", () => ({ cancel: true }));
}
