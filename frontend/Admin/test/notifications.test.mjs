import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle, stubPush } from './load.mjs';

const prompts = [];
globalThis.Notification = { permission: 'default', requestPermission: async () => prompts.push('requestPermission') };

const keys = { endpoint: 'https://push.example.com/send/abc', p256dh: 'BNcRdre', auth: 'tBHItJI' };

const device = (id, label) => ({ id, label, createdDate: '2026-03-01T12:00:00Z', lastSeenDate: '2026-03-02T08:30:00Z' });

let devices = [device('aaaa', 'Safari on macOS'), device('bbbb', 'Safari on iOS')];
const writes = [];

const respond = (url, init) => {
  const path = url.replace(/^.*\/api\//, '');
  const authorization = new Headers(init.headers).get('Authorization');

  if (path === 'internal/notifications/devices' && init.method === 'GET') {
    return answer(200, { success: true, message: null, data: devices });
  }

  if (path === 'internal/notifications/devices' && init.method === 'POST') {
    writes.push({ method: 'POST', authorization, body: JSON.parse(init.body) });
    devices = [device('cccc', 'Chrome on Windows'), ...devices];
    return answer(200, { success: true, message: null, data: devices[0] });
  }

  if (path.startsWith('internal/notifications/devices/') && init.method === 'DELETE') {
    const id = path.split('/').pop();
    writes.push({ method: 'DELETE', authorization, id });
    devices = devices.filter((item) => item.id !== id);
    return answer(200, { success: true, message: null, data: null });
  }

  return apiAnswer(url);
};

const push = stubPush({
  subscribe: async () => {
    push.events.push('subscribe');
    return keys;
  },
});

await load('http://localhost:4322/settings', { respond, push });

const section = () => document.querySelector('main .notifications');
const rows = () => [...document.querySelectorAll('main .device-row')].map((row) => row.id);

test('a load shows no permission prompt and sends no subscription', () => {
  assert.deepEqual(prompts, []);
  assert.deepEqual(writes, []);
  assert.ok(!push.events.includes('subscribe'), 'The page does not subscribe');
  assert.ok(!push.events.includes('subscription'), 'The permission is not granted, so the page reads no subscription');
  assert.ok(push.events.includes('onSubscriptionChange'), 'The page listens for a changed subscription');
});

test('the Notifications section offers the button, and lists each registered device', () => {
  assert.match(section().textContent, /Notifications are off on this device\./);
  assert.ok([...section().querySelectorAll('button')].some((button) => button.textContent.trim() === 'Turn on notifications on this device'));
  assert.deepEqual(rows(), ['aaaa', 'bbbb']);

  const first = document.getElementById('aaaa').textContent;
  assert.match(first, /Safari on macOS/);
  assert.match(first, /Added 2026-03-01/);
  assert.match(first, /Last used 2026-03-02/);
});

test('the button subscribes from the click, then sends the subscription with the token', async () => {
  press('Turn on notifications on this device', section());
  await settle();

  assert.equal(push.events.filter((event) => event === 'subscribe').length, 1);
  assert.deepEqual(writes, [{ method: 'POST', authorization: 'Bearer token-1', body: keys }]);
  assert.deepEqual(rows(), ['cccc', 'aaaa', 'bbbb'], 'The list loads again');
});

test('Remove deletes the device, and the row goes away', async () => {
  press('Remove', document.getElementById('aaaa'));
  await settle();

  assert.deepEqual(writes.at(-1), { method: 'DELETE', authorization: 'Bearer token-1', id: 'aaaa' });
  assert.deepEqual(rows(), ['cccc', 'bbbb']);
  assert.ok(!push.events.includes('unsubscribe'), 'Another device keeps the subscription of this browser');
});

test('Remove on the device of this browser also ends its subscription, and the section is off', async () => {
  press('Remove', document.getElementById('cccc'));
  await settle();

  assert.deepEqual(writes.at(-1), { method: 'DELETE', authorization: 'Bearer token-1', id: 'cccc' });
  assert.deepEqual(rows(), ['bbbb']);
  assert.equal(push.events.filter((event) => event === 'unsubscribe').length, 1);
  assert.match(section().textContent, /Notifications are off on this device\./);
});
