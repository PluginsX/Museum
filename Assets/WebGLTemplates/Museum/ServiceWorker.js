const cacheName = "SoulFlaw-Museum-1.0.0-" + Date.now();
const contentToCache = [
    "Build/Build.loader.js",
    "Build/Build.framework.js.unityweb",
    "Build/Build.data.unityweb",
    "Build/Build.wasm.unityweb",
    "TemplateData/style.css",
    "TemplateData/favicon.ico",
    "TemplateData/progress-bar-full-dark.png",
    "TemplateData/progress-bar-full-light.png"
];

// Unity WebGL WASM文件的特殊缓存处理
const wasmUrl = "Build/Build.wasm.unityweb";

self.addEventListener('install', function (e) {
    console.log('[Service Worker] Install');

    e.waitUntil((async function () {
      try {
        const cache = await caches.open(cacheName);
        console.log('[Service Worker] Caching all: app shell and content');

        // 先缓存其他资源
        const otherResources = contentToCache.filter(url => url !== wasmUrl);
        await cache.addAll(otherResources);

        // 特殊处理WASM文件 - 直接获取并缓存
        try {
          const response = await fetch(wasmUrl);
          if (response.ok) {
            await cache.put(wasmUrl, response);
            console.log('[Service Worker] WASM file cached successfully');
          }
        } catch (wasmError) {
          console.warn('[Service Worker] Failed to cache WASM file:', wasmError);
        }

      } catch (error) {
        console.error('[Service Worker] Install failed:', error);
      }
    })());
});

self.addEventListener('fetch', function (e) {
    e.respondWith((async function () {
      let response = await caches.match(e.request);
      console.log(`[Service Worker] Fetching resource: ${e.request.url}`);

      if (response) {
          console.log('[Service Worker] Serving from cache:', e.request.url);

          // 特别显示WASM缓存命中
          if (e.request.url.includes(wasmUrl)) {
            console.log('🎯 WASM文件命中缓存！');
          }

          return response;
      }

      // 如果缓存中没有，尝试从网络获取
      try {
        response = await fetch(e.request);

        // 对于WASM文件，总是尝试缓存后续请求
        if (e.request.url.includes(wasmUrl) && response.ok) {
          const cache = await caches.open(cacheName);
          cache.put(e.request, response.clone());
          console.log('[Service Worker] WASM file cached on-the-fly');
        }

        return response;
      } catch (error) {
        console.error('[Service Worker] Fetch failed:', error);
        // 如果网络请求失败且有缓存，尝试使用旧版本
        if (response) {
          return response;
        }
        throw error;
      }
    })());
});

// 监听消息以进行缓存管理
self.addEventListener('message', function (e) {
  if (e.data && e.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }

  if (e.data && e.data.type === 'CLEAR_CACHE') {
    caches.delete(cacheName).then(() => {
      console.log('[Service Worker] Cache cleared');
    });
  }
});
