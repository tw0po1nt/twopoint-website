// The push worker. The page registers it at /sw.js, so its scope is the whole admin.

// Chrome and Firefox can send the event with no old subscription, and the page then subscribes.
const resubscribe = async (event) => {
  const key = event.oldSubscription?.options?.applicationServerKey;

  if (!event.newSubscription && key) {
    await self.registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
  }

  // A worker has no token for the API, so each open page sends the subscription.
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
