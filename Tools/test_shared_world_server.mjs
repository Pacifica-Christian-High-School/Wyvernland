import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { stripTypeScriptTypes } from "node:module";
import { DatabaseSync } from "node:sqlite";
import { createHash } from "node:crypto";
const sqlite = new DatabaseSync(":memory:");
const migrations = new URL("../Sites/Wyvernland/drizzle/", import.meta.url);
for (const file of (await readdir(migrations)).filter(name => name.endsWith(".sql")).sort()) sqlite.exec(await readFile(new URL(file, migrations), "utf8"));
function prepare(sql) {
  let values = [];
  return { bind(...args) { values = args; return this; },
    async run() { const result = sqlite.prepare(sql).run(...values); return { results: [], meta: { changes: Number(result.changes) } }; },
    async all() { return { results: sqlite.prepare(sql).all(...values), meta: { changes: 0 } }; },
    async first() { return sqlite.prepare(sql).get(...values) ?? null; },
    async execute() { return /^\s*SELECT/.test(sql) ? this.all() : this.run(); } };
}
globalThis.__sharedTestEnv = { DB: { prepare, async batch(statements) { sqlite.exec("BEGIN");
  try { const results = []; for (const statement of statements) results.push(await statement.execute()); sqlite.exec("COMMIT"); return results; }
  catch (error) { sqlite.exec("ROLLBACK"); throw error; } } } };
const asUrl = source => "data:text/javascript;base64," + Buffer.from(stripTypeScriptTypes(source)).toString("base64");
let apiSource = await readFile(new URL("../Sites/Wyvernland/lib/world-api.ts", import.meta.url), "utf8");
apiSource = apiSource.replace('import { env } from "cloudflare:workers";', "const env = globalThis.__sharedTestEnv;");
const sharedUrl = asUrl(apiSource);
async function route(name) {
  let source = await readFile(new URL(`../Sites/Wyvernland/app/api/${name}/route.ts`, import.meta.url), "utf8");
  source = source.replace('import { env } from "cloudflare:workers";', "const env = globalThis.__sharedTestEnv;")
    .replace('"../../../lib/world-api"', JSON.stringify(sharedUrl));
  return import(asUrl(source));
}
const rooms = await route("rooms"), players = await route("players"), world = await route("world");
async function call(handler, body, expected = 200, headers = {}) {
  const result = await handler(new Request("https://game.example/api/world", { method: "POST", headers: { "Content-Type": "application/json", ...headers }, body: JSON.stringify(body) }));
  const data = await result.json(); assert.equal(result.status, expected, data.error); return data;
}
const createdA = await call(rooms.POST, { name: "Our world", map: "test-ground" }, 201), roomA = createdA.room;
const roomB = (await call(rooms.POST, { name: "Other world", map: "test-ground" }, 201)).room;
const lookup = await rooms.GET(new Request("https://game.example/api/rooms?code=" + roomA.code.toLowerCase()));
assert.equal((await lookup.json()).room.id, roomA.id);
assert.equal(roomA.canManage, true);
const guestLookup = await rooms.GET(new Request("https://game.example/api/rooms?code=" + roomA.code));
assert.equal((await guestLookup.json()).room.canManage, false, "Invite codes do not confer creator access");
const creatorLookup = await rooms.GET(new Request("https://game.example/api/rooms?code=" + roomA.code,
  { headers: { "X-Wyvernland-Room-Owner": createdA.ownerToken } }));
