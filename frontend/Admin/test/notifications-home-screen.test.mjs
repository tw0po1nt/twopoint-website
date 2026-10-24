import assert from 'node:assert/strict';
import { test } from 'node:test';

import { load, stubPush } from './load.mjs';

const push = stubPush({ support: () => 'home-screen' });

await load('http://localhost:4322/settings', { push });

const section = () => document.querySelector('main .notifications');

test('on iOS with no PushManager, the section says to add the admin to the Home Screen', () => {
  assert.match(section().textContent, /Add the admin to the Home Screen to turn on notifications\./);
  assert.ok(![...section().querySelectorAll('button')].some((button) => button.textContent.includes('Turn on')));
});

test('on iOS with no PushManager, the page never subscribes', () => {
  assert.deepEqual(push.events.filter((event) => event === 'subscribe' || event === 'subscription'), []);
});
