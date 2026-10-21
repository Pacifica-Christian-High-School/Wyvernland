import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/world.js", import.meta.url), "utf8");
const calls = [], messages = [], timers = new Map();
let timerId = 0, page = 0, host = "self", failRead = false, rejectClaim = false, retryClaim = false, rejectCommit = false;
const mesh = "a".repeat(64), status = { textContent: "" };
const window = {};
vm.runInNewContext(source, { window, document: { getElementById: () => status }, Map, Set, AbortSignal,
  setTimeout: callback => { timers.set(++timerId, callback); return timerId; }, clearTimeout: id => timers.delete(id),
  fetch: async (_, options) => {
    const body = JSON.parse(options.body); calls.push(body);
    if (body.action === "claim" && rejectClaim) { rejectClaim = false; return { ok: false, status: 400, json: async () => ({ error: "Invalid pickup" }) }; }
    if (body.action === "claim" && retryClaim) { retryClaim = false; return { ok: false, status: 409, json: async () => ({ error: "World is still synchronizing" }) }; }
    if (body.action === "read" && failRead) { failRead = false; throw new Error("Connection lost"); }
    let result = { room: { host_id: host }, accepted: true };
    if (body.action === "commit" && rejectCommit) { rejectCommit = false; result.rejected = [{ id: body.objects[0].id, reason: "v" }]; }
    if (body.action === "read") {
      page++;
      result = { ...result, objects: page === 1 ? [{ id: "b1", mesh }] : page === 2 ? [{ id: "d1", mesh }] : [], commands: [],
        more: page === 1, until: 8, after: page === 1 ? 4 : 8, cursor: page === 1 ? "b1" : "" };
    }
    if (body.action === "mesh-read") result = { parts: [{ part: 0, total: 2, data: "AAAA" }, { part: 1, total: 2, data: "BBBB" }] };
    if (body.action === "claim") result = { owned: true };
    return { ok: true, json: async () => result };
  } });
const bridge = window.wyvernlandWorld;
const settle = async () => { for (let i=0;i<25;i++) await new Promise(resolve => setImmediate(resolve)); };
async function next() { const [id, callback] = timers.entries().next().value; timers.delete(id); callback(); await settle(); }
bridge.attach({ SendMessage: (object, method, value) => { assert.equal(object, "Wyvernland Shared World"); assert.equal(method, "ReceiveWorld"); messages.push(JSON.parse(value)); } });
bridge.session({ id: "self", token: "secret" }); bridge.ready("b".repeat(64)); await settle();
assert.equal(calls.some(c => c.action === "activate"), false, "Physics waits for every initial snapshot page");
assert.equal(messages.at(-1).ready, false);
assert.equal(messages.at(-1).meshes[0].data, "AAAABBBB");
await next();
assert.equal(messages.at(-1).ready, true);
assert.equal(calls.filter(c => c.action === "mesh-read").length, 1, "Repeated meshes share cached geometry");
assert.deepEqual(calls.filter(c => c.action === "read").map(c => [c.after, c.cursor]), [[0, ""], [4, "b1"]]);

const publish = value => bridge.publish(JSON.stringify(value)), request = value => bridge.request(JSON.stringify(value));
publish({ objects: Array.from({ length: 40 }, (_, i) => ({ id: `d${i}`, mesh: i === 0 ? mesh : "" })),
  meshes: [{ hash: mesh, data: "A".repeat(5 * 65536) }], ack: Array.from({ length: 10 }, (_, i) => "command" + i) });
await next(); await next();
const commits = calls.filter(c => c.action === "commit");
assert.deepEqual(commits.map(c => c.objects.length), [32, 8]);
assert.deepEqual(commits.map(c => c.ack.length), [8, 2]);
assert.deepEqual(calls.filter(c => c.action === "mesh-write").map(c => c.parts.length), [4, 1]);
assert.ok(calls.findIndex(c => c.action === "mesh-write") < calls.findIndex(c => c.action === "commit"), "Geometry arrives before objects reference it");

const fractureStart = calls.length;
publish({ objects: [
  { id: "original-grip", deleted: true, gripReplacement: "intermediate-grip" },
  ...Array.from({ length: 32 }, (_, i) => ({ id: "unrelated-" + i })),
  { id: "intermediate-grip", deleted: true, gripSource: "original-grip", gripReplacement: "final-grip" },
  { id: "final-grip", held: true, holder: "self", gripSource: "intermediate-grip", mesh },
], meshes: [{ hash: mesh, data: "AAAA" }] });
await next();await next();await next();await next();
const fractureCommits = calls.slice(fractureStart).filter(c => c.action === "commit").flatMap(c => c.objects);
const finalIndex = fractureCommits.findIndex(s => s.id === "final-grip");
assert.equal(fractureCommits[finalIndex].gripSource, "original-grip", "Multiple fractures between polls inherit from the source that still owns the grip");
assert.ok(finalIndex < fractureCommits.findIndex(s => s.id === "intermediate-grip") && finalIndex < fractureCommits.findIndex(s => s.id === "original-grip"), "HTTP batch boundaries cannot delete a source before committing its retained fragment");
assert.equal(fractureCommits.length, 35, "Ordered transfer flushes every queued update exactly once");

