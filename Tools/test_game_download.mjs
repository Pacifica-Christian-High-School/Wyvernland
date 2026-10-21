import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
const template = new URL("../Assets/WebGLTemplates/Wyvernland/", import.meta.url);
const download = await readFile(new URL("download.js", template), "utf8");
const game = await readFile(new URL("game.js", template), "utf8");
for (const count of [1, 3]) {
  const elements = new Map(), chunks = Array.from({ length: count }, (_, index) => `Build/world-${String(index + 1).repeat(16)}-0${index}.bin`);
  let started = 0, blob, revoked = false, downloads = 0, bootLoading = false, worldLoading = false;
  const element = id => {
    if (!elements.has(id)) elements.set(id, { hidden: true, dataset: {}, events: {}, focus() {},
      addEventListener(name, callback) { this.events[name] = callback; } });
    return elements.get(id);
  };
  class BrowserURL extends URL {
    static createObjectURL(value) { blob = value; return "blob:fixture-world"; }
    static revokeObjectURL(url) { assert.equal(url, "blob:fixture-world"); revoked = true; }
  }
  const window = { wyvernlandRooms: { ensureRoom: async () => {} },
    wyvernlandBackgroundLoading: { setBootstrapLoading: value => bootLoading = value, setLoading: value => worldLoading = value },
    wyvernlandBuild: { config: { dataUrl: "Build/removed-original.data.unityweb" }, loaderUrl: "Build/fixture.loader.js" },
    addEventListener() {} };
  const sandbox = { window, navigator: { maxTouchPoints: 0 }, URL: BrowserURL, Blob, Set, WebAssembly, console,
    location: { href: "https://game.example/", origin: "https://game.example" },
    caches: { open: async () => { throw new Error("Storage unavailable"); } },
    fetch: async path => {
      if (path === "game-data.json") return Response.json({ size: count * 3, chunks });
      downloads++; assert.ok(chunks.includes(new URL(path).pathname.slice(1)));
      return new Response(Uint8Array.of(1,2,3));
    },
    document: { getElementById: element, addEventListener() {},
      createElement: name => name === "canvas" ? { getContext: () => ({ getExtension: () => ({ loseContext() {} }) }) } : {},
      body: { appendChild(script) { queueMicrotask(script.onload); } } },
    setInterval() { return 1; }, clearInterval() {},
    createUnityInstance: async (_canvas, config, progress) => {
      started++; assert.equal(config.dataUrl, "blob:fixture-world");
      assert.equal(bootLoading, true, "Page startup must be driven in hidden tabs before Unity resolves");
      window.wyvernlandBackgroundLoading.setLoading(true);
      assert.equal(config.cacheControl(config.dataUrl), "no-store");
      progress(1); return { Module: {} };
    } };
  vm.createContext(sandbox); vm.runInContext(download, sandbox); vm.runInContext(game, sandbox);
  await element("play").events.click();
  assert.equal(started, 1, "Prepared downloads must reach Unity startup");
  assert.equal(downloads, count); assert.equal(blob.size, count * 3); assert.ok(revoked);
  assert.equal(element("loading").hidden, true);
  assert.equal(bootLoading, false);
  assert.equal(worldLoading, true, "Page startup completion cannot cancel an in-progress Unity loading coroutine");
}
console.log("PASS: webpage starts Unity from one or multiple prepared chunks without duplicate Blob caching");
