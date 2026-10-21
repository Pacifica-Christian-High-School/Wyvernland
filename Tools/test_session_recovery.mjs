import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";

const timers = new Map(), calls = [], messages = [], elements = {
  "online-status": { textContent: "" }, "player-name": { value: "Tester", addEventListener() {} },
  "unity-canvas": { dataset: {} },
};
let timerId = 0, joins = 0, credentials, expireWorld = false, expirePresence = false, rejectHost = false;
let holdPresence = false, finishPresence, foreignHost = false;
const window = { addEventListener() {}, wyvernlandRooms: { current: { id: "room", code: "TEST" }, setWorldReady() {} } };
const context = vm.createContext({ window, document: { hidden: false, getElementById: id => elements[id] },
  AbortController, AbortSignal, console,
  setTimeout(fn, delay) { timers.set(++timerId, { fn, delay }); return timerId; }, clearTimeout(id) { timers.delete(id); },
  fetch: async (url, options) => {
    const body = JSON.parse(options.body); calls.push({ url, ...body });
    const reply = (value, status = 200) => ({ ok: status === 200, status, json: async () => value });
    if (url === "/api/players") {
      if (body.action === "leave") return reply({ left: true });
      if (body.action === "join") {
        assert.deepEqual(body.pose.damage.impacts, [], "Joining never replays combat reports from the previous session");
        assert.equal(body.pose.damage.injury, 1, "Rejoining retains the player's injury state");
        credentials = { id: "player-" + ++joins, token: "token-" + joins };
        return reply({ ...credentials, players: [{ id: credentials.id }] });
      }
      if (holdPresence) { holdPresence = false; await new Promise(resolve => finishPresence = resolve); }
      if (expirePresence) { expirePresence = false; return reply({ error: "Session expired" }, 401); }
      if (rejectHost && body.pose.damage.impacts.length) {
        rejectHost = false; foreignHost = true;
        return reply({ error: "Only the physics host can report impacts", code: "host_changed" }, 403);
      }
      return reply({ id: body.id, players: [{ id: body.id }] });
    }
    if (expireWorld) { expireWorld = false; return reply({ error: "Session expired" }, 401); }
    return reply({ room: { host_id: foreignHost ? "another-player" : body.id }, objects: [], commands: [],
      more: false, after: 0, cursor: "", playerCount: 1 });
  },
});
for (const name of ["world", "online"])
  vm.runInContext(await readFile(new URL(`../Assets/WebGLTemplates/Wyvernland/${name}.js`, import.meta.url), "utf8"), context);
const settle = async () => { for (let i = 0; i < 25; i++) await new Promise(resolve => setImmediate(resolve)); };
async function next(delay) {
  const entry = [...timers].find(([, timer]) => timer.delay === delay);
  assert.ok(entry, "Expected scheduled request at " + delay + "ms");
  timers.delete(entry[0]); entry[1].fn(); await settle();
}
const online = window.wyvernlandOnline, world = window.wyvernlandWorld;
online.attach({ SendMessage: (object, method, json) => messages.push({ object, ...JSON.parse(json) }) });
world.ready("b".repeat(64));
const pose = { room: "room", x: 1, y: 2, z: 3, paused: false, damage: { injury: 1, impacts: [{ id: "old-hit" }] } };
online.publish(JSON.stringify(pose)); await settle();
assert.equal(joins, 1); assert.equal(world.canReportImpacts(), true);

// A world 401 must invalidate presence too, even with a presence response in flight.
holdPresence = true; await next(250);
expireWorld = true; await next(150);
assert.equal(world.canReportImpacts(), false);
assert.doesNotMatch(elements["online-status"].textContent, /expired/i);
finishPresence(); await settle(); await next(250);
assert.equal(joins, 2, "World expiry obtains a fresh token instead of retrying the expired one");
assert.equal(messages.filter(m => m.object === "Wyvernland Shared World").at(-1).self, "player-2");
assert.equal(messages.at(-1).ready, true);
online.reconnect({ id: "player-1", token: "token-1" }); await settle();
assert.equal(joins, 2); assert.equal(world.canReportImpacts(), true, "Old failures cannot invalidate a replacement session");

expirePresence = true; await next(250);
assert.equal(world.canReportImpacts(), false);
assert.doesNotMatch(elements["online-status"].textContent, /expired/i);
await next(250);
assert.equal(joins, 3, "Presence expiry also rejoins with the latest pose");
assert.equal(world.canReportImpacts(), true);

// A host election must not poison the heartbeat until it expires.
rejectHost = true; await next(250);
await next(250);
const latestState = calls.filter(c => c.url === "/api/players" && c.action === "state").at(-1);
assert.deepEqual(latestState.pose.damage.impacts, []);
assert.equal(joins, 3, "Host transfer recovers without unnecessarily replacing the session");
assert.equal(world.canReportImpacts(), false);
assert.match(elements["online-status"].textContent, /1 player/);
assert.equal([...timers.values()].filter(t => t.delay === 8000).length, 0, "Completed requests clear their abort timers");
console.log("PASS: world/presence expiry, automatic rejoin with combat history, in-flight response races, stale-token isolation and host-election recovery");
