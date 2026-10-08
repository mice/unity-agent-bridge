import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const guard = new URL("./turn-budget-guard.mjs", import.meta.url);
const nonce = "a".repeat(32);
function simulate(maxTurns, attempts, reset = false, extraEnv = {}, closeStderr = false) {
    const source = `
        import install from ${JSON.stringify(guard.href)};
        import { closeSync } from "node:fs";
        const handlers = new Map();
        install({on(name, handler) { handlers.set(name, handler); }});
        handlers.get("session_start")();
        for (let i = 0; i < ${attempts}; i++) {
            if (${reset}) handlers.get("session_start")();
            if (${closeStderr} && i === ${maxTurns}) closeSync(2);
            try { await handlers.get("before_provider_request")({}); }
            catch { /* Simulate Pi's swallowing of ordinary extension exceptions. */ }
            console.log("DISPATCH:" + (i + 1));
        }
    `;
    return spawnSync(process.execPath, ["--input-type=module", "--eval", source], {
        encoding: "utf8", timeout: 5000,
        env: { ...process.env, UNITY_AGENT_BRIDGE_PI_GUARD_NONCE: nonce,
            UNITY_AGENT_BRIDGE_PI_MAX_TURNS: String(maxTurns), ...extraEnv }
    });
}

test("exactly N requests can complete", () => {
    const result = simulate(3, 3);
    assert.equal(result.status, 0);
    assert.equal(result.stdout.trim().split("\n").length, 3);
    assert.match(result.stderr, /"type":"ready"/);
});

test("N+1 is never dispatched even when hook exceptions are swallowed", () => {
    const result = simulate(2, 3);
    assert.equal(result.status, 78);
    assert.equal(result.stdout, "DISPATCH:1\nDISPATCH:2\n");
    const marker = JSON.parse(result.stderr.trim().split("\n").at(-1).slice("AGB_PI_GUARD:".length));
    assert.deepEqual(marker, { type: "limit", protocolVersion: 1, nonce, maxTurns: 2, requests: 2 });
});

test("session events cannot reset retry accounting", () => {
    const result = simulate(1, 2, true);
    assert.equal(result.status, 78);
    assert.equal(result.stdout, "DISPATCH:1\n");
});

test("broken diagnostic pipe cannot bypass the hard ceiling", () => {
    const result = simulate(2, 3, false, {}, true);
    assert.equal(result.status, 78);
    assert.equal(result.stdout, "DISPATCH:1\nDISPATCH:2\n");
});

test("compaction is cancelled before an uncounted summarizer request", async () => {
    const { default: install } = await import(guard.href);
    const originalNonce = process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE;
    const originalBudget = process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS;
    try {
        process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE = nonce;
        process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS = "2";
        const handlers = new Map();
        install({ on(name, handler) { handlers.set(name, handler); } });
        assert.deepEqual(await handlers.get("session_before_compact")(), { cancel: true });
    } finally {
        if (originalNonce === undefined) delete process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE;
        else process.env.UNITY_AGENT_BRIDGE_PI_GUARD_NONCE = originalNonce;
        if (originalBudget === undefined) delete process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS;
        else process.env.UNITY_AGENT_BRIDGE_PI_MAX_TURNS = originalBudget;
    }
});

test("invalid budgets and readiness nonce fail before dispatch", () => {
    for (const budget of [0, 65, 1.5, "bad"]) {
        const result = simulate(budget, 1);
        assert.equal(result.status, 79);
        assert.equal(result.stdout, "");
    }
    assert.equal(simulate(2, 1, false, { UNITY_AGENT_BRIDGE_PI_GUARD_NONCE: "bad" }).status, 79);
});

test("installed Pi loads the fixed guard in closed RPC mode without submitting a task", { skip: !process.env.AGB_PI_TEST_CLI }, async () => {
    const { spawn } = await import("node:child_process");
    const child = spawn(process.execPath, [process.env.AGB_PI_TEST_CLI, "--no-extensions", "--no-skills",
        "--no-prompt-templates", "--no-session", "--tools", "read,grep,find,ls", "--mode", "rpc",
        "--extension", fileURLToPath(guard)], {
        stdio: ["pipe", "pipe", "pipe"],
        env: { ...process.env, UNITY_AGENT_BRIDGE_PI_GUARD_NONCE: nonce, UNITY_AGENT_BRIDGE_PI_MAX_TURNS: "2" }
    });
    let stdout = "", stderr = "";
    let ready = false;
    const timer = setTimeout(() => child.kill(), 15000);
    child.stdout.on("data", data => { stdout += data; });
    child.stderr.on("data", data => {
        stderr += data;
        if (!ready && stderr.includes('"type":"ready"')) { ready = true; child.stdin.end(); }
    });
    const code = await new Promise((resolve, reject) => {
        child.once("error", reject);
        child.once("exit", resolve);
    }).finally(() => clearTimeout(timer));
    assert.equal(code, 0, stderr);
    assert.equal(ready, true, stderr);
    assert.equal(stdout.includes('"type":"turn_start"'), false);
});
