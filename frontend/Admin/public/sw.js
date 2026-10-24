// The push worker. The page registers it at /sw.js, so its scope is the whole admin.

// A worker has no token for the API. It makes the new subscription, then tells each open page to send it.
const resubscribe = async (event) => {
  const key = event.oldSubscription?.options?.applicationServerKey;

  if (key) {
    await self.registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
  }

  const pages = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

  for (const page of pages) {
    page.postMessage({ type: 'pushsubscriptionchange' });
  }
};

self.addEventListener('pushsubscriptionchange', (event) => {
  event.waitUntil(resubscribe(event));
});

self.addEventListener('install', () => {
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});
