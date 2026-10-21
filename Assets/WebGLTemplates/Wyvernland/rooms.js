(() => {
  "use strict";
  let room, locked = false, worldReady = false, redirecting = false, task = Promise.resolve();
  const ownerTokens = new Map();
  function run(action) { task = task.catch(() => {}).then(action); return task; }
  const status = document.getElementById("room-status");
  const code = document.getElementById("room-code");
  const name = document.getElementById("room-name");
  const map = document.getElementById("room-map");
  const shareButton = document.getElementById("share-world");
  const shareDialog = document.getElementById("share-world-dialog");
  function ownerToken(code) {
    if (ownerTokens.has(code)) return ownerTokens.get(code);
    try { return localStorage.getItem("wyvernland-room-owner-" + code) || ""; } catch { return ""; }
  }
  function controls() {
    for (const id of ["room-name", "room-map", "room-code", "join-room"]) document.getElementById(id).disabled = locked;
    document.getElementById("create-room").disabled = locked && (!worldReady || !room?.canManage);
    if (shareButton) shareButton.hidden = !locked || !worldReady || !room?.canManage;
  }
  function inviteLink() { const url = new URL(location.href); url.searchParams.set("room", room.code); return url.href; }
  function showInvite() {
    const title = document.getElementById("share-world-title"), invite = document.getElementById("share-world-link");
    if (title) title.textContent = `${room.name} · ${room.code}`;
    if (invite) invite.value = inviteLink();
    if (shareDialog) { document.exitPointerLock?.(); shareDialog.hidden = false; document.getElementById("share-world-copy")?.focus(); }
  }
  function show(value) {
    room = value; code.value = room.code;
    name.value = room.name; map.value = room.map;
    status.textContent = `${room.name} · ${room.code} · ${room.map === "island" ? "Wyvernland" : "Test Ground"}`;
    const url = new URL(location.href); url.searchParams.set("room", room.code); history.replaceState(null, "", url);
    document.getElementById("copy-room").disabled = false;
    controls();
  }
  function redirectToMain() {
    if (redirecting) return;
    redirecting = true;
    window.wyvernlandOnline?.stop();
    document.exitPointerLock?.();
    // Replace the stale invite so Back cannot reopen the missing room.
    location.replace(new URL("/", location.href).href);
  }
  async function request(url, options) {
    const response = await fetch(url, options), data = await response.json();
    if (!response.ok) {
      if (response.status === 404) redirectToMain();
      const error = new Error(data.error || "Room unavailable"); error.status = response.status; throw error;
    } return data;
  }
  async function create() {
    if (locked && (!worldReady || !room?.canManage)) throw new Error("Only the world creator can create its room.");
    const data = await request("/api/rooms", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify(locked ? { action: "share", room: room.id, ownerToken: ownerToken(room.code), name: room.name }
        : { name: name.value || `${document.getElementById("player-name").value || "Wanderer"}'s world`, map: map.value }) });
    if (data.ownerToken) {
      ownerTokens.set(data.room.code, data.ownerToken);
      try { localStorage.setItem("wyvernland-room-owner-" + data.room.code, data.ownerToken); } catch {}
    }
    show(data.room);
    if (locked) showInvite();
  }
  async function join(value) { if (!locked) {
    const selected = value.trim().toUpperCase();
    const data = await request("/api/rooms?code=" + encodeURIComponent(selected), { headers: { "X-Wyvernland-Room-Owner": ownerToken(selected) } });
    show(data.room);
  } }
  const initialParams = new URL(location.href).searchParams;
  const initial = initialParams.has("room") ? run(() => join(initialParams.get("room"))).catch(error => {
    if (error.status === 400) redirectToMain();
    status.textContent = error.message; throw error;
  }) : Promise.resolve();
  initial.catch(() => {});
  for (const [id, action] of [["create-room", create], ["join-room", () => join(code.value)]]) {
    document.getElementById(id).addEventListener("click", async () => {
      const button = document.getElementById(id); button.disabled = true;
      try { await run(action); } catch (error) { status.textContent = error.message; } finally { controls(); }
    });
  }
  async function copyInvite() {
    if (!room) return;
    try { await navigator.clipboard.writeText(inviteLink()); status.textContent = `Invite link copied · room ${room.code}`; }
    catch { status.textContent = `Share room code ${room.code} or copy this page's address.`; }
  }
  document.getElementById("copy-room").addEventListener("click", copyInvite);
  document.getElementById("share-world-copy")?.addEventListener("click", copyInvite);
  document.getElementById("share-world-close")?.addEventListener("click", () => { shareDialog.hidden = true; });
  shareButton?.addEventListener("click", async () => {
    shareButton.disabled = true;
    try { await run(create); } catch (error) { status.textContent = error.message; }
    finally { shareButton.disabled = false; controls(); }
  });
  window.wyvernlandRooms = {
    get current() { return room; },
    redirectToMain,
    setWorldReady(value) { worldReady = Boolean(value); controls(); },
    async ensureRoom() { try { await task; await initial; } catch { if (!room) throw new Error("Join a valid room or create a new world first."); }
      if (redirecting) throw new Error("Returning to the main site…");
      if (!room) await run(create);
      locked = true; controls();
      return room;
    },
  };
})();
