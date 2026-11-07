import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const ada = { id: 'ada-id', email: 'ada@example.com', name: 'Ada', status: 'new', createdDate: '2026-01-01T00:00:00Z' };

const comment = (id, status, replyTo = null) => ({
  id,
  createdDate: '2026-02-04T00:00:00Z',
  commenter: ada,
  status,
  content: `<p>Comment ${id}</p>`,
  replyTo,
  topLevel: replyTo && { id: replyTo, commenter: ada, writer: 'commenter', status: 'approved', excerpt: 'Comment', content: '<p>Comment</p>' },
});

const comments = [
  comment('c-1', 'approved'),
  comment('c-2', 'approved', 'c-1'),
  comment('c-3', 'new'),
  comment('c-4', 'new', 'c-1'),
];

const respond = (url) => {
  if (url.endsWith('/api/internal/posts/first/comments')) {
    return answer(200, { success: true, message: null, data: comments });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/first/comments', { respond });

const threads = () =>
  [...document.querySelectorAll('main .thread')].map((thread) => [
    thread.querySelector(':scope > .queue-row').id,
    [...thread.querySelectorAll('.thread-replies .queue-row')].map((item) => item.id),
  ]);
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);

test('each thread shows its top-level comment, then its replies indented, oldest first, each with its status', () => {
  assert.deepEqual(threads(), [
    ['c-1', ['c-2', 'c-4']],
    ['c-3', []],
  ]);
  assert.match(document.getElementById('c-2').textContent, /Approved/);
  assert.match(document.getElementById('c-4').textContent, /New/);
});

test('a filter that hides the top-level comment shows the reply in its place with the name of the top-level comment', async () => {
  tab('New').click();
  await settle();
  assert.deepEqual(threads(), [
    ['c-4', []],
    ['c-3', []],
  ]);
  assert.match(document.getElementById('c-4').textContent, /Reply to Ada/);
  assert.doesNotMatch(document.getElementById('c-3').textContent, /Reply to/);
});
