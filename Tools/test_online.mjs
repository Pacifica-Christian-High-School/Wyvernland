import assert from "node:assert/strict";

const origin = process.argv[2] ?? "http://127.0.0.1:5173";
const pose = { x: 2, y: 1, z: -35, yaw: 0, pitch: 0, speed: 0,
  crouching: false, grounded: true, paused: false };
const sessions = [];
async function call(body, expected = 200, headers = {}) {
  const response = await fetch(origin + "/api/players", { method: "POST",
    headers: { "Content-Type": "application/json", Origin: new URL(origin).origin, ...headers }, body: JSON.stringify(body) });
  const raw = await response.text();
  let data;
  try { data = JSON.parse(raw); } catch {
    throw new Error(`${body.action}: expected ${expected}, got ${response.status}: ${raw.slice(0, 180)}`);
  }
  assert.equal(response.status, expected, data.error ?? "Unexpected status");
  return data;
}
async function join(name, room = "test-ground") {
  const result = await call({ action: "join", name, room, pose });
  const session = { id: result.id, token: result.token };
  sessions.push(session);
  return { session, result };
}
try {
  const a = await join("Presence test A");
  const b = await join("Presence test B");
  assert.ok(b.result.players.some(p => p.id === a.session.id), "B must see A");
  const seen = await call({ action: "state", ...a.session, pose });
  assert.ok(seen.players.some(p => p.id === b.session.id), "A must see B");
  await call({ action: "state", ...b.session, pose: { ...pose, x: 12, yaw: 90, speed: 4, crouching: true } });
  const moved = await call({ action: "state", ...a.session, pose });
  const other = moved.players.find(p => p.id === b.session.id);
  assert.equal(other.x, 12); assert.equal(other.yaw, 90); assert.equal(other.speed, 4);
  assert.equal(other.crouching, true);
  assert.ok(!("token_hash" in other) && !("ip_hash" in other), "Private session values must stay private");
  const island = await join("Presence test island", "island");
  assert.ok(!island.result.players.some(p => p.id === a.session.id), "Maps must stay separate");
  await call({ action: "state", ...b.session, token: "x".repeat(72), pose }, 401);
  await call({ action: "state", ...b.session, pose: { ...pose, x: 999999 } }, 400);
  await call({ action: "leave", ...b.session });
  const left = await call({ action: "state", ...a.session, pose });
  assert.ok(!left.players.some(p => p.id === b.session.id), "Departed players must disappear");
  if (process.argv.includes("--expiry")) {
    await new Promise(resolve => setTimeout(resolve, 21000));
    await call({ action: "state", ...a.session, pose }, 401);
    const fresh = await join("Presence test after expiry");
    assert.ok(!fresh.result.players.some(p => p.id === a.session.id), "Expired players must disappear");
  }
  await call({ action: "join", name: "test", room: "island", pose }, 403,
    { Origin: "https://unrelated.example", Connection: "close" });
  console.log("PASS: two independent players, movement, map separation, session authorization, departure" +
    (process.argv.includes("--expiry") ? ", expiry and rejoin" : ""));
} finally {
  await Promise.allSettled(sessions.map(session => call({ action: "leave", ...session })));
}
