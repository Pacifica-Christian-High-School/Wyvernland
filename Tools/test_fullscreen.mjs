import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/fullscreen.js", import.meta.url), "utf8");

function fixture({ keyboard = "supported", fullscreen = "supported", deferred = false } = {}) {
  let now = 0, nextId = 0, exits = 0, locks = 0, unlocks = 0, releaseLock;
  const tasks = new Map(), requests = [], capturedKeys = [];
  function target() {
    const listeners = new Map();
    return { hidden: true, value: 0, disabled: false, attributes: {},
      addEventListener(name, callback) { if (!listeners.has(name)) listeners.set(name, []); listeners.get(name).push(callback); },
      async emit(name, event = {}) { for (const callback of listeners.get(name) || []) await callback(event); },
      setAttribute(name, value) { this.attributes[name] = value; }, focus() {},
    };
  }
  const shell = target(), canvas = target(), button = target(), hint = target(), progress = target();
  shell.contains = value => value === canvas;
  const elements = { "game-shell": shell, "unity-canvas": canvas, fullscreen: button,
    "fullscreen-exit-hint": hint, "fullscreen-exit-progress": progress };
  const window = target(), document = Object.assign(target(), { fullscreenElement: null, hidden: false, getElementById: id => elements[id] });
  document.exitFullscreen = async () => { exits++; document.fullscreenElement = null; await document.emit("fullscreenchange"); };
  shell.requestFullscreen = async options => {
    requests.push(options);
    if (fullscreen === "denied") throw new Error("Permission denied");
    if (fullscreen === "legacy" && options) { const error = new Error("Unsupported keyboard mode"); error.name = "NotSupportedError"; throw error; }
    document.fullscreenElement = shell; await document.emit("fullscreenchange");
  };
  const navigator = {};
  if (keyboard !== "missing") navigator.keyboard = {
    async lock(keys) { locks++; capturedKeys.push(...keys);
      if (keyboard === "denied") throw new Error("Keyboard lock denied");
      if (deferred) await new Promise(resolve => releaseLock = resolve);
    }, unlock() { unlocks++; },
  };
  const timer = (callback, delay, interval = false) => { const id = ++nextId; tasks.set(id, { callback, due: now + delay, period: interval ? delay : 0 }); return id; };
  vm.runInNewContext(source, { window, document, navigator, performance: { now: () => now },
    console: { info() {}, warn() {} },
    setInterval: (callback, delay) => timer(callback, delay, true), clearInterval: id => tasks.delete(id),
    setTimeout: (callback, delay) => timer(callback, delay), clearTimeout: id => tasks.delete(id) });
  async function advance(delta) {
    const end = now + delta;
    while (true) {
      const next = [...tasks.entries()].filter(([,task]) => task.due <= end).sort((a,b) => a[1].due - b[1].due)[0];
      if (!next) break;
      const [id, task] = next; now = task.due;
      if (task.period) task.due += task.period; else tasks.delete(id);
      task.callback(); await Promise.resolve();
    }
    now = end; await Promise.resolve();
  }
  async function key(type = "keydown", extra = {}) {
    const event = { code: "Escape", repeat: false, prevented: false, stopped: false,
      preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; }, ...extra };
    await window.emit(type, event); return event;
  }
  return { shell, canvas, button, hint, progress, document, window, requests, capturedKeys, advance, key,
    enter: () => button.emit("click"), async releaseLock() { releaseLock(); for (let i=0;i<10;i++) await Promise.resolve(); },
    get exits() { return exits; }, get locks() { return locks; }, get unlocks() { return unlocks; }, get timerCount() { return tasks.size; } };
}

{
  const f = fixture(); await f.enter();
  assert.equal(f.requests[0].keyboardLock, "browser"); assert.deepEqual(f.capturedKeys, ["Escape"]);
  const press = await f.key(); assert.equal(press.prevented, true); assert.equal(press.stopped, false, "The game must still receive Escape for its menu");
  await f.advance(250); await f.key("keyup"); await f.advance(4000);
  assert.equal(f.exits, 0, "A short press cannot trigger the website's fullscreen exit"); assert.equal(f.hint.hidden, true);
  await f.key(); await f.advance(1000); await f.key("keydown", { repeat: true });
  await f.advance(1999); assert.equal(f.exits, 0); assert.ok(f.progress.value > 2900);
  await f.advance(1); assert.equal(f.exits, 1, "Exit is requested at exactly three seconds, without repeats extending the deadline");
  assert.equal(f.unlocks, 1); assert.equal(f.timerCount, 0); assert.equal(f.hint.hidden, true);
  const outside = await f.key(); assert.equal(outside.prevented, false);
}
{
  const f = fixture(); await f.enter(); await f.key(); await f.advance(2999); await f.key("keyup");
  await f.key(); await f.advance(2999); assert.equal(f.exits, 0); await f.advance(1); assert.equal(f.exits, 1, "Releasing resets the entire hold");
}
for (const cancel of ["blur", "hidden", "pagehide", "wyvernland-return-to-website"]) {
  const f = fixture(); await f.enter(); await f.key(); await f.advance(2500);
  if (cancel === "hidden") { f.document.hidden = true; await f.document.emit("visibilitychange"); }
  else await f.window.emit(cancel);
  await f.advance(4000); assert.equal(f.exits, 0, cancel + " must cancel a partially held key"); assert.equal(f.timerCount, 0);
}
for (const keyboard of ["missing", "denied"]) {
  const f = fixture({ keyboard }); await f.enter(); assert.equal(f.document.fullscreenElement, f.shell);
  await f.enter(); assert.equal(f.exits, 1, "Explicit exit button remains available when keyboard capture is unavailable");
}
{
  const f = fixture({ fullscreen: "legacy" }); await f.enter();
  assert.equal(f.requests.length, 2); assert.equal(f.document.fullscreenElement, f.shell);
}
{
  const f = fixture({ fullscreen: "denied" }); await f.enter();
  assert.equal(f.locks, 0); assert.equal(f.timerCount, 0); assert.equal(f.document.fullscreenElement, null);
}
{
  const f = fixture({ deferred: true }); await f.enter(); await f.enter();
  await f.releaseLock();
  assert.ok(f.unlocks >= 2, "A late keyboard permission result cannot retain capture after exit");
}
{
  const f = fixture(); await f.enter(); await f.key("keydown", { repeat: true }); await f.advance(4000);
  assert.equal(f.exits, 0, "Orphan key repeats after lost focus cannot start an exit hold");
  f.document.fullscreenElement = {}; await f.document.emit("fullscreenchange");
  assert.equal((await f.key()).prevented, false, "Only the game's fullscreen element captures Escape");
}
console.log("PASS: three-second continuous hold, quick Escape preserves the menu, repeat/release/blur cleanup, keyboard-lock fallback, explicit exits, and late permission cleanup (browser emergency exits remain browser-controlled)");
