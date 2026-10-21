(() => {
  "use strict";
  let instance, pose, session, timer, pending = false, generation = 0, failures = 0;
  const status = () => document.getElementById("online-status");
  const nameInput = document.getElementById("player-name");
  try { if (nameInput) nameInput.value = localStorage.getItem("wyvernland-player-name") || ""; } catch {}
  nameInput?.addEventListener("change", () => {
    try { localStorage.setItem("wyvernland-player-name", nameInput.value.trim().slice(0, 24)); } catch {}
  });
  function report(message) { if (status()) status().textContent = message; }
  function send(players) {
    if (!instance || !pose) return;
    instance.SendMessage("Wyvernland Online", "ReceivePlayers", JSON.stringify({ players }));
  }
  function reconnect(value) {
    if (!session || value?.id !== session.id || value?.token !== session.token) return;
    const previous = session;
    generation++;
    session = undefined;
    failures = 0;
    clearTimeout(timer);
    window.wyvernlandWorld.session(undefined);
    leave(previous);
    send([]);
    report("Reconnecting to room…");
    if (pose && !pending) timer = setTimeout(tick, 0);
  }
  async function tick() {
    if (!pose || pending) return;
    pending = true;
    const version = generation;
    const joining = !session;
    // Hit reports belong to the old physics-host session. Never replay them
    // while joining, or after another player has taken over room physics.
    const nextPose = { ...pose, paused: pose.paused || document.hidden };
    if (pose.damage && (joining || window.wyvernlandWorld.canReportImpacts?.() === false))
      nextPose.damage = { ...pose.damage, impacts: [] };
    const body = joining
      ? { action: "join", room: window.wyvernlandRooms.current.id, name: document.getElementById("player-name")?.value, pose: nextPose }
      : { action: "state", ...session, pose: nextPose };
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 8000);
    try {
      const response = await fetch("/api/players", { method: "POST", cache: "no-store",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify(body), signal: controller.signal });
      const data = await response.json();
      if (version !== generation) {
        if (joining && data.token) leave({ id: data.id, token: data.token });
        return;
      }
      if (!response.ok) {
        if (response.status === 404) { window.wyvernlandRooms.redirectToMain(); return; }
        if (response.status === 401 && !joining) { reconnect(body); return; }
        if (response.status === 403 && data.code === "host_changed") {
          window.wyvernlandWorld.refreshAuthority();
          report("Updating room connection…");
          return;
        }
        throw new Error(data.error || "Connection interrupted");
      }
      if (joining) { session = { id: data.id, token: data.token }; window.wyvernlandWorld.session(session); }
      failures = 0;
      const others = data.players.filter(player => player.id !== session.id);
      send(others);
      report(`${data.players.length} ${data.players.length === 1 ? "player" : "players"} · room ${window.wyvernlandRooms.current.code}`);
      document.getElementById("unity-canvas").dataset.remotePlayers = String(others.length);
    } catch (error) {
      if (version !== generation) return;
      failures++;
      if (failures >= 3) send([]);
      report(failures > 1 ? `${error.message}. Retrying…` : "Connecting to other players…");
    } finally {
      clearTimeout(timeout);
      pending = false;
      if (pose) timer = setTimeout(tick, failures ? Math.min(5000, failures * 1000) : document.hidden ? 2000 : 250);
    }
  }
  function leave(value) {
    if (!value) return;
    fetch("/api/players", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "leave", ...value }), keepalive: true }).catch(() => {});
  }
  function stop() {
    generation++;
    clearTimeout(timer);
    leave(session);
    session = undefined;
    window.wyvernlandWorld.session(undefined);
    pose = undefined;
    failures = 0;
    report("Create or join a room to play together");
  }
  window.wyvernlandOnline = {
    attach(value) { instance = value; window.wyvernlandWorld.attach(value); },
    publish(value) {
      const next = JSON.parse(value);
      if (pose && next.room !== pose.room) stop();
      const first = !pose;
      pose = next;
      if (first && !pending) tick();
    },
    stop,
    reconnect,
  };
  window.addEventListener("pagehide", stop);
  window.addEventListener("wyvernland-return-to-website", stop);
})();
