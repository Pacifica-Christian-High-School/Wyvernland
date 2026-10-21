// Run the actual route and SQL with an in-memory SQLite adapter before deployment.
import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { stripTypeScriptTypes } from "node:module";
import { DatabaseSync } from "node:sqlite";
const sqlite = new DatabaseSync(":memory:");
const migrations = new URL("../Sites/Wyvernland/drizzle/", import.meta.url);
for (const file of (await readdir(migrations)).filter(name => name.endsWith(".sql")).sort()) sqlite.exec(await readFile(new URL(file, migrations), "utf8"));
function prepare(sql) {
  let values = [];
  return {
    bind(...next) { values = next; return this; },
    async run() { const result = sqlite.prepare(sql).run(...values); return { meta: { changes: Number(result.changes) }, results: [] }; },
    async first() { return sqlite.prepare(sql).get(...values) ?? null; },
    async execute() {
      if (/^\s*SELECT/.test(sql)) return { meta: { changes: 0 }, results: sqlite.prepare(sql).all(...values) };
      return this.run();
    },
  };
}
globalThis.__onlineTestEnv = { DB: { prepare, async batch(statements) {
  sqlite.exec("BEGIN");
  try { const results = []; for (const statement of statements) results.push(await statement.execute());
    sqlite.exec("COMMIT"); return results;
  } catch (error) { sqlite.exec("ROLLBACK"); throw error; }
} } };
let source = await readFile(new URL("../Sites/Wyvernland/app/api/players/route.ts", import.meta.url), "utf8");
source = source.replace('import { env } from "cloudflare:workers";', 'const env = globalThis.__onlineTestEnv;');
const { POST } = await import("data:text/javascript;base64," + Buffer.from(stripTypeScriptTypes(source)).toString("base64"));
const pose = { x: 1, y: 2, z: 3, yaw: 0, pitch: 0, speed: 0, crouching: false, grounded: true, paused: false };
async function call(body, status = 200, headers = {}) {
  const response = await POST(new Request("https://game.example/api/players", { method: "POST",
    headers: { "Content-Type": "application/json", ...headers }, body: JSON.stringify(body) }));
  const data = await response.json(); assert.equal(response.status, status, data.error); return data;
}
const a = await call({ action: "join", room: "island", name: "Alpha", pose });
const b = await call({ action: "join", room: "island", name: "Beta", pose });
const auth = value => ({ id: value.id, token: value.token });
assert.equal(b.players.length, 2);
await call({ action: "state", ...auth(b), pose: { ...pose, x: 20, yaw: 90, speed: 4, paused: true } });
const moved = await call({ action: "state", ...auth(a), pose });
assert.equal(moved.players.find(p => p.id === b.id).x, 20);
assert.equal(moved.players.find(p => p.id === b.id).paused, true);
assert.ok(!("token_hash" in moved.players[0]));
const wounded = { shockPhase: 1, injury: 16, life: 0, hit: 2, armHits: [1,2], legHits: [4,2], pits: [{ bone: "LeftElbow", point: {x:0,y:.1,z:0} }] };
await call({ action: "state", ...auth(b), pose: { ...pose, damage: wounded } });
const injured = await call({ action: "state", ...auth(a), pose });
assert.deepEqual(injured.players.find(p=>p.id===b.id).damage.armHits, [1,2]);
assert.equal(injured.players.find(p=>p.id===b.id).damage.pits[0].bone, "LeftElbow");
assert.deepEqual(injured.players.find(p=>p.id===b.id).damage.legHits,[4,2]);
assert.equal(injured.players.find(p=>p.id===b.id).damage.injury,16);
assert.equal(injured.players.find(p=>p.id===b.id).damage.shockPhase,1);
await call({ action: "state", ...auth(b), pose: { ...pose, damage: {...wounded,shockPhase:3} } },400);
await call({ action: "state", ...auth(b), pose: { ...pose, damage: {...wounded, armHits: [-1,2]} } },400);
const test = await call({ action: "join", room: "test-ground", pose });
assert.equal(test.players.length, 1);
await call({ action: "state", id: b.id, token: "x".repeat(72), pose }, 401);
await call({ action: "state", ...auth(b), pose: { ...pose, x: 999999 } }, 400);
await call({ action: "join", room: "island", pose }, 403, { Origin: "https://other.example" });
await call({ action: "leave", ...auth(b) });
const departed = await call({ action: "state", ...auth(a), pose });
assert.equal(departed.players.length, 1);
sqlite.prepare("UPDATE players SET last_seen = ? WHERE id = ?").run(Date.now() - 21000, a.id);
await call({ action: "state", ...auth(a), pose }, 401);
const fresh = await call({ action: "join", room: "island", pose });
assert.ok(!fresh.players.some(p => p.id === a.id));
for (let i = 0; i < 12; i++) {
  const classmate = await call({ action: "join", room: "island", name: `Classmate ${i}`, pose });
  assert.equal(classmate.players.length, i + 2, "Players on shared school Wi-Fi can join together");
}
console.log("PASS: real server route and SQL, movement, map isolation, authorization, disconnect, expiry, shared-network players");
sqlite.close();
delete globalThis.__onlineTestEnv;