assert.equal((await creatorLookup.json()).room.canManage, true, "Creator access can be restored in the creator's browser");
const pose = { x: 2, y: 1, z: -35, yaw: 0, pitch: 0, speed: 0, crouching: false, grounded: true, paused: false };
async function join(room, name) { const value = await call(players.POST, { action: "join", room: room.id, name, pose }); return { id: value.id, token: value.token }; }
const a = await join(roomA, "A"), b = await join(roomA, "B"), outsider = await join(roomB, "Outsider");
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { injury: 0, life: 0, hit: 0, pose: [], pits: [], corpses: [], impacts: [], received: [] } } });
const bonePose = Array.from({ length: 105 }, (_, i) => i % 7 === 6 ? 1 : 0);
const injuries = { injury: 5, life: 2, hit: 3, pose: bonePose, pits: [{ point: { x: 0, y: 1.2, z: .1 } }],
  corpses: [{ id: "corpse-a", position: { x: 2, y: 1, z: -35 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, pose: bonePose,
    left: "", right: "b" + "9".repeat(24), lp: { x: 0, y: 0, z: 0 }, rp: { x: .1, y: 0, z: 0 },
    lq: { x: 0, y: 0, z: 0, w: 1 }, rq: { x: 0, y: 0, z: 0, w: 1 } }] };
injuries.severed = 5;injuries.limbHits = [4, 0, 4, 0];
injuries.limbs = [{ ...injuries.corpses[0], id: "Detached arm", limb: 1, missing: 0 }, { ...injuries.corpses[0], id: "Detached leg", limb: 3, missing: 0 }];
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: injuries } });
const injurySnapshot = await call(players.POST, { action: "state", ...b, pose });
assert.deepEqual(injurySnapshot.players.find(p => p.id === a.id).damage, injuries, "Injuries, Boolean pit contacts, ragdoll bones, and corpse grip identities reach other players");
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { ...injuries, pose: [Infinity] } } }, 400);
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { ...injuries, corpses: Array(4).fill(injuries.corpses[0]) } } }, 400);
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { injury: 0, life: 3, hit: 3 } } });
console.log("PASS: bounded player injuries, skinned pit contacts, corpse grip and bone poses, and respawn state synchronize through authenticated presence");
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { ...injuries, limbs: [{ ...injuries.limbs[0], limb: 99 }] } } }, 400);
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { ...injuries, limbHits: [-1, 0, 0, 0] } } }, 400);
console.log("PASS: severed limb identities, independent limb hit counters and bone poses synchronize with bounded state");
const baseline = "a".repeat(64);
const action = (session, body, expected = 200) => call(world.POST, { ...body, ...session }, expected);
const heartbeatBefore = Date.now() - 19_000;
sqlite.prepare("UPDATE players SET last_seen=? WHERE id=?").run(heartbeatBefore, a.id);
await action({ ...a, token: "x".repeat(72) }, { action: "read" }, 401);
assert.equal(sqlite.prepare("SELECT last_seen FROM players WHERE id=?").get(a.id).last_seen, heartbeatBefore, "Invalid tokens cannot renew a session");
await action(a, { action: "read" });
assert.ok(sqlite.prepare("SELECT last_seen FROM players WHERE id=?").get(a.id).last_seen > heartbeatBefore + 18_000,
  "Authenticated world traffic keeps a session alive when presence requests are delayed");
