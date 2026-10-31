import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, heading, links, load } from './load.mjs';

const posts = [
  { slug: 'new-draft', title: 'A draft', state: 'draft', pendingEdit: false, publishDate: null, deploy: 'deployed', deliveryToStart: false, commentStats: { new: 0, approved: 0, rejected: 0 } },
  { slug: 'live', title: 'Live', state: 'published', pendingEdit: false, publishDate: '2026-01-02T00:00:00Z', deploy: 'deployed', deliveryToStart: false, commentStats: { new: 0, approved: 0, rejected: 0 } },
];

const respond = (url) =>
  url.endsWith('/api/internal/posts') ? answer(200, { success: true, message: null, data: posts }) : apiAnswer(url);

await load('http://localhost:4322/posts?filter=drafts', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);

test('an admin that starts at a filter of the post list shows that filter', () => {
  assert.equal(heading(), 'Posts');
  assert.equal(tab('Drafts').getAttribute('aria-selected'), 'true');
  assert.equal(tab('All').getAttribute('aria-selected'), 'false');
  assert.deepEqual(rows(), ['new-draft']);
});

test('the navigation marks Posts as current and links to the post list with All', () => {
  assert.deepEqual(
    links().find((link) => link.text === 'Posts'),
    { text: 'Posts', href: '/posts', current: 'page' },
  );
});
