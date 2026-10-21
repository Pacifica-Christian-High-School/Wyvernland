import assert from "node:assert/strict";
import { createHash } from "node:crypto";
const origin = process.argv[2] ?? "http://127.0.0.1:5173", sessions = [];
let heartbeat;
const pose = { x: 2, y: 1, z: -35, yaw: 0, pitch: 0, speed: 0, crouching: false, grounded: true, paused: false };
async function call(route, body, expected = 200) {
  const response = await fetch(origin + "/api/" + route, { method: "POST", headers: { "Content-Type": "application/json", Origin: new URL(origin).origin }, body: JSON.stringify(body) });
  const raw = await response.text(); let result;
  try { result = JSON.parse(raw); } catch { throw new Error(`${route}/${body.action}: ${response.status} ${raw.slice(0, 200)}`); }
  assert.equal(response.status, expected, result.error); return result;
}
const baseline = "b".repeat(64), action = (s, body, expected) => call("world", { ...body, ...s }, expected);
async function join(room, name) {
  const value = await call("players", { action: "join", room: room.id, name, pose });
  const session = { id: value.id, token: value.token }; sessions.push(session); return session;
}
async function activate(s) { await action(s, { action: "ready", baseline }); await action(s, { action: "activate", baseline }); }
try {
  heartbeat = setInterval(() => { for (const session of sessions)
    call("players", { action: "state", ...session, pose }).catch(() => {}); }, 4000);
  const created = await call("rooms", { name: "Shared world verification", map: "test-ground" }, 201), room = created.room;
  const found = await fetch(origin + "/api/rooms?code=" + room.code.toLowerCase());
  assert.equal((await found.json()).room.id, room.id);
  const a = await join(room, "Room test A"), b = await join(room, "Room test B");
  await activate(a); await activate(b);
  assert.equal((await action(a, { action: "read" })).room.host_id, a.id);
  const visible = await call("players", { action: "state", ...a, pose });
  assert.ok(visible.players.some(p => p.id === b.id), "Players in the same room see each other");
  const state = { id: "b" + "a".repeat(24), name: "Stone", p: [2, 1, -35], q: [0, 0, 0, 1], s: [1, 1, 1], v: [0, 0, 0], w: [0, 0, 0], body: true, gravity: true, kinematic: false, mesh: "" };
  const meshData = Buffer.from("shared mesh ".repeat(9000)).toString("base64"), hash = createHash("sha256").update(Buffer.from(meshData, "base64")).digest("hex");
  const parts = Array.from({ length: Math.ceil(meshData.length / 65536) }, (_, part) => ({ part, total: Math.ceil(meshData.length / 65536), data: meshData.slice(part * 65536, (part + 1) * 65536) }));
  await action(a, { action: "mesh-write", hash, parts });
  await action(a, { action: "commit", objects: [{ ...state, mesh: hash }] });
  assert.equal((await action(b, { action: "claim", object: state.id, state })).owned, true);
  assert.equal((await action(a, { action: "claim", object: state.id, state })).owned, false);
  await action(b, { action: "held", object: state.id, state: { ...state, p: [3, 2, -35], hand: "left" } });
  await action(a, { action: "commit", objects: [{ ...state, p: [8, 1, -35], mesh: hash }] });
  let seen = (await action(a, { action: "read" })).objects.find(o => o.id === state.id);
  assert.equal(seen.holder, b.id); assert.deepEqual(seen.p, [3, 2, -35]); assert.equal(seen.held, true);
  await action(b, { action: "release", object: state.id, state: { ...state, p: [4, 2, -35], v: [9, 0, 0] } });
  seen = (await action(a, { action: "read" })).objects.find(o => o.id === state.id);
  assert.equal(seen.holder, ""); assert.deepEqual(seen.v, [9, 0, 0]); assert.equal(seen.held, false);
  await action(b, { action: "command", command: { type: "drag", object: state.id, point: [4, 1, -35], incoming: [3, 0, 0] } });
  const commands = (await action(a, { action: "read" })).commands;
  assert.equal(commands[0].player, b.id);
  const fragment = { ...state, id: "d" + "c".repeat(32), name: "Stone fragment", mesh: hash, p: [4, 0, -35], hasPhysical: true, physical: { count: 1, modified: true } };
  await action(a, { action: "commit", objects: [{ id: state.id, deleted: true }, fragment], ack: commands.map(c => c.commandId) });
  const late = await join(room, "Late room test"); await action(late, { action: "ready", baseline });
  const restored = await action(late, { action: "read" });
  assert.equal(restored.objects.find(o => o.id === state.id).deleted, true);
  assert.equal(restored.objects.find(o => o.id === fragment.id).mesh, hash);
  await call("rooms", { action: "share", room: room.id, ownerToken: b.token, name: "Guest takeover" }, 403);
  const shared = await call("rooms", { action: "share", room: room.id, ownerToken: created.ownerToken, name: "Shared current world" });
  assert.equal(shared.room.id, room.id); assert.equal(shared.room.code, room.code);
  assert.ok((await action(late, { action: "read" })).objects.some(o => o.id === fragment.id), "Creating a room from inside the world preserves its destruction");
  assert.equal((await action(late, { action: "mesh-read", hash, part: 0 })).parts.map(p => p.data).join(""), meshData);
  await action(late, { action: "activate", baseline });
  await action(b, { action: "commit", objects: [state] }, 403);
  await call("players", { action: "leave", ...a });
  assert.equal((await action(b, { action: "read" })).room.host_id, b.id, "An active guest takes over physics");
  await call("players", { action: "leave", ...b }); await call("players", { action: "leave", ...late });
  const returning = await join(room, "Returning room test"); await action(returning, { action: "ready", baseline });
  assert.ok((await action(returning, { action: "read" })).objects.some(o => o.id === fragment.id), "Room state survives after everyone leaves");
  await action(returning, { action: "activate", baseline });
  assert.equal((await action(returning, { action: "read" })).room.host_id, returning.id);
  console.log("PASS: live HTTP rooms, player visibility, held objects, throws, destruction, mesh persistence, late joins, host transfer, empty-room persistence");
} finally { clearInterval(heartbeat); await Promise.allSettled(sessions.map(s => call("players", { action: "leave", ...s }))); }
