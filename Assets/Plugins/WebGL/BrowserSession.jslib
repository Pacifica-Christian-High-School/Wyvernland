mergeInto(LibraryManager.library, {
  WyvernlandReturnToWebsite: function () {
    window.dispatchEvent(new Event("wyvernland-return-to-website"));
  },
  WyvernlandPublishPlayer: function (json) {
    if (window.wyvernlandOnline) window.wyvernlandOnline.publish(UTF8ToString(json));
  },
  WyvernlandLeaveOnline: function () {
    if (window.wyvernlandOnline) window.wyvernlandOnline.stop();
  },
  WyvernlandSelectedMap: function () {
    return window.wyvernlandRooms && window.wyvernlandRooms.current ? (window.wyvernlandRooms.current.map === "test-ground" ? 1 : 0) : -1;
  },
  WyvernlandWorldReady: function (baseline) { if(window.wyvernlandWorld) window.wyvernlandWorld.ready(UTF8ToString(baseline)); },
  WyvernlandPublishWorld: function (json) { if(window.wyvernlandWorld) window.wyvernlandWorld.publish(UTF8ToString(json)); },
  WyvernlandWorldRequest: function (json) { if(window.wyvernlandWorld) window.wyvernlandWorld.request(UTF8ToString(json)); },
  WyvernlandSetHosting: function (hosting) { if(window.wyvernlandBackgroundLoading) window.wyvernlandBackgroundLoading.setHosting(hosting !== 0); },
  WyvernlandSetLoading: function (loading) {
    if (window.wyvernlandBackgroundLoading) window.wyvernlandBackgroundLoading.setLoading(loading !== 0);
  }
});
