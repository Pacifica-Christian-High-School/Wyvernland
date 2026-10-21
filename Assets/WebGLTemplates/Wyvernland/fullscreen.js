(() => {
  "use strict";
  const shell = document.getElementById("game-shell");
  const canvas = document.getElementById("unity-canvas");
  const button = document.getElementById("fullscreen");
  const hint = document.getElementById("fullscreen-exit-hint");
  const progress = document.getElementById("fullscreen-exit-progress");
  const holdMilliseconds = 3000;
  let heldSince, holdTimer, hintTimer, generation = 0, suspended = false;

  const isGameFullscreen = () => Boolean(document.fullscreenElement &&
    (document.fullscreenElement === shell || shell.contains(document.fullscreenElement)));
  function cancelHold() {
    clearInterval(holdTimer); clearTimeout(hintTimer);
    holdTimer = hintTimer = heldSince = undefined;
    hint.hidden = true;
    progress.value = 0;
  }
  function unlock() { navigator.keyboard?.unlock?.(); }
  async function captureEscape(version) {
    if (!navigator.keyboard?.lock) return;
    try {
      await navigator.keyboard.lock(["Escape"]);
      // A permission prompt can outlive fullscreen. Never retain its lock
      // after the player has left, or request fullscreen again automatically.
      if (version !== generation && (!isGameFullscreen() || suspended)) unlock();
    } catch (error) {
      console.info("Escape keyboard capture unavailable; browser exit controls apply.", error);
    }
  }
  async function exit() {
    cancelHold();
    if (!isGameFullscreen()) return;
    try { await document.exitFullscreen(); }
    catch (error) { console.warn("Could not exit fullscreen; use the browser's fullscreen controls.", error); }
  }
  function tick() {
    if (heldSince === undefined || !isGameFullscreen() || document.hidden || suspended) { cancelHold(); return; }
    progress.value = Math.min(holdMilliseconds, performance.now() - heldSince);
    if (progress.value >= holdMilliseconds) void exit();
  }

  button.addEventListener("click", async () => {
    if (button.disabled) return;
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else {
        try { await shell.requestFullscreen({ keyboardLock: "browser" }); }
        catch (error) {
          if (error.name !== "NotSupportedError") throw error;
          await shell.requestFullscreen();
        }
      }
      canvas.focus({ preventScroll: true });
    } catch (error) { console.warn("Fullscreen unavailable", error); }
  });
  document.addEventListener("fullscreenchange", () => {
    const version = ++generation;
    cancelHold();
    const fullscreen = isGameFullscreen();
    button.innerHTML = fullscreen ? 'Exit fullscreen <span aria-hidden="true">⛶</span>' : 'Fullscreen <span aria-hidden="true">⛶</span>';
    button.setAttribute("aria-label", fullscreen ? "Exit fullscreen" : "Enter fullscreen");
    if (fullscreen) {
      hint.hidden = false;
      hintTimer = setTimeout(() => { if (heldSince === undefined) hint.hidden = true; }, 5000);
      void captureEscape(version);
    } else unlock();
  });
  // Capture before Unity's listeners, but allow the first press and release to
  // reach the game so Escape still opens/closes its pause menu.
  window.addEventListener("keydown", event => {
    if (event.code !== "Escape" || !isGameFullscreen() || suspended || event.ctrlKey || event.altKey || event.metaKey) return;
    event.preventDefault();
    if (heldSince !== undefined || event.repeat || document.hidden) return;
    heldSince = performance.now();
    clearTimeout(hintTimer); hint.hidden = false; progress.value = 0;
    holdTimer = setInterval(tick, 25);
  }, true);
  window.addEventListener("keyup", event => {
    if (event.code === "Escape") cancelHold();
  }, true);
  window.addEventListener("blur", cancelHold);
  document.addEventListener("visibilitychange", () => { if (document.hidden) cancelHold(); });
  window.addEventListener("pagehide", () => { suspended = true; generation++; cancelHold(); unlock(); });
  window.addEventListener("pageshow", () => { suspended = false; });
  window.addEventListener("wyvernland-return-to-website", () => { suspended = true; generation++; cancelHold(); unlock(); });
})();
