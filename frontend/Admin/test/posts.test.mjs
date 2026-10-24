import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, heading, load, settle } from './load.mjs';

const stats = { new: 1, approved: 2, rejected: 0 };

const posts = [
  { slug: 'new-draft', title: 'A draft', state: 'draft', pendingEdit: false, publishDate: null, commentStats: { new: 0, approved: 0, rejected: 0 } },
  { slug: 'soon', title: 'Soon', state: 'upcoming', pendingEdit: false, publishDate: '2099-01-02T00:00:00Z', commentStats: stats },
  { slug: 'edited', title: 'Edited', state: 'published', pendingEdit: true, publishDate: '2026-01-03T00:00:00Z', commentStats: stats },
  { slug: 'live', title: 'Live', state: 'published', pendingEdit: false, publishDate: '2026-01-02T00:00:00Z', commentStats: stats },
];

const reads = [];

const respond = (url) => {
  if (url.endsWith('/api/internal/posts')) {
    reads.push(url);
    return answer(200, { success: true, message: null, data: posts });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);

test('the post list shows each post with its state, its publish date, and its comment counts', () => {
  assert.equal(heading(), 'Posts');
  assert.deepEqual(rows(), ['new-draft', 'soon', 'edited', 'live']);
  assert.match(document.getElementById('new-draft').textContent, /Draft/);
  assert.match(document.getElementById('new-draft').textContent, /No publish date/);
  assert.match(document.getElementById('soon').textContent, /Upcoming/);
  assert.match(document.getElementById('live').textContent, /Published/);
  assert.match(document.getElementById('live').textContent, /2026-01-02/);
  assert.match(document.getElementById('live').textContent, /1 new, 2 approved, 0 rejected/);
  assert.match(document.getElementById('edited').textContent, /Pending edit/);
  assert.doesNotMatch(document.getElementById('live').textContent, /Pending edit/);
});

test('the filters show only the drafts, or only the posts with a pending edit', async () => {
  const before = reads.length;
  tab('Drafts').click();
  await settle();
  assert.deepEqual(rows(), ['new-draft']);

  tab('Pending edits').click();
  await settle();
  assert.deepEqual(rows(), ['edited']);

  tab('All').click();
  await settle();
  assert.equal(rows().length, 4);
  assert.equal(reads.length, before, 'The filter calls no API');
});
