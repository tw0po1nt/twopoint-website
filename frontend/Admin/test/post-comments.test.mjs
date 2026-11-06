import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, expectedComment, heading, load, press, queue, rendered, settle, shownComment } from './load.mjs';

const comment = (id, name, status, content) => ({
  id,
  createdDate: '2026-02-04T00:00:00Z',
  commenter: { id: `${name.toLowerCase()}-id`, email: `${name.toLowerCase()}@example.com`, name, status: 'new', createdDate: '2026-01-01T00:00:00Z' },
  status,
  content,
});

const comments = [
  comment('c-1', 'Ada', 'new', 'The first comment.'),
  comment('c-4', 'Grace', 'approved', rendered('An approved comment.')),
  comment('c-5', 'Alan', 'rejected', 'A rejected comment.'),
];

const reads = [];
const actions = [];

const respond = (url, init) => {
  if (url.endsWith('/api/internal/posts/first/comments')) {
    reads.push(url.replace(/^.*\/api\//, ''));
    return answer(200, { success: true, message: null, data: comments });
  }

  if (url.includes('/api/internal/posts/')) {
    actions.push({ method: init.method, url: url.replace(/^.*\/api\//, ''), body: init.body ?? null });
    return answer(200, { success: true, message: null, data: null });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/first/comments', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const row = (id) => document.getElementById(id);
const buttons = (id) => [...row(id).querySelectorAll('.actions button')].map((item) => item.textContent.trim());
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);
const navCount = () => count('nav.side-nav a[href="/comments"]');

test('the page lists each comment of the post with its approval', () => {
  assert.equal(heading(), 'Comments');
  const title = document.querySelector('main .page-sub a');
  assert.deepEqual([title.textContent.trim(), title.getAttribute('href')], ['First', '/posts/first']);
  assert.deepEqual(reads, ['internal/posts/first/comments']);
  assert.deepEqual(rows(), ['c-1', 'c-4', 'c-5']);
  assert.match(row('c-4').textContent, /Grace/);
  assert.match(row('c-4').textContent, /Approved/);
  assert.match(row('c-4').textContent, /An approved comment\./);
  assert.equal(tab('All').getAttribute('aria-selected'), 'true');
  assert.equal(navCount(), String(queue.length));
});

test('a comment row of the post shows the rendered HTML of the comment', () => {
  assert.deepEqual(shownComment(row('c-4')), expectedComment);
});

test('each row offers only the changes that its approval allows', () => {
  assert.deepEqual(buttons('c-1'), ['Approve', 'Reject', 'Delete']);
  assert.deepEqual(buttons('c-4'), ['Reject', 'Delete']);
  assert.deepEqual(buttons('c-5'), ['Approve', 'Delete']);
});

test('the filter shows only the comments with its approval', async () => {
  tab('Approved').click();
  await settle();
  assert.equal(tab('Approved').getAttribute('aria-selected'), 'true');
  assert.deepEqual(rows(), ['c-4']);

  tab('Rejected').click();
  await settle();
  assert.deepEqual(rows(), ['c-5']);

  tab('New').click();
  await settle();
  assert.deepEqual(rows(), ['c-1']);
  assert.equal(reads.length, 1, 'The filter calls no API');
});

test('Approve sends the approval, and the comment moves to the approved comments and out of the queue', async () => {
  press('Approve', row('c-1'));
  await settle();
  assert.deepEqual(actions, [{ method: 'PUT', url: 'internal/posts/first/comments/c-1', body: '{"approval":"approved"}' }]);
  assert.match(document.querySelector('main .empty').textContent, /No new comments\./);
  assert.equal(navCount(), String(queue.length - 1));

  tab('Approved').click();
  await settle();
  assert.deepEqual(rows(), ['c-1', 'c-4']);
});

test('Delete in the confirmation deletes the comment', async () => {
  tab('All').click();
  await settle();
  press('Delete', row('c-5'));
  await settle();
  const dialog = document.getElementById('delete-comment');
  assert.equal(dialog.open, true);

  press('Delete', dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(actions[1], { method: 'DELETE', url: 'internal/posts/first/comments/c-5', body: null });
  assert.deepEqual(rows(), ['c-1', 'c-4']);
});