request({ action: "claim", object: "d0", state: { mesh } });
request({ action: "held", object: "d0", state: { p: [1, 2, 3] } });
request({ action: "held", object: "d0", state: { p: [4, 5, 6] } });
await next(); await next();
request({ action: "release", object: "d0", state: { v: [5, 0, 0], releaseId: "a".repeat(32) } });
await next();
assert.equal(calls.find(c => c.action === "claim").state.mesh, "");
assert.deepEqual(messages.find(m => m.claims)?.claims, [{ id: "d0", owned: true }]);
assert.deepEqual(calls.filter(c => c.action === "held").map(c => c.state.p), [[4, 5, 6]], "Queued held poses coalesce to the latest pose");
assert.ok(calls.findIndex(c => c.action === "held") < calls.findIndex(c => c.action === "release"));

const throwStart = calls.length;
request({ action: "held", object: "throw", state: { p: [1, 0, 0] } });
request({ action: "release", object: "throw", state: { v: [20, 8, 0], releaseId: "b".repeat(32) } });
request({ action: "claim", object: "throw", state: {} });
request({ action: "held", object: "throw", state: { p: [9, 0, 0] } });
await next();await next();await next();
assert.deepEqual(calls.slice(throwStart).filter(c => c.object === "throw").map(c => c.action), ["release", "claim", "held"], "Unsent held poses are retired on throw, and a regrab stays after its release barrier");
assert.ok(messages.some(m => m.releases?.[0]?.releaseId === "b".repeat(32)), "The native client receives release acknowledgement with its unique throw ID");

rejectCommit = true;
publish({ objects: [{ id: "invalid-sample", v: [null, 0, 0] }, { id: "healthy-sample" }] });
await next();
assert.equal(messages.at(-1).ready, true, "A rejected object sample must not pause the game");
const afterRejected = calls.filter(c => c.action === "commit").length;
await next();
assert.equal(calls.filter(c => c.action === "commit").length, afterRejected, "Rejected samples cannot poison the retry queue");
rejectClaim = true;
request({ action: "claim", object: "declined-pickup", state: {} });
await next();
assert.equal(messages.at(-1).ready, true, "A declined interaction must not interrupt world synchronization");
assert.deepEqual(messages.find(m => m.claims?.[0]?.id === "declined-pickup").claims, [{ id: "declined-pickup", owned: false }]);

retryClaim = true;
request({ action: "claim", object: "temporary-sync-stone", state: {} });
await next();
assert.equal(messages.some(m => m.claims?.[0]?.id === "temporary-sync-stone"), false,
  "A temporary room synchronization error must not roll a held stone back to its spawn");
await next(); await next();
assert.deepEqual(messages.find(m => m.claims?.[0]?.id === "temporary-sync-stone").claims,
  [{ id: "temporary-sync-stone", owned: true }], "The held stone claim retries after synchronization recovers");

failRead = true; await next(); assert.equal(messages.at(-1).ready, false);
assert.match(status.textContent, /Reconnecting/);
host = "other"; await next(); assert.equal(messages.at(-1).ready, true); assert.equal(messages.at(-1).host, "other");
const before = calls.filter(c => c.action === "commit").length;
publish({ objects: [{ id: "stale" }] }); await next();
host = "self"; await next(); await next();
assert.equal(calls.filter(c => c.action === "commit").length, before, "Former host updates cannot be replayed after an election");
bridge.session(null); assert.equal(messages.at(-1).ready, false); assert.equal(messages.at(-1).self, "");

let finishOld;
const raceWindow = {}, raceTimers = new Map(), raceCalls = [], raceMessages = [];
vm.runInNewContext(source, { window: raceWindow, document: { getElementById: () => status }, Map, Set, AbortSignal,
  setTimeout: callback => { raceTimers.set(++timerId, callback); return timerId; }, clearTimeout: id => raceTimers.delete(id),
  fetch: async (_, options) => {
    const body = JSON.parse(options.body); raceCalls.push(body);
    if (body.action === "ready" && body.id === "old") await new Promise(resolve => finishOld = resolve);
    return { ok: true, json: async () => ({ room: { host_id: body.id }, objects: [], commands: [], more: false, after: 0, cursor: "" }) };
  } });