console.log("PASS: authenticated shared-world requests renew the session without weakening token checks");
for (const session of [a, b, outsider]) { await action(session, { action: "ready", baseline }); await action(session, { action: "activate", baseline }); }
assert.equal((await action(a, { action: "read" })).room.host_id, a.id);
await action(b, { action: "command", command: { type: "spawn-npc", point: [pose.x+4,pose.y,pose.z], incoming: [0,0,0] } });
await action(b, { action: "command", command: { type: "spawn-npc", point: [pose.x+40,pose.y,pose.z], incoming: [0,0,0] } },400);
const npcCommands=(await action(a, {action:"read"})).commands;
assert.equal(npcCommands.length,1);assert.equal(npcCommands[0].type,"spawn-npc");assert.equal(npcCommands[0].player,b.id);
await action(a,{action:"commit",objects:[],ack:[npcCommands[0].commandId]});
assert.equal((await action(a,{action:"read"})).commands.length,0);
console.log("PASS: bounded NPC spawn requests reach only the physics host and are acknowledged through the room queue");
const base = { id: "b" + "a".repeat(24), name: "Stone", p: [2, 1, -35], q: [0, 0, 0, 1], s: [1, 1, 1], v: [0, 0, 0], w: [0, 0, 0], body: true, kinematic: false, gravity: true, sleeping: true, mesh: "" };
const confirmedHit = { id: "c".repeat(32), target: b.id, source: base.id, life: 0, part: 4,
  point: { x: 2, y: 2.5, z: -35 }, normal: { x: 0, y: -1, z: 0 }, velocity: { x: 0, y: -30, z: 0 }, threshold: 22.3, mass: 20 };
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { injury: 0, life: 3, hit: 3, impacts: [confirmedHit] } } });
const confirmedSnapshot = await call(players.POST, { action: "state", ...b, pose });
assert.deepEqual(confirmedSnapshot.players.find(p => p.id === a.id).damage.impacts, [confirmedHit], "Pre-solver hit velocity reaches the victim even after the projectile stops or shatters");
await call(players.POST, { action: "state", ...b, pose: { ...pose, damage: { injury: 0, life: 0, hit: 0, impacts: [confirmedHit] } } }, 403);
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { injury: 0, life: 3, hit: 3, impacts: [{ ...confirmedHit, threshold: 1 }] } } }, 400);
await call(players.POST, { action: "state", ...a, pose: { ...pose, damage: { injury: 0, life: 3, hit: 3, impacts: [null] } } }, 400);
await call(players.POST, { action: "state", ...b, pose: { ...pose, damage: { injury: 8, life: 0, hit: 1, received: [confirmedHit.id] } } });
assert.deepEqual((await call(players.POST, { action: "state", ...a, pose })).players.find(p => p.id === b.id).damage.received, [confirmedHit.id], "Victim acknowledgements return to the host");
console.log("PASS: only the authenticated physics host can relay bounded, body-specific impact events with original motion");
const meshData = Buffer.from("authoritative mesh fixture").toString("base64"), meshHash = createHash("sha256").update(Buffer.from(meshData, "base64")).digest("hex");
await action(a, { action: "mesh-write", hash: meshHash, parts: [{ part: 0, total: 1, data: meshData }] });
await action(a, { action: "commit", objects: [{ ...base, mesh: meshHash }] });
assert.equal((await action(b, { action: "read" })).objects[0].sleeping, true, "Sleeping bodies stay asleep for late clients");
await action(b, { action: "commit", objects: [base] }, 403);
assert.equal((await action(outsider, { action: "read", room: roomA.id })).objects.length, 0);
assert.equal((await action(b, { action: "claim", object: base.id, state: base })).owned, true);
assert.equal((await action(a, { action: "claim", object: base.id, state: base })).owned, false, "One object has one holder");
assert.equal((await action(a, { action: "unclaim", object: base.id })).accepted, false, "Only the reservation owner can cancel it");
assert.equal((await action(b, { action: "unclaim", object: base.id })).accepted, true);
const cancelled = (await action(a, { action: "read" })).objects.find(value => value.id === base.id);
assert.equal(cancelled.holder, ""); assert.equal(cancelled.kinematic, false); assert.equal(cancelled.gravity, true);
assert.deepEqual(cancelled.p, base.p, "Reservation cancellation preserves the host's physical pose");
await action(a, { action: "commit", objects: [{ ...base, body: false, gravity: false }] });
await action(b, { action: "claim", object: base.id, state: base });
await action(b, { action: "unclaim", object: base.id });
const supported = (await action(a, { action: "read" })).objects.find(value => value.id === base.id);
assert.equal(supported.body, false); assert.equal(supported.gravity, false, "Cancelling a reach cannot create a Rigidbody on a supported static prop");
await action(a, { action: "commit", objects: [base] });
await action(b, { action: "claim", object: base.id, state: base });
await action(b, { action: "held", object: base.id, state: { ...base, p: [4, 2, -35], hand: "left" } });
assert.equal((await action(b, { action: "unclaim", object: base.id })).accepted, false, "Actual held objects require a real drop, not reservation cancellation");
await action(a, { action: "commit", objects: [{ ...base, p: [99, 0, 0], mesh: meshHash }] });
const held = (await action(a, { action: "read" })).objects.find(value => value.id === base.id);
assert.equal(held.holder, b.id); assert.deepEqual(held.p, [4, 2, -35]); assert.equal(held.hand, "left");
assert.equal(held.sleeping, false, "Holding a sleeping prop must activate its collision bubble");
await action(b, { action: "release", object: base.id, state: { ...base, p: [6, 2, -35], v: [3, 0, 0] } });
await action(a, { action: "commit", objects: [{ ...base, p: [4, 2, -35], held: true, kinematic: true, gravity: false }] });
const afterLateHeld = (await action(a, { action: "read" })).objects.find(value => value.id === base.id);
assert.equal(afterLateHeld.held, false, "A late held snapshot cannot resurrect a grip after release");
assert.equal(afterLateHeld.kinematic, false); assert.equal(afterLateHeld.gravity, true);
assert.deepEqual(afterLateHeld.p, [6, 2, -35], "A late held commit cannot overwrite the released pose or velocity");
assert.deepEqual(afterLateHeld.v, [3, 0, 0]);
await action(b, { action: "claim", object: base.id, state: base });
await action(b, { action: "held", object: base.id, state: { ...base, p: [4, 2, -35] } });
const strikeCommand = { type: "strike", object: base.id, target: "b" + "b".repeat(24), point: [4, 1, -35], incoming: [20, 0, 0], hasStrikeRoll: true, strikeRoll: 0.7 };
for (const strikeRoll of [-0.01, 1.01, null, "0.2"]) await action(b, { action: "command", command: { ...strikeCommand, strikeRoll } }, 400);
await action(b, { action: "command", command: strikeCommand });
const commands = (await action(a, { action: "read" })).commands;
assert.equal(commands.length, 1); assert.equal(commands[0].player, b.id);
assert.equal(commands[0].hasStrikeRoll, true); assert.equal(commands[0].strikeRoll, 0.7, "The host replays the guest's single contact roll without rerolling");
const fragment = { ...base, id: "d" + "c".repeat(32), name: "Stone fragment", p: [4, 0, -35], mesh: meshHash, physical: { count: 1, modified: true } };
await action(a, { action: "commit", objects: [{ id: base.id, deleted: true }, fragment], ack: [commands[0].commandId] });
const destroyed = await action(b, { action: "read" });
assert.ok(destroyed.objects.find(value => value.id === base.id).deleted);
assert.equal(destroyed.objects.find(value => value.id === base.id).holder, "");
assert.equal(destroyed.objects.find(value => value.id === fragment.id).physical.count, 1);
assert.equal((await action(a, { action: "read" })).commands.length, 0);
const late = await join(roomA, "Late joiner");
await action(late, { action: "ready", baseline });
const restored = await action(late, { action: "read" });
assert.ok(restored.objects.some(value => value.id === fragment.id && value.mesh === meshHash));
assert.equal((await action(late, { action: "mesh-read", hash: meshHash, part: 0 })).parts[0].data, meshData);
await action(late, { action: "activate", baseline });
assert.equal((await action(b, { action: "claim", object: base.id, state: base })).owned, false, "Destroyed objects cannot be picked up");
await action(b, { action: "claim", object: fragment.id, state: { ...fragment, mesh: "" } });
await action(b, { action: "held", object: fragment.id, state: { ...fragment, p: [3, 2, -35] } });
sqlite.prepare("UPDATE players SET last_seen=? WHERE id=?").run(Date.now() - 21000, b.id);
await action(a, { action: "read" });
const freed = (await action(a, { action: "read" })).objects.find(value => value.id === fragment.id);
assert.equal(freed.holder, ""); assert.equal(!!freed.kinematic, false); assert.deepEqual(freed.p, [3, 2, -35]);
assert.equal(!!freed.sleeping, false, "Disconnect releases must wake dropped props");
sqlite.prepare("UPDATE players SET last_seen=? WHERE id=?").run(Date.now() - 21000, a.id);
assert.equal((await action(late, { action: "read" })).room.host_id, late.id, "Host transfers after a disconnect");
const many = Array.from({ length: 70 }, (_, i) => ({ ...base, id: "b" + i.toString(16).padStart(24, "0") }));
for (let offset=0;offset<many.length;offset+=32) await action(late, { action: "commit", objects: many.slice(offset, offset+32) });
let page = await action(late, { action: "read" }), all = [...page.objects];
while (page.more) { page = await action(late, { action: "read", after: page.after, cursor: page.cursor, until: page.until }); all.push(...page.objects); }
assert.equal(new Set(all.map(value => value.id)).size, 72, "Snapshot pages preserve every changed object");
const fragmentGuest = await join(roomA, "Fragment holder");
await action(fragmentGuest, { action: "ready", baseline });await action(fragmentGuest, { action: "activate", baseline });
async function readFragmentWorld() {
  let snapshot = await action(fragmentGuest, { action: "read" }), objects = [...snapshot.objects];
  while (snapshot.more) { snapshot = await action(fragmentGuest, { action: "read", after: snapshot.after, cursor: snapshot.cursor, until: snapshot.until }); objects.push(...snapshot.objects); }
  return objects;
}
const brokenSource = { ...base, id: "b" + "6".repeat(24) };
const retainedPiece = { ...base, id: "d" + "7".repeat(32), name: "Held fragment", mesh: meshHash,
  held: true, kinematic: true, gravity: false, holder: fragmentGuest.id, gripSource: brokenSource.id, hand: "left", grip: [2, 1.2, -35] };
