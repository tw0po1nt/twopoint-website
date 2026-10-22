import assert from 'node:assert/strict';
import { test } from 'node:test';

import { deferred, heading, load, settle, stubSignIn, summary } from './load.mjs';

const started = deferred();
const token = deferred();

const signIn = stubSignIn({
  start: () => {
    signIn.events.push('start');
    return started.promise;
  },
  token: () => {
    signIn.events.push('token');
    return token.promise;
  },
});

const { calls, config, events } = await load('http://localhost:4322/', { signIn });

test('the admin calls no API while the sign-in starts', () => {
  assert.deepEqual(events, ['start']);
  assert.deepEqual(calls, []);
  assert.equal(heading(), 'Dashboard');
  assert.deepEqual(summary(), [], 'The dashboard waits');
});

test('the admin asks for a token after the sign-in starts, and calls no API before the token', async () => {
  started.resolve(true);
  await settle();
  assert.deepEqual(events, ['start', 'token']);
  assert.deepEqual(calls, []);
});

test('the token goes with the first API call', async () => {
  token.resolve('token-2');
  await settle();
  assert.deepEqual(calls, [{ url: `${config.api}/api/internal/posts`, authorization: 'Bearer token-2' }]);
  assert.deepEqual(summary(), ['2', '3']);
});
