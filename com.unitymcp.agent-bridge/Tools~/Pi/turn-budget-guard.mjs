import { writeSync } from "node:fs";
import { loadScope, scopedDefinitions, ScopeDenied, permissionExitCode } from "./read-scope-tools.mjs";

export const markerPrefix = "AGB_PI_GUARD:";
export const budgetExitCode = 78;

export default function installTurnBudgetGuard(pi) {
    const nonce = process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE;
    const maxTurns = Number(process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS);
    if (!/^[a-f0-9]{32}$/.test(nonce ?? "") || !Number.isInteger(maxTurns) || maxTurns < 1 || maxTurns > 64) {
        process.exit(79);
    }

    let scope;
    try {
        scope = loadScope(process.env.UNITY_AGENT_BRIDGE_PI_READ_POLICY);
        if (typeof pi.registerTool !== "function" || typeof pi.setActiveTools !== "function") throw new Error("Scope tool API missing");
    } catch { process.exit(79); }
    let requests = 0;
    const signal = (type) => writeSync(2, markerPrefix + JSON.stringify({
        type, protocolVersion: 2, nonce, maxTurns, requests, policyHash: scope.policyHash
    }) + "\n");
    const denied = () => {
        try { signal("path_denied"); }
        finally { process.exit(permissionExitCode); }
    };
    for (const tool of scopedDefinitions(scope, denied)) pi.registerTool(tool);
    pi.on("tool_call", event => {
        try {
            if (!scope.tools.includes(event.toolName)) throw new ScopeDenied("Unlisted tool");
            scope.inspect(event.input?.path ?? ".", event.toolName !== "read");
        } catch (error) { if (error instanceof ScopeDenied) denied(); /* Missing/inaccessible allowed files become ordinary scoped-tool errors. */ }
    });

    pi.on("before_provider_request", () => {
        if (requests >= maxTurns) {
            // Pi catches ordinary handler exceptions. Even a broken stderr pipe must not let dispatch continue.
            try { signal("limit"); }
            finally { process.exit(budgetExitCode); }
        }
        requests++;
    });
    pi.on("session_start", () => { pi.setActiveTools(scope.tools); signal("ready"); });
    // Pi 0.85.1 compaction uses a separate summarizer outside before_provider_request.
    // Refuse compaction so that it cannot introduce an uncounted model request.
    pi.on("session_before_compact", () => ({ cancel: true }));
}