await action(late, { action: "commit", objects: [brokenSource] });
await action(fragmentGuest, { action: "claim", object: brokenSource.id, state: brokenSource });
await action(fragmentGuest, { action: "held", object: brokenSource.id, state: { ...brokenSource, hand: "left", grip: retainedPiece.grip } });
const brokenBatch = [{ id: brokenSource.id, deleted: true }, retainedPiece];
await action(late, { action: "commit", objects: brokenBatch });
let fragmentRead = await readFragmentWorld();
assert.equal(fragmentRead.find(s => s.id === retainedPiece.id).holder, fragmentGuest.id, "Source deletion cannot erase the retained fragment's inherited owner");
assert.equal(fragmentRead.find(s => s.id === retainedPiece.id).gripSource, brokenSource.id);
assert.deepEqual(fragmentRead.find(s => s.id === retainedPiece.id).grip, retainedPiece.grip);
assert.equal((await action(late, { action: "claim", object: retainedPiece.id, state: { ...retainedPiece, mesh: "" } })).owned, false, "Other players cannot steal a retained fragment");
await action(late, { action: "commit", objects: brokenBatch });
assert.equal((await action(fragmentGuest, { action: "release", object: retainedPiece.id, state: retainedPiece })).accepted, true);
await action(late, { action: "commit", objects: brokenBatch });
fragmentRead = await readFragmentWorld();
assert.equal(fragmentRead.find(s => s.id === retainedPiece.id).holder, "", "Retrying a fracture cannot reattach a fragment after it was dropped");
assert.equal(fragmentRead.find(s => s.id === retainedPiece.id).held, false);
const partialSource = { ...base, id: "b" + "8".repeat(24) };
const partialPiece = { ...retainedPiece, id: "d" + "9".repeat(32), gripSource: partialSource.id };
await action(late, { action: "commit", objects: [partialSource] });
await action(fragmentGuest, { action: "claim", object: partialSource.id, state: partialSource });
await action(fragmentGuest, { action: "held", object: partialSource.id, state: partialSource });
await action(late, { action: "commit", objects: [partialSource, partialPiece] });
fragmentRead = await readFragmentWorld();
assert.equal(fragmentRead.find(s => s.id === partialPiece.id).holder, fragmentGuest.id);
assert.equal(fragmentRead.find(s => s.id === partialSource.id).holder, "");
assert.equal(fragmentRead.find(s => s.id === partialSource.id).held, false);
assert.equal(fragmentRead.find(s => s.id === partialSource.id).gravity, true, "An unselected partial remainder continues falling");
await action(fragmentGuest, { action: "commit", objects: [{ ...partialPiece, id: "d" + "a".repeat(32) }] }, 403);
console.log("PASS: atomic retained-fragment ownership for full/partial destruction, live hand coordinates, guest exclusivity, normal release, and retry safety");
const dummyState = { ...base, id: "b" + "5".repeat(24), name: "Test Ground damage dummy (20,20)", p: [20, .15, 20], hasDummy: true,
  dummy: { damage: { injury: 5, life: 2, hit: 3, pose: bonePose, pits: [], corpses: [] } } };
