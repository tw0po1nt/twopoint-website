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

const bodyLength = 120;

const readData = (event) => {
  try {
    return event.data ? event.data.json() : null;
  } catch {
    return null;
  }
};

const targetOf = (data) =>
  data.case === 'approved'
    ? `/posts/${encodeURIComponent(data.slug)}/comments#${encodeURIComponent(data.commentId)}`
    : `/comments#${encodeURIComponent(data.commentId)}`;

const noticeOf = (data) => ({
  title: `${data.case === 'approved' ? 'Comment posted on' : 'New comment on'} ${data.postTitle}`,
  options: {
    body: `${data.commenter}: ${[...String(data.text)].slice(0, bodyLength).join('')}`,
    tag: data.commentId,
    data: { target: targetOf(data) },
  },
});

// A browser requires a notification for each push, so a payload that is not readable still shows one.
const fallbackNotice = { title: 'New comment', options: { data: { target: '/comments' } } };

const setBadge = async (count) => {
  if (typeof count === 'number' && self.navigator && 'setAppBadge' in self.navigator) {
    await self.navigator.setAppBadge(count);
  }
};

const tellPages = async (message) => {
  const pages = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

  for (const page of pages) {
    page.postMessage(message);
  }

  return pages;
};

const show = async (event) => {
  const data = readData(event);
  const { title, options } = data ? noticeOf(data) : fallbackNotice;

  await self.registration.showNotification(title, options);

  if (data) {
    await setBadge(data.newCount);
    await tellPages({ type: 'new-comment', count: data.newCount });
  }
};

self.addEventListener('push', (event) => {
  event.waitUntil(show(event));
});

const open = async (target) => {
  const pages = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

  if (pages.length === 0) {
    await self.clients.openWindow(target);
    return;
  }

  await pages[0].focus();
  pages[0].postMessage({ type: 'open', target });
};

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  event.waitUntil(open(event.notification.data?.target ?? '/comments'));
});

self.addEventListener('install', () => {
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});
