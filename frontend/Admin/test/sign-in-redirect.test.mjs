import assert from 'node:assert/strict';
import { test } from 'node:test';

import { banner, load, press, settle, stubSignIn } from './load.mjs';

const signIn = stubSignIn({
  start: async () => {
    signIn.events.push('start');
    return false;
  },
  signIn: async () => {
    signIn.events.push('signIn');
    throw new Error('The stub has no Entra.');
  },
});

const { calls, events } = await load('http://localhost:4322/comments', { signIn });

test('with no session, the admin sends the page to the Entra sign-in and calls no API', () => {
  assert.deepEqual(events, ['start', 'signIn']);
  assert.deepEqual(calls, []);
});

test('a failed sign-in shows a Sign in button that tries again', async () => {
  assert.match(banner(), /^Sign-in failed\./);
  press('Sign in');
  await settle();
  assert.deepEqual(events, ['start', 'signIn', 'signIn']);
  assert.deepEqual(calls, []);
});
