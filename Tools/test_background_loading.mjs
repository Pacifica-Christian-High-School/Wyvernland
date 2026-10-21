import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/background-loading.js", import.meta.url), "utf8");
const native = new Map(), events = {}, workers = [];
let nativeId = 0, revoked = 0;
const window = { requestAnimationFrame: cb => { native.set(++nativeId, cb); return nativeId; },
  cancelAnimationFrame: id => native.delete(id), addEventListener: (event, cb) => events[event] = cb };
const document = { hidden: false, addEventListener: (event, cb) => events[event] = cb };
class Worker { constructor() { workers.push(this); } terminate() { this.stopped = true; } }
const context = { window, document, Worker, Blob, performance: { now: () => 100 },
  URL: { createObjectURL: () => "blob:clock", revokeObjectURL: () => revoked++ },
  setInterval: () => 1, clearInterval: () => {}, Map };
vm.runInNewContext(source, context);
window.wyvernlandBackgroundLoading.setBootstrapLoading(true);
window.wyvernlandBackgroundLoading.setLoading(true);
let frames = 0;
const cancelled = window.requestAnimationFrame(() => frames += 100);
window.cancelAnimationFrame(cancelled);
window.requestAnimationFrame(() => { frames++; window.requestAnimationFrame(() => frames++); });
document.hidden = true; events.visibilitychange();
assert.equal(workers.length, 1);
workers[0].onmessage(); assert.equal(frames, 1, "Hidden loading advances one pending frame");
workers[0].onmessage(); assert.equal(frames, 2, "Yielded loading continues on later worker ticks");
window.requestAnimationFrame(() => frames++);
window.wyvernlandBackgroundLoading.setBootstrapLoading(false);
assert.notEqual(workers[0].stopped, true, "Resolving createUnityInstance cannot stop Unity's active world loading");
window.wyvernlandBackgroundLoading.setSynchronizing(true);
window.wyvernlandBackgroundLoading.setLoading(false);
assert.notEqual(workers[0].stopped, true, "The clock must bridge world preparation into room synchronization");
window.wyvernlandBackgroundLoading.setSynchronizing(false);
assert.equal(workers[0].stopped, true); assert.equal(revoked, 1);
workers[0].onmessage(); assert.equal(frames, 2, "Gameplay does not run via the loading clock");
document.hidden = false; events.visibilitychange();
for (const cb of [...native.values()]) cb(200);
assert.equal(frames, 3, "Visible frames resume without duplicate callbacks");
window.wyvernlandBackgroundLoading.setLoading(true);
document.hidden = true; events.visibilitychange();
events.pagehide(); assert.equal(workers[1].stopped, true);
window.wyvernlandBackgroundLoading.setHosting(true);
assert.equal(workers.length, 2, "Late Unity callbacks must not restart a closed page's clock");
window.wyvernlandBackgroundLoading.setLoading(false);
events.pageshow();
window.requestAnimationFrame(() => frames++);
workers[2].onmessage(); assert.equal(frames, 4, "Room host keeps advancing shared physics in a hidden tab");
window.wyvernlandBackgroundLoading.setHosting(false); assert.equal(workers[2].stopped, true);
console.log("PASS: loading advances in hidden tab, cancellation, visible resume, clock stops after load and exit");
