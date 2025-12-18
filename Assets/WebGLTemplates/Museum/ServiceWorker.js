const cacheName = "SoulFlaw-Museum-0.1.0";
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

self.addEventListener('install', function (e) {
    console.log('[Service Worker] Install');
    
    e.waitUntil((async function () {
      const cache = await caches.open(cacheName);
      console.log('[Service Worker] Caching all: app shell and content');
      await cache.addAll(contentToCache);
    })());
});

self.addEventListener('fetch', function (event) {
    if (event.request.method !== 'GET') {
        return;
    }

    const rangeHeader = event.request.headers.get('range');

    event.respondWith((async function () {
        const cache = await caches.open(cacheName);
        const normalizedRequest = rangeHeader ? new Request(event.request.url) : event.request;
        let response = await caches.match(normalizedRequest);

        console.log(`[Service Worker] Fetching resource: ${event.request.url}`);

        if (!rangeHeader && response) {
            console.log('发现本地缓存');
            return response;
        }

        response = await fetch(event.request);

        if (rangeHeader) {
            if (response && response.status === 200) {
                await cache.put(normalizedRequest, response.clone());
            } else if (response && response.status === 206) {
                event.waitUntil(cacheFullContent(cache, normalizedRequest));
            }
            return response;
        }

        if (response && response.ok && response.status === 200) {
            console.log(`[Service Worker] Caching new resource: ${event.request.url}`);
            await cache.put(event.request, response.clone());
        }

        return response;
    })());
});

async function cacheFullContent(cache, request) {
    const alreadyCached = await cache.match(request);
    if (alreadyCached) {
        return;
    }

    try {
        const fullRequest = new Request(request.url, { method: 'GET', headers: new Headers({ 'cache-bust': Date.now().toString() }) });
        const fullResponse = await fetch(fullRequest);
        if (fullResponse && fullResponse.ok && fullResponse.status === 200) {
            console.log(`[Service Worker] Caching full resource after partial fetch: ${request.url}`);
            await cache.put(request, fullResponse.clone());
        }
    } catch (error) {
        console.warn(`[Service Worker] Failed to cache full content: ${request.url}`, error);
    }
}
