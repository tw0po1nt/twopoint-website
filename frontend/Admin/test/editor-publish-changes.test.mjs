import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle } from './load.mjs';

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

const ok = (data) => answer(200, { success: true, message: null, data });

const checks = [];
const publishes = [];

const respond = (url, init = {}) => {
  const headers = new Headers(init.headers);

  if (url.endsWith('/api/internal/posts/my-post/changes/publish/check')) {
    checks.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
    return ok({ problems: [] });
  }

  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/changes/publish')) {
    publishes.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
    return ok({ publishDate: '2026-02-04T13:00:00Z', state: 'published', warnings: [] });
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(pending);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const dialog = () => document.querySelector('#publish-dialog');
const status = () => document.querySelector('.editor-status').textContent.trim();
const bar = () => document.querySelector('.editor-bar');
const confirm = () => [...dialog().querySelectorAll('.actions button')].find((item) => item.textContent.trim() === 'Publish changes');

const type = async (element, text) => {
  element.value = text;
  element.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('Publish changes opens the dialog with the changes from live, and asks for a check that keeps the update date', async () => {
  press('Publish changes', bar());
  await settle();

  assert.ok(dialog().open, 'The dialog is open');
  assert.deepEqual(checks, [{ ifMatch: '"w1"', body: { updateDate: null } }]);

  assert.deepEqual(
    [...dialog().querySelectorAll('.diff-added, .diff-removed')].map((line) => line.textContent.trim()),
    ['-Removed: Old title', '+Added: New title', '-Removed: Second line.', '+Added: Second line, edited.'],
  );

  assert.equal(confirm().disabled, false);
});

test('a date asks for a check of that UTC time, and Publish changes waits for a complete date', async () => {
  const sent = checks.length;
  const radios = dialog().querySelectorAll('input[type="radio"]');

  radios[1].dispatchEvent(new Event('change', { bubbles: true }));
  await settle();
  assert.equal(checks.length, sent, 'No check for an empty date');
  assert.equal(confirm().disabled, true);

  await type(dialog().querySelector('#update-date'), '2026-03-02T10:00');
  assert.deepEqual(checks.at(-1).body, { updateDate: '2026-03-02T10:00:00Z' });
  assert.equal(confirm().disabled, false);
});

test('Publish changes sends the ETag and the update date, closes the dialog, and shows the live state', async () => {
  confirm().click();
  await settle();

  assert.deepEqual(publishes, [{ ifMatch: '"w1"', body: { updateDate: '2026-03-02T10:00:00Z' } }]);
  assert.ok(!dialog().open, 'The dialog closes');
  assert.equal(status(), 'Published Feb 4, 2026 · deployed');
  assert.equal(bar().querySelector('.editor-primary').disabled, true);
});
