import assert from 'node:assert/strict';
import { test } from 'node:test';

import { banner, load, press, settle, stubSignIn, summary } from './load.mjs';

const tokens = [null, 'token-3'];
let popups = 0;

const signIn = stubSignIn({
  token: async () => {
    signIn.events.push('token');
    return tokens.shift();
  },
  signInByPopup: async () => {
    signIn.events.push('signInByPopup');
    popups += 1;

    if (popups === 1) {
      throw new Error('The author closed the popup.');
    }
  },
});

const { calls, config, events } = await load('http://localhost:4322/', { signIn });

test('when Entra needs a new sign-in, the banner shows and no API call goes out', () => {
  assert.deepEqual(events, ['start', 'token']);
  assert.deepEqual(calls, []);
  assert.match(banner(), /^Your session ended\. Sign in again to continue\./);
});

test('a closed popup keeps the banner and asks the author to try again', async () => {
  press('Sign in again');
  await settle();
  assert.deepEqual(events, ['start', 'token', 'signInByPopup']);
  assert.match(banner(), /^The sign-in did not complete\. Try again\./);
  assert.deepEqual(calls, []);
});

test('after the popup, the failed call runs again with the new token and the banner goes', async () => {
  press('Sign in again');
  await settle();
  assert.deepEqual(events, ['start', 'token', 'signInByPopup', 'signInByPopup', 'token']);
  assert.deepEqual(calls, [{ url: `${config.api}/api/internal/posts`, authorization: 'Bearer token-3' }]);
  assert.equal(banner(), null);
  assert.deepEqual(summary(), ['2', '3']);
});
