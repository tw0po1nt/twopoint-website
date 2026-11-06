import assert from 'node:assert/strict';
import { test } from 'node:test';

import { banner, load, press, settle } from './load.mjs';

const refused = () => new Response('', { status: 403 });

const { calls, events } = await load('http://localhost:4322/', { respond: refused });

test('a refused account shows a banner that offers another account', () => {
  assert.equal(calls.length, 3);
  assert.match(banner(), /^The API refused this account\. Sign in with the author account\./);
});

test('Use another account sends the page to the Entra sign-in', async () => {
  press('Use another account');
  await settle();
  assert.deepEqual(events, ['start', 'token', 'token', 'token', 'signIn']);
  assert.equal(calls.length, 3);
});
