import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const post = {
  slug: 'my-post',
  state: 'published',
  pendingEdit: false,
  publishDate: '2026-02-04T13:00:00Z',
  eTag: null,
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: noDetails,
  text: '',
  publishedText: null,
};

const ok = (data) => answer(200, { success: true, message: null, data });

const states = ['failed', 'deployed'];
const reads = [];
const rebuilds = [];

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    reads.push(new Headers(init.headers).get('Authorization'));
    return ok({ deploy: states.shift() });
  }

  if (url.endsWith('/api/internal/deploys')) {
    rebuilds.push(init.method);
    return ok({ dispatch: 'queued', productionRun: 42 });
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(post);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const status = () => document.querySelector('.editor-status').textContent.replace(/\s+/g, ' ').trim();
const retry = () => [...document.querySelectorAll('.editor-status button')].find((item) => item.textContent.trim() === 'retry');

test('the editor reads the deploy state at the page load and shows a failed deploy with retry', () => {
  assert.deepEqual(reads, ['Bearer token-1']);
  assert.match(status(), /^Published Feb 4, 2026 · deploy failed/);
  assert.ok(retry(), 'The status offers retry');
});

test('retry sends the rebuild, reads the deploy state again, and shows the new state', async () => {
  retry().click();
  await settle();

  assert.deepEqual(rebuilds, ['POST']);
  assert.equal(reads.length, 2);
  assert.equal(status(), 'Published Feb 4, 2026 · deployed');
  assert.equal(retry(), undefined);
});
