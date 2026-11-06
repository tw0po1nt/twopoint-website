import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle, stubPush } from './load.mjs';

const prompts = [];
globalThis.Notification = { permission: 'granted', requestPermission: async () => prompts.push('requestPermission') };

const keys = { endpoint: 'https://push.example.com/send/new', p256dh: 'BNcRdre', auth: 'tBHItJI' };
const device = { id: 'aaaa', label: 'Chrome on Windows', createdDate: '2026-03-01T12:00:00Z', lastSeenDate: '2026-03-02T08:30:00Z' };
const registers = [];

const respond = (url, init) => {
  if (url.endsWith('/api/internal/notifications/devices') && init.method === 'POST') {
    registers.push(JSON.parse(init.body));
    return answer(200, { success: true, message: null, data: device });
  }

  return url.endsWith('/api/internal/notifications/devices') ? answer(200, { success: true, message: null, data: [] }) : apiAnswer(url);
};

let listener;
const push = stubPush({
  permission: () => 'granted',
  subscribe: async () => {
    push.events.push('subscribe');
    return keys;
  },
  onSubscriptionChange: (callback) => {
    listener = callback;
  },
});

await load('http://localhost:4322/', { respond, push });

test('a load with the permission granted and no subscription registers nothing, and does not subscribe', () => {
  assert.deepEqual(registers, []);
  assert.ok(!push.events.includes('subscribe'));
});

test('after a changed subscription, the page subscribes with no prompt, and registers the result', async () => {
  listener();
  await settle();

  assert.equal(push.events.filter((event) => event === 'subscribe').length, 1);
  assert.deepEqual(registers, [keys]);
  assert.deepEqual(prompts, []);
});