raceWindow.wyvernlandWorld.attach({ SendMessage: (_, __, value) => raceMessages.push(JSON.parse(value)) });
raceWindow.wyvernlandWorld.session({ id: "old", token: "old-token" }); raceWindow.wyvernlandWorld.ready("b".repeat(64));
raceWindow.wyvernlandWorld.session({ id: "new", token: "new-token" });
finishOld(); await settle();
assert.equal(raceMessages.some(m => m.error), false, "A replaced session's response cannot change the new session's readiness");
const [raceId, callback] = raceTimers.entries().next().value; raceTimers.delete(raceId); callback(); await settle();
assert.ok(raceCalls.some(c => c.action === "ready" && c.id === "new"), "The replacement session performs its own baseline handshake");
assert.equal(raceMessages.at(-1).self, "new"); assert.equal(raceMessages.at(-1).ready, true);
console.log("PASS: browser snapshot and mesh hydration, bounded commits, held pose coalescing, release ordering, reconnect, host transfer");

const flightWindow = {}, flightTimers = new Map(), flightCalls = [], flightMessages = [];
let finishHeld;
vm.runInNewContext(source, { window: flightWindow, document: { getElementById: () => status }, Map, Set, AbortSignal,
  setTimeout: callback => { flightTimers.set(++timerId, callback); return timerId; }, clearTimeout: id => flightTimers.delete(id),
  fetch: async (_, options) => {
    const body = JSON.parse(options.body);flightCalls.push(body);
    if (body.action === "held" && body.state.p[0] === 1) await new Promise(resolve => finishHeld = resolve);
    return { ok: true, json: async () => ({ room: { host_id: "self" }, owned: true, accepted: true, revision: 10, playerCount: 3,
      objects: [], commands: [], more: false, after: 10, cursor: "" }) };
  } });
const flightBridge = flightWindow.wyvernlandWorld;
flightBridge.attach({ SendMessage: (_, __, value) => flightMessages.push(JSON.parse(value)) });
flightBridge.session({ id: "self", token: "flight-token" });flightBridge.ready("b".repeat(64));await settle();
async function nextFlight() { const [id, fn] = flightTimers.entries().next().value;flightTimers.delete(id);fn();await settle(); }
flightBridge.request(JSON.stringify({ action: "held", object: "flying", state: { p: [1, 0, 0] } }));await nextFlight();
flightBridge.request(JSON.stringify({ action: "held", object: "flying", state: { p: [2, 0, 0] } }));
flightBridge.request(JSON.stringify({ action: "release", object: "flying", state: { releaseId: "c".repeat(32), v: [25, 8, 0] } }));
flightBridge.request(JSON.stringify({ action: "claim", object: "flying", state: {} }));
flightBridge.request(JSON.stringify({ action: "held", object: "flying", state: { p: [3, 0, 0] } }));
finishHeld();await settle();await nextFlight();await nextFlight();await nextFlight();
assert.deepEqual(flightCalls.filter(c => c.object === "flying").map(c => c.action), ["held", "release", "claim", "held"], "Updating a hand during HTTP flight cannot erase its release or reorder a regrab");
assert.deepEqual(flightCalls.filter(c => c.action === "held").map(c => c.state.p[0]), [1, 3]);
assert.equal(flightMessages.find(m => m.releases)?.releases[0].revision, 10);
assert.ok(flightMessages.some(m => m.playerCount === 3), "Unity receives authoritative room player count for shared test bars");
console.log("PASS: in-flight held updates, release barriers, rapid regrab order, release acknowledgements and room-count forwarding");

const roomSource = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/rooms.js", import.meta.url), "utf8");
const elements = new Map();
for (const id of ["room-status", "room-code", "room-name", "room-map", "copy-room", "player-name", "create-room", "join-room"])
  elements.set(id, { value: id === "room-map" ? "island" : id === "player-name" ? "Alice" : "", disabled: false,
    events: {}, addEventListener(event, callback) { this.events[event] = callback; } });
const location = { href: "https://game.example/?room=FRIENDS1" }, roomWindow = {}, copied = [];
let creates = 0;
vm.runInNewContext(roomSource, { window: roomWindow, URL, location,
  history: { replaceState: (_, __, url) => location.href = url.href }, document: { getElementById: id => elements.get(id) },
  navigator: { clipboard: { writeText: async text => copied.push(text) } },
  fetch: async (url, options) => { if (options?.method === "POST") creates++;
    return { ok: true, json: async () => ({ room: { id: "room-uuid", code: "FRIENDS1", name: "Friends' world", map: "island" } }) }; } });
const selected = await roomWindow.wyvernlandRooms.ensureRoom();
assert.equal(selected.id, "room-uuid"); assert.equal(creates, 0, "Play waits for the invite lookup instead of creating another room");
assert.equal(elements.get("create-room").disabled, true);
await elements.get("copy-room").events.click(); assert.match(copied[0], /room=FRIENDS1/);
console.log("PASS: invite lookup finishes before Play, room controls lock, invite links copy");
