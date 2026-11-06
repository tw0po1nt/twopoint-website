import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const pending = {
  slug: 'my-post',
  state: 'published',
  pendingEdit: true,
  publishDate: '2026-02-04T13:00:00Z',
  eTag: '"w1"',
  title: 'New title',
  excerpt: 'About it.',
  body: 'First line.\nSecond line, edited.\nThird line.\n',
  details: noDetails,
  text: '',
  published: { title: 'Old title', excerpt: 'About it.', body: 'First line.\nSecond line.\nThird line.\n', details: noDetails },
};

const clean = { ...pending, pendingEdit: false, eTag: null, title: 'Old title', body: pending.published.body };

const ok = (data) => answer(200, { success: true, message: null, data });

const discards = [];
let discarded = false;

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/changes/discard')) {
    discards.push({ method: init.method, ifMatch: new Headers(init.headers).get('If-Match') });
    discarded = true;
    return ok(null);
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(discarded ? clean : pending);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const status = () => document.querySelector('.editor-status').textContent.trim();
const bar = () => document.querySelector('.editor-bar');
const menuItem = (label) => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim().startsWith(label));
const changes = () => document.querySelector('#changes-dialog');
const discard = () => document.querySelector('#discard-dialog');
const button = (root, label) => [...root.querySelectorAll('button')].find((item) => item.textContent.trim() === label);

test('a pending edit shows that the changes are not live, and the primary action publishes the changes', () => {
  assert.equal(status(), 'Published Feb 4, 2026 · deployed · changes not live · saved');
  assert.equal(bar().querySelector('.editor-primary').textContent.trim(), 'Publish changes');
  assert.equal(bar().querySelector('.editor-primary').disabled, false);
});

test('Changes from live shows the lines that changed, and Close closes the dialog', async () => {
  assert.equal(menuItem('Compare with live').getAttribute('aria-disabled'), null);
  assert.ok(!changes().open, 'The dialog starts closed');

  menuItem('Compare with live').click();
  await settle();

  assert.ok(changes().open, 'The dialog is open');
  assert.deepEqual(
    [...changes().querySelectorAll('.diff-added, .diff-removed')].map((line) => line.textContent.trim()),
    ['-Removed: Old title', '+Added: New title', '-Removed: Second line.', '+Added: Second line, edited.'],
  );

  button(changes(), 'Close').click();
  await settle();
  assert.ok(!changes().open, 'The dialog closes');
});

test('Keep closes the discard dialog and sends nothing, and Discard sends the ETag and shows the published text', async () => {
  menuItem('Discard changes').click();
  await settle();
  assert.ok(discard().open, 'The dialog is open');

  button(discard(), 'Cancel').click();
  await settle();
  assert.ok(!discard().open, 'The dialog closes');
  assert.equal(discards.length, 0);

  menuItem('Discard changes').click();
  await settle();
  button(discard(), 'Discard').click();
  await settle();

  assert.deepEqual(discards, [{ method: 'POST', ifMatch: '"w1"' }]);
  assert.equal(document.querySelector('#editor-title').value, 'Old title');
  assert.equal(status(), 'Published Feb 4, 2026 · deployed');
  assert.equal(bar().querySelector('.editor-primary').disabled, true);
  assert.match(menuItem('Discard changes').textContent, /No changes to discard\./);
});
