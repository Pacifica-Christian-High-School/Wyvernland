(() => {
  "use strict";
  const cacheName = "wyvernland-world-chunks-v1";
  async function load(manifest, progress) {
    if (!Number.isSafeInteger(manifest.size) || manifest.size < 1 || !Array.isArray(manifest.chunks) ||
        !manifest.chunks.length || manifest.chunks.length > 128) throw new Error("Invalid world download.");
    const urls = manifest.chunks.map(path => {
      const url = new URL(path, location.href);
      if (url.origin !== location.origin || !/^\/Build\/world-[a-f0-9]{16}-[0-9]{2}\.bin$/.test(url.pathname))
        throw new Error("Invalid world file.");
      return url.href;
    });
    let cache;
    try {
      cache = await caches.open(cacheName);
      const current = new Set(urls);
      await Promise.all((await cache.keys()).filter(key => !current.has(key.url)).map(key => cache.delete(key)));
    } catch { cache = undefined; }
    const pieces = new Array(urls.length), writes = [];
    let next = 0, downloaded = 0;
    async function worker() {
      while (next < urls.length) {
        const index = next++, url = urls[index];
        let response, cached = false;
        if (cache) { try { response = await cache.match(url); cached = !!response; } catch {} }
        if (!response) response = await fetch(url);
        if (!response.ok) throw new Error("A game download was interrupted. Please try again.");
        if (!cached && cache) writes.push(cache.put(url, response.clone()).catch(() => {}));
        const chunks = [];
        const reader = response.body.getReader();
        while (true) {
          const { done, value } = await reader.read(); if (done) break;
          chunks.push(value); downloaded += value.byteLength; progress(downloaded, cached);
        }
        pieces[index] = new Blob(chunks, { type: "application/octet-stream" });
      }
    }
    // Bound simultaneous requests and temporary response buffers.
    await Promise.all(Array.from({ length: Math.min(2, urls.length) }, worker));
    await Promise.allSettled(writes);
    if (downloaded !== manifest.size) {
      if (cache) await Promise.allSettled(urls.map(url => cache.delete(url)));
      throw new Error("The world download is incomplete. Please try again.");
    }
    return new Blob(pieces, { type: "application/octet-stream" });
  }
  window.wyvernlandDownload = { load };
})();
