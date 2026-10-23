import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, load, postsAnswer, press, settle } from './load.mjs';

const rebuilds = [];
const replies = [
  answer(200, { success: true, message: null, data: { dispatch: 'queued', productionRun: 42 } }),
  answer(502, { success: false, message: 'The dispatch to tw0po1nt/twopoint-website-staging failed.', data: null }),
];

const respond = (url, init) => {
  if (url.endsWith('/api/internal/deploys')) {
    rebuilds.push({ method: init.method, authorization: new Headers(init.headers).get('Authorization') });
    return replies.shift();
  }

  return postsAnswer();
};

const { config } = await load('http://localhost:4322/', { respond });

const card = () => document.querySelector('main .card.rebuild');
const text = () => card().querySelector('.rebuild-text').textContent.trim();
const alert = () => card().querySelector('.alert')?.textContent.trim() ?? null;

test('the dashboard has Rebuild site, and sends nothing before a click', () => {
  assert.ok([...card().querySelectorAll('button')].some((button) => button.textContent.trim() === 'Rebuild site'));
  assert.deepEqual(rebuilds, []);
});

test('Rebuild site posts to the deploys endpoint with the token, and shows the production run', async () => {
  press('Rebuild site', card());
  await settle();
  assert.deepEqual(rebuilds, [{ method: 'POST', authorization: 'Bearer token-1' }]);
  assert.equal(text(), 'The rebuild started. The production run is #42.');
  assert.equal(alert(), null);
  assert.ok(config.api);
});

test('a failed dispatch shows the reason from the API', async () => {
  press('Rebuild site', card());
  await settle();
  assert.equal(rebuilds.length, 2);
  assert.equal(text(), 'The rebuild did not start.');
  assert.equal(alert(), 'The dispatch to tw0po1nt/twopoint-website-staging failed.');
});
