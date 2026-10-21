(() => {
  "use strict";
  let instance, session, baseline, timer, busy = false, initial = true, prepared = false, active = false;
  let after = 0, cursor = "", until, host = "", generation = 0, inFlight;
  const objects = new Map(), meshes = new Map(), meshCache = new Map(), operations = [], acknowledgements = new Set();
  const send = data => instance?.SendMessage("Wyvernland Shared World", "ReceiveWorld", JSON.stringify({ self: session?.id || "", host, ready: active, ...data }));
  async function api(body, value = session) {
    if (!value) throw new Error("Connecting to room");
    const version = generation;
    const response = await fetch("/api/world", { method: "POST", cache: "no-store", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ ...body, ...value }), signal: AbortSignal.timeout(15000) });
    const result = await response.json();
    if (version !== generation) throw new Error("Room session changed");
    if (!response.ok) {
      if (response.status === 401) {
        window.wyvernlandOnline?.reconnect(value);
        // Stop retrying rejected credentials even if presence has already stopped.
        if (version === generation) window.wyvernlandWorld.session(undefined);
      }
      const error = new Error(result.error || "World unavailable"); error.status = response.status; throw error;
    } return result;
  }
  async function loadMesh(hash) {
    if (meshCache.has(hash)) return meshCache.get(hash);
    let data = "", part = 0, total = 1;
    while (part < total) {
      const result = await api({ action: "mesh-read", hash, part });
      if (!result.parts?.length) throw new Error("Waiting for shared object geometry");
      for (const chunk of result.parts) {
        if (chunk.part !== part || chunk.total > 128) throw new Error("Invalid shared object geometry");
        data += chunk.data; total = chunk.total; part++;
      }
    }
    meshCache.set(hash, data); if (meshCache.size > 256) meshCache.delete(meshCache.keys().next().value); return data;
  }
  async function uploadMesh(hash, data) {
    const total = Math.ceil(data.length / 65536);
    for (let offset = 0; offset < total; offset += 4) {
      const parts = Array.from({ length: Math.min(4, total - offset) }, (_, i) => ({ part: offset + i, total, data: data.slice((offset + i) * 65536, (offset + i + 1) * 65536) }));
      await api({ action: "mesh-write", hash, parts });
    }
  }
  async function tick() {
    if (busy || !session || !baseline || !instance) return;
    busy = true; const version = generation;
    try {
      if (!prepared) { await api({ action: "ready", baseline }); prepared = true; }
      if (active && operations.length) {
        const operation = operations[0]; inFlight = operation; let result;
        try { result = await api(operation); } catch (error) {
          // A room can briefly reject writes while it elects a new host or
          // activates a player. Keep the original claim queued across that gap.
          if ([400, 403].includes(error.status)) {
            operations.shift(); if (operation.action === "release") send({ releases: [{ id: operation.object, releaseId: operation.state.releaseId, accepted: false }] }); if (operation.action === "claim") send({ claims: [{ id: operation.object, owned: false }] });
            console.warn("Room interaction declined", operation.action, error.message);
          } else throw error;
        }
        if (version !== generation) return;
        if (result) {
          if (operation.action === "claim") send({ claims: [{ id: operation.object, owned: result.owned }] });
          if (operation.action === "release") send({ releases: [{ id: operation.object, releaseId: operation.state.releaseId, accepted: result.accepted, revision: result.revision }] });
          operations.shift();
        }
        inFlight = undefined;
      }
      if (active && host === session.id && (objects.size || acknowledgements.size)) {
        // Commit the held replacement before its old source is released/deleted.
        const selected = [...objects.values()].filter(value => !value.gripReplacement || !objects.has(value.gripReplacement)).slice(0, 32);
        const batch = selected.map(value => {
          if (!value.held || !value.gripSource) return value;
          let source = value.gripSource;
          for (let depth = 0; depth < 32; depth++) {
            const ancestor = objects.get(source);
            if (!ancestor?.gripSource || ancestor.gripSource === source) break;
            source = ancestor.gripSource;
          }
          return source === value.gripSource ? value : { ...value, gripSource: source };
        });
        for (const value of batch) if (value.mesh && meshes.has(value.mesh)) { await uploadMesh(value.mesh, meshes.get(value.mesh)); meshes.delete(value.mesh); }
        const ack = [...acknowledgements].slice(0, 8);
        const result = await api({ action: "commit", objects: batch, ack });
        if (!result.accepted) throw new Error("Room host changed");
        if (result.rejected?.length) console.warn("Skipped invalid world samples", result.rejected);
        for (const value of selected) if (objects.get(value.id) === value) objects.delete(value.id);
        for (const id of ack) acknowledgements.delete(id);
      }
      const result = await api({ action: "read", after, cursor, ...(until === undefined ? {} : { until }) });
      if (version !== generation) return;
      const nextHost = result.room.host_id || "";
      if (nextHost !== session.id) { objects.clear(); meshes.clear(); acknowledgements.clear(); }
      host = nextHost;
      const needed = [...new Set(result.objects.map(value => value.mesh).filter(Boolean))];
      const incomingMeshes = [];
      for (const hash of needed) incomingMeshes.push({ hash, data: await loadMesh(hash) });
      if (version !== generation) return;
      send({ playerCount: result.playerCount, objects: result.objects, meshes: incomingMeshes, commands: result.commands });
      after = result.after; cursor = result.cursor; until = result.more ? result.until : undefined;
      if (initial && !result.more) {
        const result = await api({ action: "activate", baseline });
        if (version !== generation) return;
        host = result.room.host_id || ""; initial = false; active = true; send({});
        window.wyvernlandBackgroundLoading?.setSynchronizing(false);
        window.wyvernlandRooms?.setWorldReady?.(true);
      } else if (!initial && !active) {
        active = true; send({});
        window.wyvernlandBackgroundLoading?.setSynchronizing(false);
        window.wyvernlandRooms?.setWorldReady?.(true);
      }
    } catch (error) {
      if (version !== generation) return;
      if (error.status === 404) { window.wyvernlandRooms.redirectToMain(); return; }
      active = false; host = ""; send({ error: error.message });
      window.wyvernlandBackgroundLoading?.setSynchronizing(true);
      window.wyvernlandRooms?.setWorldReady?.(false);
      const status = document.getElementById("online-status"); if (status) status.textContent = `${error.message}. Reconnecting…`;
    } finally { inFlight = undefined; busy = false; if (session && baseline) timer = setTimeout(tick, until !== undefined || active && operations.length ? 0 : active ? 150 : 1500); }
  }
  window.wyvernlandWorld = {
    canReportImpacts() { return active && host === session?.id; },
    refreshAuthority() { active = false; host = ""; send({}); clearTimeout(timer); tick(); },
    attach(value) { instance = value; tick(); },
    session(value) { generation++; session = value; after = 0; cursor = ""; until = undefined; initial = true; prepared = active = false; host = "";
      window.wyvernlandBackgroundLoading?.setSynchronizing(Boolean(value || baseline));
      window.wyvernlandRooms?.setWorldReady?.(false);
      objects.clear(); meshes.clear(); operations.length = 0; acknowledgements.clear(); clearTimeout(timer); if (!value) send({}); else tick(); },
    ready(value) { baseline = value; window.wyvernlandBackgroundLoading?.setSynchronizing(true); tick(); },
    publish(value) { const batch = JSON.parse(value);
      if (active && host === session?.id) {
        for (const state of batch.objects || []) objects.set(state.id, state);
        for (const mesh of batch.meshes || []) meshes.set(mesh.hash, mesh.data);
        for (const id of batch.ack || []) acknowledgements.add(id);
      }
    },
    request(value) { const operation = JSON.parse(value);
      if (!active) return;
      if (operation.action === "claim") operation.state.mesh = "";
      if (operation.action === "release") {
        // Retire unsent hand poses; an in-flight request finishes before release.
        for (let i = operations.length - 1; i >= 0; i--)
          if (operations[i] !== inFlight && operations[i].action === "held" && operations[i].object === operation.object) operations.splice(i, 1);
      }
      if (operation.action === "held") {
        for (let i = operations.length - 1; i >= 0; i--) {
          const prior = operations[i]; if (prior.object !== operation.object) continue;
          if (["release", "claim", "unclaim"].includes(prior.action)) break;
          if (prior.action === "held" && prior !== inFlight) { operations[i] = operation; return; }
        }
      }
      if (operations.length < 256) operations.push(operation);
    },
  };
})();
