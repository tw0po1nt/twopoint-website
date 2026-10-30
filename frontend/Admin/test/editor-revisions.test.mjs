import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const live = {
  slug: 'my-post',
  state: 'published',
  pendingEdit: false,
  publishDate: '2026-02-04T13:00:00Z',
  eTag: null,
  title: 'Live title',
  excerpt: 'About it.',
  body: 'Live body.\n',
  details: noDetails,
  text: '',
  published: { title: 'Live title', excerpt: 'About it.', body: 'Live body.\n', details: noDetails },
};

const restored = { ...live, pendingEdit: true, eTag: '"w2"', title: 'First title', body: 'First body.\n' };

const revisions = [
  { name: '2026-02-04T13:00:00Z', publishedAt: '2026-02-04T13:00:00Z', title: 'Live title' },
  { name: '2026-01-01T00:00:00.0000000Z', publishedAt: '2026-01-01T00:00:00Z', title: 'First title' },
];

const ok = (data) => answer(200, { success: true, message: null, data });

const restores = [];

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/revisions')) {
    return ok(revisions);
  }

  if (url.endsWith('/api/internal/posts/my-post/revisions/restore')) {
    const headers = new Headers(init.headers);
    restores.push({
      method: init.method,
      ifMatch: headers.get('If-Match'),
      ifNoneMatch: headers.get('If-None-Match'),
      body: JSON.parse(init.body),
    });
    return ok({ eTag: '"w2"' });
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(restores.length > 0 ? restored : live);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const status = () => document.querySelector('.editor-status').textContent.trim();
const menuItem = (label) => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim().startsWith(label));
const dialog = () => document.querySelector('#revisions-dialog');
const button = (root, label) => [...root.querySelectorAll('button')].find((item) => item.textContent.trim() === label);

test('Revisions lists each revision, the newest first, with its title and time', async () => {
  assert.equal(menuItem('Revisions').getAttribute('aria-disabled'), null);
  assert.ok(!dialog().open, 'The dialog starts closed');

  menuItem('Revisions').click();
  await settle();

  assert.ok(dialog().open, 'The dialog is open');
  assert.deepEqual(
    [...dialog().querySelectorAll('.revision')].map((entry) => entry.querySelector('.revision-title').textContent.trim()),
    ['Live title', 'First title'],
  );
  assert.deepEqual(
    [...dialog().querySelectorAll('.revision button')].map((entry) => entry.getAttribute('aria-label')),
    ['Restore the revision of Feb 4, 2026, 13:00 UTC', 'Restore the revision of Jan 1, 2026, 00:00 UTC'],
  );

  button(dialog(), 'Close').click();
  await settle();
  assert.ok(!dialog().open, 'The dialog closes');
  assert.equal(restores.length, 0);
});

test('Restore sends the revision name with If-None-Match, and the editor shows the revision as a pending edit', async () => {
  menuItem('Revisions').click();
  await settle();

  dialog().querySelectorAll('.revision button')[1].click();
  await settle();

  assert.deepEqual(restores, [
    { method: 'POST', ifMatch: null, ifNoneMatch: '*', body: { revision: '2026-01-01T00:00:00.0000000Z' } },
  ]);
  assert.ok(!dialog().open, 'The dialog closes');
  assert.equal(document.querySelector('#editor-title').value, 'First title');
  assert.equal(status(), 'Published Feb 4, 2026 · deployed · changes not live · saved');
});
