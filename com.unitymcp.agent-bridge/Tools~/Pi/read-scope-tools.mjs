import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";

export const permissionExitCode = 80;
export const limits = Object.freeze({ entries: 8192, depth: 64, fileBytes: 1048576, scanBytes: 16777216, outputBytes: 32768 });
const windows = process.platform === "win32";
const key = value => windows ? value.toLowerCase() : value;
const beneath = (value, root) => key(value) === key(root) || key(value).startsWith(key(root) + path.sep);
const forbiddenSyntax = value => typeof value !== "string" || !value.trim() || /[\x00-\x1f]/.test(value) ||
    value.startsWith("~") || /^[\\/]{2}[?.]/.test(value) || /^[a-zA-Z]:[^\\/]/.test(value) ||
    value.replaceAll("\\", "/").split("/").includes("..");

export class ScopeDenied extends Error {}

function folders(values) {
    if (!Array.isArray(values) || values.length > 64) throw new Error("Invalid scope folders");
    return values.map(value => {
        if (forbiddenSyntax(value) || path.isAbsolute(value) || value.includes(":") ||
            value.replaceAll("\\", "/").split("/").some(part => !part || part === ".")) throw new Error("Invalid scope folder");
        return value.replaceAll("\\", "/");
    });
}

export function loadScope(raw, cwd = process.cwd()) {
    if (typeof raw !== "string" || Buffer.byteLength(raw) > 16384) throw new Error("Missing/oversized scope");
    const policy = JSON.parse(raw);
    if (policy.version !== 1 || Object.keys(policy).sort().join(",") !== "denyFolders,projectRoot,readFolders,tools,version" ||
        typeof policy.projectRoot !== "string" || !path.isAbsolute(policy.projectRoot) ||
        !Array.isArray(policy.tools) || policy.tools.length === 0 ||
        policy.tools.some(name => !["read", "grep", "find", "ls"].includes(name))) throw new Error("Invalid scope policy");
    const root = path.resolve(policy.projectRoot);
    const physicalRoot = fs.realpathSync.native(root);
    if (!fs.statSync(physicalRoot).isDirectory() || key(fs.realpathSync.native(cwd)) !== key(physicalRoot)) throw new Error("Scope root is not bound cwd");
    const read = folders(policy.readFolders), deny = folders(policy.denyFolders);
    const reads = read.map(folder => path.resolve(root, folder));
    const denies = deny.map(folder => path.resolve(root, folder));
    const physicalReads = read.map(folder => path.resolve(physicalRoot, folder));
    const physicalDenies = deny.map(folder => path.resolve(physicalRoot, folder));
    const tools = Object.freeze([...new Set(policy.tools)]);
    const permitted = (target, allowed, denied, container) => !denied.some(folder => beneath(target, folder)) &&
        (allowed.some(folder => beneath(target, folder)) || (container && allowed.some(folder => beneath(folder, target))));
    function inspect(input, container = false) {
        if (forbiddenSyntax(input)) throw new ScopeDenied("Invalid or traversing tool path");
        const logical = path.resolve(root, input);
        const relative = path.relative(root, logical);
        if (!beneath(logical, root) || relative.includes(":") || !permitted(logical, reads, denies, container))
            throw new ScopeDenied("Tool path outside declared read scope");
        let real;
        try { real = fs.realpathSync.native(logical); }
        catch (error) {
            // Distinguish a missing allowed file from an unresolved link anywhere in its path.
            const parts = path.relative(root, logical).split(path.sep).filter(Boolean);
            let current = root;
            for (const part of parts) {
                current = path.join(current, part);
                try { if (fs.lstatSync(current).isSymbolicLink()) throw new ScopeDenied("Unresolved symbolic link"); }
                catch (probe) { if (probe instanceof ScopeDenied) throw probe; break; }
            }
            throw error;
        }
        if (!beneath(real, physicalRoot) || !permitted(real, physicalReads, physicalDenies, container))
            throw new ScopeDenied("Physical target outside declared read scope");
        return { logical, real, stat: fs.statSync(real) };
    }
    function accessible(input, container = false) {
        try { return inspect(input, container); }
        catch (error) {
            if (error instanceof ScopeDenied || ["ENOENT", "ELOOP", "ENOTDIR", "EACCES", "EPERM"].includes(error.code)) return null;
            throw error;
        }
    }
    return Object.freeze({ root, physicalRoot, tools, inspect, accessible,
        policyHash: createHash("sha256").update(raw, "utf8").digest("hex") });
}

