import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, stubPush } from './load.mjs';

const device = { id: 'aaaa', label: 'Safari on macOS', createdDate: '2026-03-01T12:00:00Z', lastSeenDate: '2026-03-02T08:30:00Z' };

const respond = (url) =>
  url.endsWith('/api/internal/notifications/devices') ? answer(200, { success: true, message: null, data: [device] }) : apiAnswer(url);

const push = stubPush({ permission: () => 'granted', publicKey: () => '' });

await load('http://localhost:4322/settings', { respond, push });

const section = () => document.querySelector('main .notifications');

test('a build with no VAPID public key says that it cannot turn on notifications, and still lists the devices', () => {
  assert.match(section().textContent, /Notifications aren't set up for this build/);
  assert.ok(![...section().querySelectorAll('button')].some((button) => button.textContent.includes('Turn on')));
  assert.deepEqual([...document.querySelectorAll('main .device-row')].map((row) => row.id), ['aaaa']);
});

test('a build with no VAPID public key never reads or makes a subscription', () => {
  assert.deepEqual(push.events.filter((event) => event === 'subscribe' || event === 'subscription'), []);
});
