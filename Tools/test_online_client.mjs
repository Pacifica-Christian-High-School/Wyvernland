import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/online.js", import.meta.url), "utf8");
const callbacks = [], requests = [], snapshots = [], events = {};
const elements = {
  "online-status": { textContent: "" },
  "player-name": { value: "Tester", addEventListener: () => {} },
  "unity-canvas": { dataset: {} },
};
let reply = { id: "self", token: "secret", players: [{ id: "self" }, { id: "other", x: 1 }] };
const context = {
  window: { addEventListener: (name, handler) => events[name] = handler,
    wyvernlandRooms: { current: { id: "island", code: "PUBLIC" } },
    wyvernlandWorld: { attach() {}, session() {} } },
  document: { hidden: false, getElementById: name => elements[name] },
  AbortController, console,
  setTimeout: (fn, delay) => { callbacks.push({ fn, delay }); return callbacks.length; },
  clearTimeout: () => {},
  fetch: async (_url, options) => {
    requests.push(JSON.parse(options.body));
    return { ok: true, status: 200, json: async () => reply };
  },
};
vm.runInNewContext(source, context);
const online = context.window.wyvernlandOnline;
online.attach({ SendMessage: (object, method, json) => {
  assert.equal(object, "Wyvernland Online"); assert.equal(method, "ReceivePlayers");
  snapshots.push(JSON.parse(json));
} });
const flush = async () => { for (let i = 0; i < 10; i++) await Promise.resolve(); };
const pose = { room: "test-ground", x: 1, y: 2, z: 3, paused: false };
online.publish(JSON.stringify(pose)); await flush();
assert.equal(requests[0].action, "join"); assert.equal(requests[0].name, "Tester");
assert.equal(snapshots[0].players.length, 1); assert.equal(snapshots[0].players[0].id, "other");
assert.equal(elements["unity-canvas"].dataset.remotePlayers, "1");
reply = { id: "self", players: [{ id: "self" }] };
await callbacks.find(entry => entry.delay === 250).fn(); await flush();
assert.equal(requests[1].action, "state"); assert.equal(snapshots[1].players.length, 0);
context.document.hidden = true;
await callbacks.filter(entry => entry.delay === 250).at(-1).fn(); await flush();
assert.equal(requests[2].pose.paused, true);
assert.ok(callbacks.some(entry => entry.delay === 2000));
events.pagehide(); await flush();
assert.equal(requests.at(-1).action, "leave");
online.publish(JSON.stringify({ ...pose, room: "island" })); await flush();
assert.equal(requests.at(-1).action, "join"); assert.equal(requests.at(-1).room, "island");
console.log("PASS: browser join, Unity delivery, self exclusion, departures, background heartbeat, reconnect");
