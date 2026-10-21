import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
const source = await readFile(new URL("../Assets/WebGLTemplates/Wyvernland/rooms.js", import.meta.url), "utf8");
const token = "a".repeat(72), stored = new Map();
const room = { id: "own-world", code: "MYWORLD1", name: "My current world", map: "test-ground" };
function fixture(storage, invited = false) {
  const elements = new Map(), calls = [], copies = [], location = { href: "https://game.example/" + (invited ? "?room=" + room.code : "") };
  for (const id of ["room-status", "room-code", "room-name", "room-map", "copy-room", "player-name", "create-room", "join-room",
    "share-world", "share-world-dialog", "share-world-title", "share-world-link", "share-world-copy", "share-world-close"])
    elements.set(id, { value: id === "room-map" ? "test-ground" : "", hidden: id.startsWith("share-world"), disabled: false,
      events: {}, focus() {}, addEventListener(event, callback) { this.events[event] = callback; } });
  const window = {};
  vm.runInNewContext(source, { window, URL, Map, location,
    localStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) },
    document: { getElementById: id => elements.get(id), exitPointerLock() {} },
    history: { replaceState: (_, __, url) => location.href = url.href },
    navigator: { clipboard: { writeText: async value => copies.push(value) } },
    fetch: async (url, options) => {
      const body = options?.body ? JSON.parse(options.body) : null; calls.push({ url, options, body });
      const canManage = body ? body.action !== "share" || body.ownerToken === token : options.headers["X-Wyvernland-Room-Owner"] === token;
      return { ok: true, json: async () => ({ room: { ...room, canManage }, ...(body && !body.action ? { ownerToken: token } : {}) }) };
    } });
  return { bridge: window.wyvernlandRooms, elements, calls, copies, location };
}
const creator = fixture(stored);
await creator.bridge.ensureRoom();
assert.equal(creator.calls.length, 1);
assert.equal(creator.elements.get("create-room").disabled, true, "Room controls wait for the world to finish loading");
creator.bridge.setWorldReady(true);
assert.equal(creator.elements.get("share-world").hidden, false);
assert.equal(creator.elements.get("create-room").disabled, false, "The creator can create a room after entering the world");
await creator.elements.get("share-world").events.click();
assert.equal(creator.calls[1].body.action, "share");
assert.equal(creator.calls[1].body.room, room.id);
assert.equal(creator.bridge.current.id, room.id, "Sharing must keep the existing world and its live session");
assert.equal(creator.elements.get("share-world-dialog").hidden, false);
await creator.elements.get("share-world-copy").events.click();
assert.match(creator.copies[0], /room=MYWORLD1/);
assert.equal(creator.copies[0].includes(token), false, "Invite links never disclose creator credentials");
creator.elements.get("share-world-close").events.click();
assert.equal(creator.elements.get("share-world-dialog").hidden, true);
const returning = fixture(stored, true);
await returning.bridge.ensureRoom(); returning.bridge.setWorldReady(true);
assert.equal(returning.elements.get("share-world").hidden, false, "Returning creators retain access in their browser");
assert.equal(returning.calls.length, 1, "Returning to a world must not create a replacement room");
const guest = fixture(new Map(), true);
await guest.bridge.ensureRoom(); guest.bridge.setWorldReady(true);
assert.equal(guest.elements.get("share-world").hidden, true);
assert.equal(guest.elements.get("create-room").disabled, true);
await guest.elements.get("create-room").events.click();
assert.equal(guest.calls.length, 1, "Guests cannot invoke room creation from somebody else's world");
assert.match(guest.elements.get("room-status").textContent, /Only the world creator/);
console.log("PASS: in-world creator sharing, current-world preservation, invite privacy, returning owners and guest restrictions");