await action(late, { action: "commit", objects: [dummyState] });
assert.deepEqual((await readFragmentWorld()).find(s => s.id === dummyState.id).dummy, dummyState.dummy, "The world host persists dummy injury and bone state for all room visitors");
await action(fragmentGuest, { action: "commit", objects: [{ ...dummyState, dummy: { damage: { injury: 0 } } }] }, 403);
console.log("PASS: Test Ground dummy damage uses shared-world persistence and host authority");
const permanentBody = { ...base, id: "d" + "c".repeat(32), name: "Persistent corpse", pickable: true,
  hasCorpse: true, expires: 0, kinematic: true, gravity: false,
  corpse: { id: "Player corpse test", position: { x: 2, y: 1, z: -35 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, pose: bonePose } };
await action(late, { action: "commit", objects: [permanentBody] });
assert.equal((await action(fragmentGuest, { action: "claim", object: permanentBody.id, state: permanentBody })).owned, true);
const draggedBody = { ...permanentBody, corpse: { ...permanentBody.corpse, pose: bonePose.map((n, i) => i === 0 ? 3 : n) } };
await action(fragmentGuest, { action: "held", object: permanentBody.id, state: draggedBody });
assert.deepEqual((await readFragmentWorld()).find(s => s.id === permanentBody.id).corpse, draggedBody.corpse, "Held corpse publishes its articulated pose");
await action(fragmentGuest, { action: "release", object: permanentBody.id, state: draggedBody });
let bodyRead = (await readFragmentWorld()).find(s => s.id === permanentBody.id);
assert.equal(bodyRead.kinematic, true, "Ragdoll root remains kinematic while its bones simulate");
assert.deepEqual(bodyRead.corpse, draggedBody.corpse);
// Body persistence is independent of the dead player's presence and 30-second timers.
await action(fragmentGuest, { action: "claim", object: permanentBody.id, state: draggedBody });
await action(fragmentGuest, { action: "held", object: permanentBody.id, state: draggedBody });
sqlite.prepare("UPDATE players SET last_seen=? WHERE id=?").run(Date.now() - 40_000, fragmentGuest.id);
const persistedBodies = await action(late, { action: "read" });
assert.equal(sqlite.prepare("SELECT COUNT(*) AS n FROM world_objects WHERE id=?").get(permanentBody.id).n, 1);
assert.equal(bodyRead.expires, 0);
const disconnectedBody = sqlite.prepare("SELECT state,holder FROM world_objects WHERE id=?").get(permanentBody.id);
assert.equal(disconnectedBody.holder, null);
assert.equal(JSON.parse(disconnectedBody.state).kinematic, 1);
assert.deepEqual(JSON.parse(disconnectedBody.state).corpse, draggedBody.corpse, "Disconnected holder leaves the complete last ragdoll pose in the room");
sqlite.prepare("UPDATE players SET last_seen=? WHERE id=?").run(Date.now(), fragmentGuest.id);
console.log("PASS: pickable permanent corpses retain articulated poses across holding, release, owner disconnect and late joins");
const thrown = { ...base, id: "d" + "d".repeat(32), pickable: true, p: [2, 1, -35] };
await action(late, { action: "commit", objects: [thrown] });
await action(fragmentGuest, { action: "claim", object: thrown.id, state: thrown });
await action(fragmentGuest, { action: "held", object: thrown.id, state: { ...thrown, p: [3, 1, -35] } });
const released = { ...thrown, p: [3.5, 1.5, -35], v: [20, 8, 0], releaseId: "a".repeat(32) };
const releaseAck = await action(fragmentGuest, { action: "release", object: thrown.id, state: released });
assert.equal(releaseAck.accepted, true);assert.ok(releaseAck.revision > 0);
await action(late, { action: "commit", objects: [{ ...thrown, p: [3, 1, -35], v: [0, 0, 0] }] });
let throwRead = (await readFragmentWorld()).find(s => s.id === thrown.id);
assert.deepEqual(throwRead.p, released.p, "A stale host pose cannot overwrite the launch point");
assert.deepEqual(throwRead.v, released.v, "Stale host writes cannot cancel a guest's throw velocity");
assert.equal(throwRead.held, false);
const inFlight = { ...released, p: [6, 2.4, -35], v: [20, 6.5, 0] };
await action(late, { action: "commit", objects: [inFlight] });
const retryAck = await action(fragmentGuest, { action: "release", object: thrown.id, state: released });
assert.equal(retryAck.accepted, true, "Retrying a release after losing its HTTP response is idempotent");
throwRead = (await readFragmentWorld()).find(s => s.id === thrown.id);
assert.deepEqual(throwRead.p, inFlight.p, "A release retry cannot rewind a moving object");
assert.equal(throwRead.releaseId, released.releaseId);
const countSnapshot = await action(late, { action: "read" });
assert.equal(countSnapshot.playerCount, sqlite.prepare("SELECT COUNT(*) AS n FROM players WHERE room=? AND last_seen>=?").get(roomA.id, Date.now() - 20_000).n);
console.log("PASS: throw acknowledgements, original launch velocity, stale-host rejection, release retry safety, and authoritative room player count");
await action(late, { action: "ready", baseline: "b".repeat(64) }, 409);
await action({ ...late, token: "x".repeat(72) }, { action: "read" }, 401);
await call(world.POST, { ...late, action: "read" }, 403, { Origin: "https://elsewhere.example" });
const invalid = { ...base, id: "d" + "e".repeat(32), p: [0, -10001, 0] };
const healthy = { ...many[0], p: [3, 2, -35] };
const mixed = await action(late, { action: "commit", objects: [invalid, healthy, null,
  { ...base, id: "d" + "f".repeat(32), v: [null, 0, 0] },
  { ...base, id: "d" + "1".repeat(32), name: "x".repeat(48000) }] });
assert.equal(mixed.accepted, true);
assert.deepEqual(mixed.rejected.map(value => value.reason), ["p", "id", "v", "size"]);
const recovered = (await action(late, { action: "read", after: page.after })).objects;
assert.deepEqual(recovered.find(value => value.id === healthy.id).p, healthy.p, "A bad physics sample cannot block other world changes");
assert.equal(recovered.some(value => value.id === invalid.id), false, "Invalid samples never enter the shared world");
await action(late, { action: "commit", objects: [base, ...many.slice(0, 32)] }, 400);
await action(late, { action: "commit", objects: null }, 400);
await call(rooms.POST, { action: "share", room: roomA.id, ownerToken: b.token, name: "Guest takeover" }, 403);
await call(rooms.POST, { action: "share", room: roomB.id, ownerToken: createdA.ownerToken, name: "Wrong world" }, 403);
const beforeShare = sqlite.prepare("SELECT baseline,revision FROM rooms WHERE id=?").get(roomA.id);
const beforeObjects = sqlite.prepare("SELECT COUNT(*) AS count FROM world_objects WHERE room=?").get(roomA.id).count;
const shared = await call(rooms.POST, { action: "share", room: roomA.id, ownerToken: createdA.ownerToken, name: "Our current world", map: "island" });
assert.equal(shared.room.id, roomA.id); assert.equal(shared.room.code, roomA.code); assert.equal(shared.room.map, "test-ground");
assert.deepEqual(sqlite.prepare("SELECT baseline,revision FROM rooms WHERE id=?").get(roomA.id), beforeShare);
assert.equal(sqlite.prepare("SELECT COUNT(*) AS count FROM world_objects WHERE room=?").get(roomA.id).count, beforeObjects, "Sharing preserves the existing world's changes");
assert.equal((await action(late, { action: "read" })).room.owner_token_hash, undefined);
console.log("PASS: rooms, pickup exclusivity, held-object visibility, host authority, destruction and mesh persistence, late joins, disconnect release, host transfer, snapshot pagination");
console.log("PASS: creator-only sharing preserves the current world after physics host transfer");
console.log("PASS: malformed and out-of-bounds samples are isolated while valid world changes continue");
const laggedStone = { ...base, id: "b" + "e".repeat(24), p: [22, 1, -35] };
assert.equal((await action(late, { action: "claim", object: laggedStone.id, state: laggedStone })).owned, true,
  "A reachable held stone remains claimable when presence trails the world request by more than ten metres");
await action(late, { action: "unclaim", object: laggedStone.id });
await action(late, { action: "claim", object: "b" + "f".repeat(24), state: { ...base, p: [42, 1, -35] } }, 400);
console.log("PASS: lagged room presence does not reject a valid stone pickup");
const settledChip = { ...base, id: "d" + "8f".repeat(16), p: [3, .025, -35],
  smallFragment: true, shatterDebris: true, fragmentSettled: true,
  body: false, kinematic: false, gravity: false, sleeping: false,
  v: [0, 0, 0], w: [0, 0, 0], expires: 0,
  physical: { ...base.physical, fragment: true } };
await action(late, { action: "commit", objects: [settledChip] });
const restoredChip = (await readFragmentWorld()).find(state => state.id === settledChip.id);
assert.equal(restoredChip.smallFragment, true);
assert.equal(restoredChip.shatterDebris, true);
assert.equal(restoredChip.fragmentSettled, true);
assert.equal(restoredChip.body, false);
assert.equal(restoredChip.expires, 0);
assert.equal(restoredChip.deleted, false);
console.log("PASS: settled small fragment geometry remains in shared rooms without physics or expiry");
sqlite.close(); delete globalThis.__sharedTestEnv;
