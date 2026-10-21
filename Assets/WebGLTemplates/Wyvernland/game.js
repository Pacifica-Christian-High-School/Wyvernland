(() => {
  "use strict";
  const byId = id => document.getElementById(id);
  const play = byId("play"), canvas = byId("unity-canvas");
  let instance, starting = false;

  function fail(message) {
    window.wyvernlandBackgroundLoading?.setBootstrapLoading(false);
    window.wyvernlandBackgroundLoading?.setLoading(false);
    window.wyvernlandBackgroundLoading?.setSynchronizing(false);
    byId("welcome").hidden = true;
    byId("loading").hidden = true;
    byId("error").hidden = false;
    byId("error-message").textContent = message;
    byId("game-state").textContent = "UNABLE TO START";
  }

  if (navigator.maxTouchPoints > 0)
    byId("device-note").textContent = "Connect a keyboard and mouse to play.";

  play.addEventListener("click", async () => {
    if (starting) return;
    starting = true;
    window.wyvernlandBackgroundLoading?.setBootstrapLoading(true);
    try { await window.wyvernlandRooms.ensureRoom(); } catch (error) { fail(error.message); return; }
    const probe = document.createElement("canvas");
    const context = probe.getContext("webgl2");
    if (!context || typeof WebAssembly !== "object") {
      fail("Wyvernland needs WebGL 2 and WebAssembly. Try a current desktop browser with hardware acceleration enabled.");
      return;
    }
    context.getExtension("WEBGL_lose_context")?.loseContext();
    starting = true;
    play.disabled = true;
    byId("player-name").disabled = true;
    byId("welcome").hidden = true;
    byId("loading").hidden = false;
    byId("game-state").textContent = "LOADING WYVERNLAND";
    const build = window.wyvernlandBuild;
    build.config.showBanner = (message, type) => {
      if (type === "error") fail(String(message));
      else console.warn(message);
    };
    let dataBlobUrl;
    try {
      const manifestResponse = await fetch("game-data.json");
      if (manifestResponse.ok) {
        const manifest = await manifestResponse.json();
        if (manifest.chunks.length > 0) {
          const world = await window.wyvernlandDownload.load(manifest, (downloaded, cached) => {
            byId("progress").value = downloaded / manifest.size * 0.5;
            byId("status").textContent = `${cached ? "Reading saved game files" : "Downloading the game"}… ${Math.round(downloaded / manifest.size * 100)}%`;
          });
          dataBlobUrl = URL.createObjectURL(world);
          build.config.dataUrl = dataBlobUrl;
          build.config.cacheControl = url => url.startsWith("blob:") ? "no-store" : "must-revalidate";
        }
      } else if (manifestResponse.status !== 404) {
        throw new Error("The game download could not start. Please try again.");
      }
      await new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = build.loaderUrl;
        script.onload = resolve;
        script.onerror = () => reject(new Error("The game download failed. Check your connection and try again."));
        document.body.appendChild(script);
      });
      instance = await createUnityInstance(canvas, build.config, progress => {
        byId("progress").value = dataBlobUrl ? 0.5 + progress * 0.5 : progress;
        byId("status").textContent = progress < 0.9
          ? `Downloading the game… ${Math.round(progress * 100)}%`
          : "Starting the game…";
      });
      byId("loading").hidden = true;
      window.wyvernlandOnline?.attach(instance);
      // Unity may already be building the world. Finish only the page's stage.
      window.wyvernlandBackgroundLoading?.setBootstrapLoading(false);
      byId("game-state").textContent = "WYVERNLAND · KEYBOARD + MOUSE";
      byId("fullscreen").disabled = false;
      canvas.focus({ preventScroll: true });
      // Keep memory diagnostics on the canvas for export validation.
      const memoryTimer = setInterval(() => {
        const heap = instance?.Module?.HEAPU8;
        if (heap) canvas.dataset.heapBytes = String(heap.byteLength);
      }, 1000);
      window.addEventListener("wyvernland-return-to-website", () => clearInterval(memoryTimer), { once: true });
    } catch (error) {
      console.error(error);
      fail("The game couldn’t load. Check your connection, close other heavy tabs, and try again. " + String(error.message || error));
    } finally {
      if (dataBlobUrl) URL.revokeObjectURL(dataBlobUrl);
    }
  });

  byId("retry").addEventListener("click", () => location.reload());
  window.addEventListener("wyvernland-return-to-website", async () => {
    byId("game-state").textContent = "RETURNING TO WEBSITE";
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      if (instance) await instance.Quit();
    } finally {
      location.reload();
    }
  });
  canvas.addEventListener("contextmenu", event => event.preventDefault());
  canvas.addEventListener("keydown", event => {
    if (["Space", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Tab"].includes(event.code)) event.preventDefault();
  });
})();
