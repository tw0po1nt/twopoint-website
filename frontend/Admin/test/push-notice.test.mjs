import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import vm from 'node:vm';

const source = await readFile('dist/sw.js', 'utf8');

// The worker runs in a scope with `self`, so the test gives it a stub of that scope.
const start = ({ windows = [], badge = true } = {}) => {
  const listeners = {};
  const shown = [];
  const badges = [];
  const opened = [];
  const closed = [];
  const messages = [];
  const focused = [];

  const clientOf = (name) => ({
    focus: async () => focused.push(name),
    postMessage: (message) => messages.push([name, JSON.parse(JSON.stringify(message))]),
  });

  const self = {
    addEventListener: (type, listener) => {
      listeners[type] = listener;
    },
    registration: {
      showNotification: async (title, options) => {
        shown.push({ title, ...JSON.parse(JSON.stringify(options)) });
      },
    },
    navigator: badge ? { setAppBadge: async (count) => badges.push(count) } : {},
    clients: {
      matchAll: async () => windows.map(clientOf),
      openWindow: async (url) => opened.push(url),
      claim: async () => {},
    },
    skipWaiting: () => {},
  };

  vm.runInNewContext(source, { self });
  return { listeners, shown, badges, opened, closed, messages, focused };
};

const fire = async (worker, type, event) => {
  const waits = [];
  worker.listeners[type]({ ...event, waitUntil: (promise) => waits.push(promise) });
  await Promise.all(waits);
};

const payload = (fields) => ({ data: { json: () => ({ case: 'awaits-moderation', commentId: 'c-1', slug: 'first-post', title: 'New comment on First post', body: 'Ada: A comment.', newCount: 3, ...fields }) } });

// The case gives the target. The backend writes the title and the body.
const cases = [
  {
    name: 'a comment that waits for moderation',
    fields: { case: 'awaits-moderation' },
    target: '/comments#c-1',
  },
  {
    name: 'a comment of a trusted commenter',
    fields: { case: 'approved' },
    target: '/posts/first-post/comments#c-1',
  },
];

for (const { name, fields, target } of cases) {
  test(`${name} gives the title, the body, the tag, and the target`, async () => {
    const worker = start();

    await fire(worker, 'push', payload(fields));

    assert.equal(worker.shown.length, 1);
    assert.equal(worker.shown[0].title, 'New comment on First post');
    assert.equal(worker.shown[0].body, 'Ada: A comment.');
    assert.equal(worker.shown[0].tag, 'c-1');
    assert.equal(worker.shown[0].data.target, target);
  });
}

test('a reply shows the title and the body from the backend', async () => {
  const worker = start();

  await fire(worker, 'push', payload({ title: 'Ben replied to Ada', body: 'Use List.map.' }));

  assert.equal(worker.shown[0].title, 'Ben replied to Ada');
  assert.equal(worker.shown[0].body, 'Use List.map.');
});

test('a slug with a special character is safe in the target', async () => {
  const worker = start();

  await fire(worker, 'push', payload({ case: 'approved', slug: 'a b', commentId: 'c 2' }));

  assert.equal(worker.shown[0].data.target, '/posts/a%20b/comments#c%202');
});

test('a push sets the badge to the count of new comments, and tells each open page', async () => {
  const worker = start({ windows: ['one', 'two'] });

  await fire(worker, 'push', payload({ newCount: 5 }));

  assert.deepEqual(worker.badges, [5]);
  assert.deepEqual(worker.messages, [
    ['one', { type: 'new-comment', count: 5 }],
    ['two', { type: 'new-comment', count: 5 }],
  ]);
});

test('a browser with no badge API still shows the notification', async () => {
  const worker = start({ badge: false });

  await fire(worker, 'push', payload({}));

  assert.equal(worker.shown.length, 1);
  assert.deepEqual(worker.badges, []);
});

test('a push with no readable payload still shows a notification, because the browser requires it', async () => {
  const worker = start();

  await fire(worker, 'push', { data: { json: () => { throw new Error('no JSON'); } } });
  await fire(worker, 'push', {});

  assert.equal(worker.shown.length, 2);
  assert.equal(worker.shown[0].title, 'New comment');
  assert.equal(worker.shown[0].data.target, '/comments');
});

test('a failed production deploy gives a notice that opens the dashboard, and sets no badge', async () => {
  const worker = start({ windows: ['one'] });

  await fire(worker, 'push', { data: { json: () => ({ case: 'deploy-failed', runNumber: 41 }) } });

  assert.equal(worker.shown.length, 1);
  assert.equal(worker.shown[0].title, 'Production deploy failed');
  assert.equal(worker.shown[0].body, 'Run #41 of site.yml failed. Tap to open the dashboard and rebuild.');
  assert.equal(worker.shown[0].tag, 'deploy-41');
  assert.equal(worker.shown[0].data.target, '/');
  assert.deepEqual(worker.badges, []);
  assert.deepEqual(worker.messages, []);
});

const click = (target) => ({ notification: { data: { target }, close: () => {} } });

test('a click with an open admin tab brings the tab to the front and asks it to open the target', async () => {
  const worker = start({ windows: ['one'] });

  await fire(worker, 'notificationclick', click('/comments#c-1'));

  assert.deepEqual(worker.focused, ['one']);
  assert.deepEqual(worker.messages, [['one', { type: 'open', target: '/comments#c-1' }]]);
  assert.deepEqual(worker.opened, []);
});

test('a click with no open admin tab opens a window at the target', async () => {
  const worker = start();

  await fire(worker, 'notificationclick', click('/comments#c-1'));

  assert.deepEqual(worker.opened, ['/comments#c-1']);
});

test('a click closes the notification', async () => {
  const worker = start();
  let closed = false;

  await fire(worker, 'notificationclick', { notification: { data: { target: '/comments' }, close: () => { closed = true; } } });

  assert.ok(closed);
});
