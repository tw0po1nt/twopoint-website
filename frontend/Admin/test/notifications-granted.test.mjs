import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle, stubPush } from './load.mjs';

const prompts = [];
globalThis.Notification = { permission: 'granted', requestPermission: async () => prompts.push('requestPermission') };

const keys = { endpoint: 'https://push.example.com/send/abc', p256dh: 'BNcRdre', auth: 'tBHItJI' };
const device = { id: 'aaaa', label: 'Safari on macOS', createdDate: '2026-03-01T12:00:00Z', lastSeenDate: '2026-03-02T08:30:00Z' };
const registers = [];

const respond = (url, init) => {
  const path = url.replace(/^.*\/api\//, '');

  if (path === 'internal/notifications/devices' && init.method === 'POST') {
    registers.push({ authorization: new Headers(init.headers).get('Authorization'), body: JSON.parse(init.body) });
    return answer(200, { success: true, message: null, data: device });
  }

  if (path === 'internal/notifications/devices') {
    return answer(200, { success: true, message: null, data: [device] });
  }

  return apiAnswer(url);
};

let listener;
const push = stubPush({
  permission: () => 'granted',
  subscription: async () => {
    push.events.push('subscription');
    return keys;
  },
  subscribe: async () => {
    push.events.push('subscribe');
    return keys;
  },
  onSubscriptionChange: (callback) => {
    listener = callback;
  },
});

await load('http://localhost:4322/', { respond, push });

test('a load with the permission granted sends the subscription again with no prompt and no click', () => {
  assert.deepEqual(prompts, []);
  assert.ok(!push.events.includes('subscribe'), 'The page does not subscribe');
  assert.deepEqual(registers, [{ authorization: 'Bearer token-1', body: keys }]);
});

test('a changed subscription is sent again', async () => {
  listener();
  await settle();
  assert.equal(registers.length, 2);
  assert.deepEqual(prompts, []);
});

test('the Settings page shows that the notifications are on, with no button', async () => {
  const link = [...document.querySelectorAll('nav.side-nav a')].find((item) => item.getAttribute('href') === '/settings');
  link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
  await settle();

  const section = document.querySelector('main .notifications');
  assert.match(section.textContent, /This device gets notifications\./);
  assert.ok(![...section.querySelectorAll('button')].some((button) => button.textContent.includes('Turn on')));
  assert.deepEqual([...document.querySelectorAll('main .device-row')].map((row) => row.id), ['aaaa']);
});