function checkAbort(signal) { if (signal?.aborted) throw new Error("Operation cancelled"); }
function boundedText(text) {
    const bytes = Buffer.from(text);
    if (bytes.length <= limits.outputBytes) return { text, truncated: false };
    let end = limits.outputBytes;
    while (end > 0 && (bytes[end] & 0xc0) === 0x80) end--;
    return { text: bytes.subarray(0, end).toString("utf8"), truncated: true };
}
function result(text, truncated = false) {
    const output = boundedText(text);
    return { content: [{ type: "text", text: output.text }], details: { scopeEnforced: true, truncated: truncated || output.truncated } };
}
function readBytes(scope, input, signal) {
    checkAbort(signal);
    const ref = scope.inspect(input);
    if (!ref.stat.isFile()) throw new Error("Target is not a regular file");
    const descriptor = fs.openSync(ref.real, fs.constants.O_RDONLY | (fs.constants.O_NOFOLLOW ?? 0));
    try {
        if (fs.fstatSync(descriptor).size > limits.fileBytes) throw new Error("Scoped read exceeds file byte limit");
        const buffer = Buffer.alloc(limits.fileBytes + 1);
        let length = 0, count;
        do { count = fs.readSync(descriptor, buffer, length, buffer.length - length, null); length += count; } while (count > 0 && length < buffer.length);
        if (length > limits.fileBytes) throw new Error("Scoped read exceeds file byte limit");
        checkAbort(signal);
        return buffer.subarray(0, length);
    } finally { fs.closeSync(descriptor); }
}

function number(value, fallback, maximum) {
    if (value === undefined) return fallback;
    if (!Number.isInteger(value) || value < 1 || value > maximum) throw new Error("Invalid scoped tool limit/offset");
    return value;
}

// Patterns operate on already-scoped relative names, never on filesystem roots.
function globExpression(pattern) {
    if (typeof pattern !== "string" || pattern.length > 256) throw new Error("Invalid glob pattern");
    let cursor = 0;
    function compile(stop = false) {
        let value = "";
        while (cursor < pattern.length) {
            const char = pattern[cursor++];
            if (stop && (char === "," || char === "}")) { cursor--; break; }
            if (char === "*") {
                if (pattern[cursor] === "*") {
                    cursor++;
                    if (pattern[cursor] === "/") { cursor++; value += "(?:.*/)?"; } else value += ".*";
                } else value += "[^/]*";
            } else if (char === "?") value += "[^/]";
            else if (char === "{") {
                const alternatives = [];
                do { alternatives.push(compile(true)); } while (pattern[cursor++] === ",");
                if (pattern[cursor - 1] !== "}" || alternatives.length > 16) throw new Error("Invalid glob alternatives");
                value += "(?:" + alternatives.join("|") + ")";
            } else if (char === "[") {
                const end = pattern.indexOf("]", cursor);
                if (end < cursor || end - cursor > 64) throw new Error("Invalid glob class");
                let group = pattern.slice(cursor, end);
                if (group.includes("\\") || group.includes("/")) throw new Error("Invalid glob class");
                if (group.startsWith("!")) group = "^" + group.slice(1);
                value += "[" + group + "]"; cursor = end + 1;
            } else value += char.replace(/[\\^$+.()|{}\[\]]/g, "\\$&");
        }
        return value;
    }
    return new RegExp("^" + compile() + "$", windows ? "i" : "");
}

function* files(scope, input, state, signal) {
    const start = scope.inspect(input, true);
    const visited = new Set();
    function* walk(ref, depth) {
        checkAbort(signal);
        if (++state.entries > limits.entries || depth > limits.depth) { state.truncated = true; return; }
        if (ref.stat.isFile()) {
            if (scope.accessible(ref.logical)) yield ref.logical;
            return;
        }
        if (!ref.stat.isDirectory() || visited.has(key(ref.real))) return;
        visited.add(key(ref.real));
        const directory = fs.opendirSync(ref.real);
        try {
            let entry;
            while ((entry = directory.readSync()) !== null) {
                if (state.entries >= limits.entries) { state.truncated = true; return; }
                if ([".git", "node_modules"].includes(entry.name)) continue;
                const child = scope.accessible(path.join(ref.logical, entry.name), true);
                if (child) yield* walk(child, depth + 1);
                else if (++state.entries > limits.entries) { state.truncated = true; return; }
            }
        } finally { directory.closeSync(); }
    }
    yield* walk(start, 0);
}

const string = { type: "string" }, integer = { type: "integer", minimum: 1 }, boolean = { type: "boolean" };
const properties = {
    read: { path: string, offset: integer, limit: integer },
    grep: { pattern: string, path: string, glob: string, ignoreCase: boolean, literal: boolean, context: { type: "integer", minimum: 0, maximum: 20 }, limit: integer },
    find: { pattern: string, path: string, limit: integer },
    ls: { path: string, limit: integer }
};

