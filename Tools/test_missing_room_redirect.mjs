import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const scripts = await Promise.all(["rooms", "world", "online"].map(name =>
  readFile(new URL(`../Assets/WebGLTemplates/Wyvernland/${name}.js`, import.meta.url), "utf8")));
const flush = async () => { for (let i = 0; i < 40; i++) await Promise.resolve(); };
function fixture({ query = "?room=MISSING1", rooms = 200, players = 200, world = 200 } = {}) {
  const elements = new Map(), redirects = [], calls = [], timers = new Map();
  let timer = 0;
  const location = { href: "https://game.example/" + query, replace(url) { redirects.push(url); this.href = url; } };
  const document = { hidden: false, exitPointerLock() {}, getElementById(id) {
    if (!elements.has(id)) elements.set(id, { value: "", dataset: {}, events: {}, addEventListener(type, fn) { this.events[type] = fn; } });
    return elements.get(id);
  } };
  const window = { addEventListener() {} };
  const context = vm.createContext({ window, document, location, URL, Map, Set, AbortController, AbortSignal, console,
    history: { replaceState(_, __, url) { location.href = url.href; } },
    navigator: { clipboard: { writeText: async () => {} } },
    setTimeout(fn) { timers.set(++timer, fn); return timer; }, clearTimeout(id) { timers.delete(id); },
    fetch: async (url, options) => {
      calls.push({ url, body: options?.body && JSON.parse(options.body) });
      const status = url.startsWith("/api/rooms") ? rooms : url === "/api/players" ? players : world;
      return { status, ok: status === 200, json: async () => status !== 200 ? { error: status === 404 ? "Room not found" : "Service unavailable" } : {
        room: { id: "selected-room", code: "MISSING1", name: "World", map: "island", host_id: "self" },
        id: "self", token: "token", players: [], objects: [], commands: [], after: 0, cursor: "", more: false,
      } };
    },
  });
  for (const source of scripts) vm.runInContext(source, context);
  return { window, document, location, calls, redirects, timers };
}

for (const options of [{ rooms: 404 }, { rooms: 400, query: "?room=" }, { rooms: 400, query: "?room=bad!#game" }]) {
  const f = fixture(options);
  await assert.rejects(f.window.wyvernlandRooms.ensureRoom());
  assert.deepEqual(f.redirects, ["https://game.example/"], "Bad invites replace the URL with the main site");
  assert.equal(f.calls.length, 1, "Missing invites cannot create a replacement world");
  f.window.wyvernlandRooms.redirectToMain();
  assert.equal(f.redirects.length, 1, "Concurrent missing-room replies trigger only one redirect");
}
const valid = fixture();
await valid.window.wyvernlandRooms.ensureRoom();
assert.equal(valid.redirects.length, 0, "Valid invites stay in their room");
const offline = fixture({ rooms: 503 });
await assert.rejects(offline.window.wyvernlandRooms.ensureRoom());
assert.equal(offline.redirects.length, 0, "An outage does not mean the room is missing");

for (const endpoint of ["players", "world"])
  for (const status of [404, 503]) {
    const f = fixture({ [endpoint]: status });
    await f.window.wyvernlandRooms.ensureRoom();
    f.window.wyvernlandOnline.attach({ SendMessage() {} });
    f.window.wyvernlandOnline.publish(JSON.stringify({ room: "island", x: 0, y: 0, z: 0 }));
    await flush();
    if (endpoint === "world") { f.window.wyvernlandWorld.ready("a".repeat(64)); await flush(); }
    assert.equal(f.redirects.length, status === 404 ? 1 : 0, `${endpoint}: redirect only confirmed missing rooms`);
    if (status === 404) {
      assert.equal(f.redirects[0], "https://game.example/");
      assert.equal(f.timers.size, 0, "Redirect stops presence and world reconnect loops");
      await assert.rejects(f.window.wyvernlandRooms.ensureRoom());
    } else assert.ok(f.timers.size > 0, "Temporary failures keep retrying");
  }
console.log("PASS: missing/malformed invite redirects, valid rooms, no accidental room creation, active-room removal, retry cleanup and transient failure recovery");
