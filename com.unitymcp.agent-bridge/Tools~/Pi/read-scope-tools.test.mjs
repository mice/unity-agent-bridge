import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { spawn, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { loadScope, scopedDefinitions, ScopeDenied, limits } from "./read-scope-tools.mjs";

const guard = fileURLToPath(new URL("./turn-budget-guard.mjs", import.meta.url));
const helper = new URL("./read-scope-tools.mjs", import.meta.url).href;
function fixture(run) {
    const container = fs.mkdtempSync(path.join(os.tmpdir(), "PiReadScope-"));
    const root = path.join(container, "Project"), outside = path.join(container, "Outside");
    for (const dir of ["Assets", "Assets/Private", "Assets/Sub", "AssetsSibling", "Library", "Temp", "Packages", "ProjectSettings", "Documentation~"])
        fs.mkdirSync(path.join(root, dir), { recursive: true });
    fs.mkdirSync(outside);
    for (const [file, text] of Object.entries({ "Assets/public.txt": "public MATCH\nline two\n", "Assets/Sub/code.cs": "MATCH source\n",
        "Assets/Private/secret.txt": "PRIVATE_SECRET MATCH", "Library/hidden.txt": "LIBRARY_SECRET MATCH", "AssetsSibling/hidden.txt": "SIBLING_SECRET MATCH" }))
        fs.writeFileSync(path.join(root, file), text);
    fs.writeFileSync(path.join(outside, "outside.txt"), "OUTSIDE_SECRET MATCH");
    const policy = JSON.stringify({ version: 1, projectRoot: root, readFolders: ["Assets", "Packages", "ProjectSettings", "Documentation~"],
        denyFolders: ["Assets/Private", "Library", "Temp"], tools: ["read", "grep", "find", "ls"] });
    const scope = loadScope(policy, root);
    const definitions = new Map(scopedDefinitions(scope, () => { throw new ScopeDenied("denied"); }).map(tool => [tool.name, tool]));
    const call = async (name, args) => definitions.get(name).execute("test", args);
    return Promise.resolve().then(() => run({ root, outside, policy, scope, call })).finally(() => {
        const resolved = path.resolve(container), tempRoot = path.resolve(os.tmpdir());
        assert.ok(resolved.startsWith(tempRoot + path.sep) && path.basename(resolved).startsWith("PiReadScope-"));
        fs.rmSync(resolved, { recursive: true, force: true });
    });
}
const text = result => result.content.filter(item => item.type === "text").map(item => item.text).join("\n");

test("all four tools operate on allowed files with stable parameter names", () => fixture(async ({ root, call }) => {
    assert.equal(text(await call("read", { path: "Assets/public.txt", offset: 2, limit: 1 })), "line two");
    assert.match(text(await call("read", { path: path.join(root, "Assets/public.txt") })), /public MATCH/);
    assert.match(text(await call("grep", { pattern: "match", ignoreCase: true, path: "Assets", glob: "*.txt" })), /public.txt:1:public MATCH/);
    assert.match(text(await call("find", { pattern: "**/*.{txt,cs}", path: "Assets" })), /code.cs/);
    assert.match(text(await call("ls", { path: "Assets" })), /public.txt/);
}));

test("explicit outside, parent, sibling-prefix and denied targets fail across tools", () => fixture(async ({ root, outside, call }) => {
    for (const target of ["../Outside/outside.txt", path.join(outside, "outside.txt"), "Assets/../Library/hidden.txt",
        "AssetsSibling/hidden.txt", "Library/hidden.txt", "Assets/Private/secret.txt", "~/.pi/auth.json", "Assets/public.txt:stream"])
        for (const name of ["read", "grep", "find", "ls"])
            await assert.rejects(call(name, { path: target, pattern: "*" }), ScopeDenied, name + ":" + target);
    if (process.platform === "win32") await assert.rejects(call("read", { path: "C:relative" }), ScopeDenied);
}));

test("root discovery exposes permitted branches only", () => fixture(async ({ call }) => {
    for (const result of [await call("ls", {}), await call("find", { pattern: "**/*" }), await call("grep", { pattern: "MATCH" })]) {
        const output = text(result);
        assert.doesNotMatch(output, /PRIVATE_SECRET|LIBRARY_SECRET|SIBLING_SECRET|OUTSIDE_SECRET|Private|Library|AssetsSibling|hidden.txt|secret.txt/);
    }
}));

test("recursive searches omit external and denied directory links", () => fixture(async ({ root, outside, call }) => {
    const type = process.platform === "win32" ? "junction" : "dir";
    fs.symlinkSync(outside, path.join(root, "Assets/ExternalLink"), type);
    fs.symlinkSync(path.join(root, "Library"), path.join(root, "Assets/DeniedLink"), type);
    for (const name of ["read", "grep", "find", "ls"]) {
        const suffix = name === "read" ? "/outside.txt" : "";
        await assert.rejects(call(name, { path: "Assets/ExternalLink" + suffix, pattern: "*" }), ScopeDenied);
        await assert.rejects(call(name, { path: "Assets/DeniedLink" + (name === "read" ? "/hidden.txt" : ""), pattern: "*" }), ScopeDenied);
    }
    for (const result of [await call("ls", { path: "Assets" }), await call("grep", { path: "Assets", pattern: "MATCH" }), await call("find", { path: "Assets", pattern: "**/*" })])
        assert.doesNotMatch(text(result), /SECRET|ExternalLink|DeniedLink|outside.txt|hidden.txt/);
}));

test("safe links and directory cycles remain bounded", () => fixture(async ({ root, call }) => {
    fs.symlinkSync(path.join(root, "Assets/Sub"), path.join(root, "Assets/SafeLink"), process.platform === "win32" ? "junction" : "dir");
    fs.symlinkSync(path.join(root, "Assets"), path.join(root, "Assets/Sub/Loop"), process.platform === "win32" ? "junction" : "dir");
    assert.match(text(await call("read", { path: "Assets/SafeLink/code.cs" })), /MATCH source/);
    assert.match(text(await call("find", { path: "Assets", pattern: "*.cs" })), /code.cs/);
}));

test("broken links are denied while missing allowed files are ordinary errors", () => fixture(async ({ root, call }) => {
    fs.symlinkSync(path.join(root, "Gone"), path.join(root, "Assets/Broken"), process.platform === "win32" ? "junction" : "dir");
    await assert.rejects(call("read", { path: "Assets/Broken/file.txt" }), ScopeDenied);
    await assert.rejects(call("read", { path: "Assets/missing.txt" }), error => error.code === "ENOENT");
}));

test("host case rules and segment boundaries are enforced", () => fixture(async ({ call }) => {
    if (process.platform === "win32") assert.match(text(await call("read", { path: "assets/PUBLIC.TXT" })), /MATCH/);
    else await assert.rejects(call("read", { path: "assets/public.txt" }), ScopeDenied);
    await assert.rejects(call("ls", { path: "AssetsSibling" }), ScopeDenied);
}));

test("output, file and discovery limits explicitly expose truncation", () => fixture(async ({ root, call }) => {
    fs.writeFileSync(path.join(root, "Assets/large.txt"), "😀".repeat(10000));
    const output = await call("read", { path: "Assets/large.txt" });
    assert.equal(output.details.truncated, true);
    assert.ok(Buffer.byteLength(text(output)) <= limits.outputBytes);
    assert.doesNotMatch(text(output), /\uFFFD/);
    const found = await call("find", { path: "Assets", pattern: "**/*", limit: 1 });
    assert.equal(found.details.truncated, true);
    fs.writeFileSync(path.join(root, "Assets/oversized.txt"), Buffer.alloc(limits.fileBytes + 1));
    await assert.rejects(call("read", { path: "Assets/oversized.txt" }), /byte limit/);
}));

test("malformed policy, cwd mismatch and unavailable tools have no fallback", () => fixture(async ({ root, policy, call }) => {
    for (const raw of ["{}", "not JSON", JSON.stringify({ ...JSON.parse(policy), readFolders: ["../Outside"] }),
        JSON.stringify({ ...JSON.parse(policy), tools: ["bash"] }), JSON.stringify({ ...JSON.parse(policy), injected: true })])
        assert.throws(() => loadScope(raw, root));
    assert.throws(() => loadScope(policy, path.dirname(root)), /cwd/);
    const scope = loadScope(JSON.stringify({ ...JSON.parse(policy), tools: ["read"] }), root);
    const find = scopedDefinitions(scope, () => { throw new ScopeDenied(); }).find(tool => tool.name === "find");
    await assert.rejects(find.execute("test", { pattern: "*" }), ScopeDenied);
}));

test("permission denial terminates before contents and survives a broken diagnostic pipe", () => fixture(async ({ root, policy }) => {
    for (const broken of [false, true]) {
        const source = `import install from ${JSON.stringify(new URL("./turn-budget-guard.mjs", import.meta.url).href)};
            import {closeSync} from 'node:fs'; const handlers=new Map(); install({on(n,h){handlers.set(n,h)},registerTool(){},setActiveTools(){}});
            handlers.get('session_start')(); if (${broken}) closeSync(2);
            try { handlers.get('tool_call')({toolName:'read',input:{path:'Library/hidden.txt'}}); } catch {}
            console.log('LEAK');`;
        const child = spawnSync(process.execPath, ["--input-type=module", "--eval", source], { cwd: root, timeout: 5000, encoding: "utf8",
            env: { ...process.env, UNITY_AGENT_BRIDGE_PI_GUARD_NONCE: "a".repeat(32), UNITY_AGENT_BRIDGE_PI_MAX_TURNS: "2", UNITY_AGENT_BRIDGE_PI_READ_POLICY: policy } });
        assert.equal(child.status, 80);
        assert.equal(child.stdout, "");
        if (!broken) assert.match(child.stderr, /"type":"path_denied"/);
    }
}));

test("installed Pi session uses scoped overrides and performs allowed reads without a model prompt", { skip: !process.env.AGB_PI_TEST_CLI }, () => fixture(async ({ root, policy }) => {
    const sdk = path.join(path.dirname(process.env.AGB_PI_TEST_CLI), "index.js");
    const source = `
        import {pathToFileURL} from 'node:url';
        const sdk = await import(pathToFileURL(${JSON.stringify(sdk)}).href);
        const root = process.cwd(), agentDir = root + '/Config';
        const settings = sdk.SettingsManager.inMemory({packages:[]});
        const loader = new sdk.DefaultResourceLoader({cwd:root,agentDir,settingsManager:settings,noExtensions:true,noSkills:true,noPromptTemplates:true,noThemes:true,noContextFiles:true,additionalExtensionPaths:[${JSON.stringify(guard)}]});
        await loader.reload();
        const {session} = await sdk.createAgentSession({cwd:root,agentDir,settingsManager:settings,resourceLoader:loader,tools:JSON.parse(process.env.UNITY_AGENT_BRIDGE_PI_READ_POLICY).tools,sessionManager:sdk.SessionManager.inMemory()});
        try {
            const tools = session.getAllTools();
            const active = session.agent.state.tools;
            const read = active.find(tool => tool.name === 'read');
            if (process.env.AGB_PI_TEST_DENIED === '1') await read.execute('scope-denied',{path:'Library/hidden.txt'});
            const output = await read.execute('scope-read',{path:'Assets/public.txt'});
            const found = await active.find(tool=>tool.name==='find').execute('scope-find',{pattern:'*.cs',path:'Assets'});
            const searched = active.find(tool=>tool.name==='grep') ? await active.find(tool=>tool.name==='grep').execute('scope-grep',{pattern:'MATCH',path:'Assets'}) : null;
            const listed = active.find(tool=>tool.name==='ls') ? await active.find(tool=>tool.name==='ls').execute('scope-ls',{path:'Assets'}) : null;
            console.log(JSON.stringify({tools:tools.filter(tool=>['read','grep','find','ls'].includes(tool.name)),active:active.map(tool=>tool.name),output,found,searched,listed,prompts:session.agent.state.messages.length}));
        } finally { session.dispose(); }
    `;
    for (const names of [["read","find"],["read","grep","find","ls"]]) {
        const child = spawnSync(process.execPath, ["--input-type=module", "--eval", source], { cwd: root, encoding: "utf8", timeout: 15000,
            env: { ...process.env, UNITY_AGENT_BRIDGE_PI_GUARD_NONCE: "a".repeat(32), UNITY_AGENT_BRIDGE_PI_MAX_TURNS: "2",
                UNITY_AGENT_BRIDGE_PI_READ_POLICY: JSON.stringify({...JSON.parse(policy),tools:names}) } });
        assert.equal(child.status, 0, child.stderr);
        const reply = JSON.parse(child.stdout.trim().split("\n").at(-1));
        assert.deepEqual(reply.active.sort(), [...names].sort());
        for (const name of names) {
            const tool = reply.tools.find(tool=>tool.name===name);
            assert.ok(tool, JSON.stringify(reply.tools));
            assert.match(tool.description,/Project-scoped/);
            assert.equal(tool.parameters.additionalProperties,false);
        }
        assert.match(text(reply.output), /public MATCH/);
        assert.match(text(reply.found), /code.cs/);
        if (reply.searched) assert.doesNotMatch(text(reply.searched), /SECRET/);
        if (reply.listed) assert.doesNotMatch(text(reply.listed), /Private/);
        assert.equal(reply.prompts,0);
        const denied = spawnSync(process.execPath, ["--input-type=module", "--eval", source], {cwd:root,encoding:"utf8",timeout:15000,
            env:{...process.env,AGB_PI_TEST_DENIED:"1",UNITY_AGENT_BRIDGE_PI_GUARD_NONCE:"a".repeat(32),UNITY_AGENT_BRIDGE_PI_MAX_TURNS:"2",UNITY_AGENT_BRIDGE_PI_READ_POLICY:JSON.stringify({...JSON.parse(policy),tools:names})}});
        assert.equal(denied.status,80,denied.stderr);
        assert.doesNotMatch(denied.stdout,/LIBRARY_SECRET|OUTSIDE_SECRET/);
        assert.match(denied.stderr,/"type":"path_denied"/);
    }
}));