export function scopedDefinitions(scope, denied) {
    const execute = {
        read(args, signal) {
            const buffer = readBytes(scope, args.path, signal);
            const types = [["image/png", "89504e470d0a1a0a"], ["image/jpeg", "ffd8ff"], ["image/gif", "47494638"], ["image/webp", "52494646"]];
            const image = types.find(([mime, prefix]) => buffer.subarray(0, 8).toString("hex").startsWith(prefix) &&
                (mime !== "image/webp" || buffer.subarray(8, 12).toString("ascii") === "WEBP"));
            if (image) {
                if (buffer.length * 4 / 3 > limits.outputBytes) throw new Error("Scoped image exceeds output byte limit");
                return { content: [{ type: "image", data: buffer.toString("base64"), mimeType: image[0] }], details: { scopeEnforced: true, truncated: false } };
            }
            if (buffer.includes(0)) throw new Error("Scoped read supports text or bounded images");
            const lines = buffer.toString("utf8").split(/\r?\n/);
            const offset = number(args.offset, 1, 1000000) - 1, limit = number(args.limit, 2000, 10000);
            if (offset >= lines.length) throw new Error("Read offset exceeds file line count");
            return result(lines.slice(offset, offset + limit).join("\n"), offset + limit < lines.length);
        },
        ls(args, signal) {
            checkAbort(signal);
            const ref = scope.inspect(args.path ?? ".", true);
            if (!ref.stat.isDirectory()) throw new Error("ls target is not a directory");
            const maximum = number(args.limit, 500, 5000), names = [];
            let entries = 0, truncated = false;
            const directory = fs.opendirSync(ref.real);
            try {
                let entry;
                while ((entry = directory.readSync()) !== null) {
                    checkAbort(signal);
                    if (++entries > limits.entries || names.length >= maximum) { truncated = true; break; }
                    const child = scope.accessible(path.join(ref.logical, entry.name), true);
                    if (child && (child.stat.isDirectory() || scope.accessible(child.logical))) names.push(entry.name + (child.stat.isDirectory() ? "/" : ""));
                }
            } finally { directory.closeSync(); }
            return result(names.sort().join("\n") || "No accessible entries", truncated);
        },
        find(args, signal) {
            const input = args.path ?? ".", base = scope.inspect(input, true);
            const match = globExpression(args.pattern), maximum = number(args.limit, 1000, 5000), hits = [];
            const state = { entries: 0, truncated: false };
            for (const file of files(scope, input, state, signal)) {
                const relative = path.relative(base.logical, file).replaceAll("\\", "/") || path.basename(file);
                if (match.test(relative) || (!args.pattern.includes("/") && match.test(path.basename(file)))) hits.push(relative);
                if (hits.length >= maximum) { state.truncated = true; break; }
            }
            return result(hits.join("\n") || "No files found", state.truncated);
        },
        grep(args, signal) {
            if (typeof args.pattern !== "string" || args.pattern.length > 1024) throw new Error("Invalid grep pattern");
            const input = args.path ?? ".", base = scope.inspect(input, true), maximum = number(args.limit, 100, 1000);
            const context = args.context ?? 0;
            if (!Number.isInteger(context) || context < 0 || context > 20) throw new Error("Invalid grep context");
            const pattern = args.literal ? args.pattern.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") : args.pattern;
            const match = new RegExp(pattern, args.ignoreCase ? "i" : "");
            const glob = args.glob ? globExpression(args.glob) : null;
            const hits = [], state = { entries: 0, truncated: false };
            let bytes = 0;
            for (const file of files(scope, input, state, signal)) {
                const relative = path.relative(base.logical, file).replaceAll("\\", "/") || path.basename(file);
                if (glob && !glob.test(relative) && !glob.test(path.basename(file))) continue;
                const info = scope.inspect(file);
                if (info.stat.size > limits.fileBytes) { state.truncated = true; continue; }
                if (bytes + info.stat.size > limits.scanBytes) { state.truncated = true; break; }
                const buffer = readBytes(scope, file, signal); bytes += buffer.length;
                if (buffer.includes(0)) continue;
                const lines = buffer.toString("utf8").split(/\r?\n/);
                for (let line = 0; line < lines.length; line++) {
                    checkAbort(signal);
                    if (!match.test(lines[line])) continue;
                    hits.push(`${relative}:${line + 1}:` + lines[line].slice(0, 500));
                    for (let adjacent = Math.max(0, line - context); adjacent <= Math.min(lines.length - 1, line + context); adjacent++)
                        if (adjacent !== line) hits.push(`${relative}-${adjacent + 1}-` + lines[adjacent].slice(0, 500));
                    if (hits.length >= maximum || Buffer.byteLength(hits.join("\n")) >= limits.outputBytes) { state.truncated = true; break; }
                }
                if (hits.length >= maximum || Buffer.byteLength(hits.join("\n")) >= limits.outputBytes) break;
            }
            return result(hits.join("\n") || "No matches found", state.truncated);
        }
    };
    return Object.entries(execute).map(([name, run]) => ({
        name, label: name, description: `Project-scoped ${name}. Only declared template folders; denied/link targets filtered. Bounded discovery/output; grep uses JavaScript line regex, glob supports common wildcards.`,
        promptSnippet: `Scoped ${name} within template folders`,
        parameters: { type: "object", properties: properties[name], required: name === "read" ? ["path"] : ["grep", "find"].includes(name) ? ["pattern"] : [], additionalProperties: false },
        async execute(_id, args, signal) {
            try {
                if (!scope.tools.includes(name)) throw new ScopeDenied("Unlisted tool");
                return run(args, signal);
            } catch (error) {
                if (error instanceof ScopeDenied) denied();
                throw error;
            }
        }
    }));
}
