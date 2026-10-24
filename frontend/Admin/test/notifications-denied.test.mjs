import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, stubPush } from './load.mjs';

const respond = (url) =>
  url.endsWith('/api/internal/notifications/devices') ? answer(200, { success: true, message: null, data: [] }) : apiAnswer(url);

const push = stubPush({ permission: () => 'denied' });

const { config } = await load('http://localhost:4322/settings', { respond, push });

const skip = config.vapidPublicKey ? false : 'The build has no VAPID public key.';

const section = () => document.querySelector('main .notifications');

test('with the permission denied, the section tells the author to change the browser setting', { skip }, () => {
  assert.match(section().textContent, /The browser blocks notifications for this site\. Change the browser setting/);
  assert.ok(![...section().querySelectorAll('button')].some((button) => button.textContent.includes('Turn on')));
});

test('with the permission denied, the page sends no subscription and does not subscribe', () => {
  assert.deepEqual(push.events.filter((event) => event === 'subscribe' || event === 'subscription'), []);
});

test('a list with no device says so', () => {
  assert.match(section().textContent, /No device is registered\./);
});
