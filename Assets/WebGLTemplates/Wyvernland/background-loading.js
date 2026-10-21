(() => {
  "use strict";
  // Browsers suspend animation frames in hidden tabs. During loading, a worker
  // advances pending frames so Unity's yielding world preparation keeps running.
  const request = window.requestAnimationFrame.bind(window);
  const cancel = window.cancelAnimationFrame.bind(window);
  const pending = new Map();
  let nextId = 0, loading = false, bootstrapLoading = false, synchronizing = false, hosting = false, suspended = false, worker, workerUrl, fallback;
  const needsClock = () => !suspended && document.hidden && (loading || bootstrapLoading || synchronizing || hosting);
  function schedule(id, entry) {
    entry.nativeId = request(timestamp => {
      if (pending.get(id) !== entry) return;
      pending.delete(id);
      entry.callback(timestamp);
    });
  }
  window.requestAnimationFrame = callback => {
    const id = ++nextId, entry = { callback, nativeId: null };
    pending.set(id, entry);
    schedule(id, entry);
    return id;
  };
  window.cancelAnimationFrame = id => {
    const entry = pending.get(id);
    if (!entry) return;
    cancel(entry.nativeId);
    pending.delete(id);
  };
  function tick() {
    if (!needsClock()) return;
    for (const [id, entry] of [...pending]) {
      if (pending.get(id) !== entry) continue;
      cancel(entry.nativeId);
      pending.delete(id);
      entry.callback(performance.now());
    }
  }
  function stopClock() {
    worker?.terminate(); worker = undefined;
    if (workerUrl) URL.revokeObjectURL(workerUrl);
    workerUrl = undefined;
    clearInterval(fallback); fallback = undefined;
  }
  function refreshClock() {
    if (!needsClock()) { stopClock(); return; }
    if (worker || fallback) return;
    try {
      workerUrl = URL.createObjectURL(new Blob(["setInterval(() => postMessage('tick'), 33);"], { type: "text/javascript" }));
      worker = new Worker(workerUrl);
      worker.onmessage = tick;
      worker.onerror = () => {
        stopClock(); if (needsClock()) fallback = setInterval(tick, 100);
      };
    } catch {
      stopClock(); fallback = setInterval(tick, 100);
    }
  }
  window.wyvernlandBackgroundLoading = {
    setLoading(value) { loading = Boolean(value); refreshClock(); },
    setBootstrapLoading(value) { bootstrapLoading = Boolean(value); refreshClock(); },
    setSynchronizing(value) { synchronizing = Boolean(value); refreshClock(); },
    setHosting(value) { hosting = Boolean(value); refreshClock(); },
  };
  document.addEventListener("visibilitychange", () => {
    refreshClock();
    if (!document.hidden) for (const [id, entry] of pending) {
      cancel(entry.nativeId); schedule(id, entry);
    }
  });
  window.addEventListener("pagehide", () => { suspended = true; stopClock(); });
  window.addEventListener("pageshow", () => { suspended = false; refreshClock(); });
  window.addEventListener("wyvernland-return-to-website", () => {
    suspended = true; loading = bootstrapLoading = synchronizing = hosting = false; stopClock();
  });
})();
