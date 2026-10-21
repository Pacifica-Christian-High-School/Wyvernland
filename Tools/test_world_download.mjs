import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/download.js", import.meta.url), "utf8");
const paths = [0, 1, 2].map(i => `Build/world-${String(i + 1).repeat(16)}-0${i}.bin`);
const manifest = { size: 9, chunks: paths }, stored = new Map();
let requests = 0, active = 0, maximum = 0;
const window = {}, origin = "https://game.example";
const cache = { async keys() { return [...stored.keys()].map(url => ({ url })); },
  async match(url) { const bytes = stored.get(url); return bytes ? new Response(bytes) : undefined; },
  async put(url, response) { stored.set(url, await response.arrayBuffer()); },
  async delete(key) { return stored.delete(typeof key === "string" ? key : key.url); } };
const fetch = async url => {
  requests++; active++; maximum = Math.max(maximum, active);
  const index = paths.indexOf(new URL(url).pathname.slice(1));
  await new Promise(resolve => setTimeout(resolve, index === 0 ? 20 : 2)); active--;
  assert.ok(index >= 0); return new Response(Uint8Array.of(index, index, index));
};
vm.runInNewContext(source, { window, URL, Blob, Set, location: { href: origin + "/", origin },
  caches: { open: async () => cache }, fetch });
let last = 0;
const first = await window.wyvernlandDownload.load(manifest, bytes => { assert.ok(bytes >= last); last = bytes; });
assert.deepEqual([...new Uint8Array(await first.arrayBuffer())], [0,0,0,1,1,1,2,2,2], "Parallel chunks must assemble in manifest order");
assert.equal(maximum, 2); assert.equal(requests, 3); assert.equal(stored.size, 3);
await window.wyvernlandDownload.load(manifest, () => {});
assert.equal(requests, 3, "An unchanged world must load from persistent cache without network requests");
stored.set(origin + "/Build/world-ffffffffffffffff-00.bin", new ArrayBuffer(1));
await window.wyvernlandDownload.load(manifest, () => {});
assert.equal(stored.size, 3, "Old world cache entries must be removed");
await assert.rejects(() => window.wyvernlandDownload.load({ size: 1, chunks: ["https://unrelated.example/Build/world-1111111111111111-00.bin"] }, () => {}), /Invalid world file/);
stored.set(origin + "/" + paths[0], Uint8Array.of(9).buffer);
await assert.rejects(() => window.wyvernlandDownload.load(manifest, () => {}), /incomplete/);
assert.equal(stored.size, 0, "An incomplete cached download must be discarded for a clean retry");
const fallback = {};
vm.runInNewContext(source, { window: fallback, URL, Blob, Set, location: { href: origin + "/", origin },
  caches: { open: async () => { throw new Error("Storage unavailable"); } }, fetch });
const uncached = await fallback.wyvernlandDownload.load(manifest, () => {});
assert.equal(uncached.size, 9);
const single = await fallback.wyvernlandDownload.load({ size: 3, chunks: [paths[0]] }, () => {});
assert.deepEqual([...new Uint8Array(await single.arrayBuffer())], [0,0,0], "Small exports must load from a single hashed chunk");
console.log("PASS: bounded parallel download, ordered assembly, persistent reuse, obsolete-cache cleanup, incomplete-cache recovery, storage-unavailable fallback");
