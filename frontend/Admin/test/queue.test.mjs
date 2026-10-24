import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, heading, load, press, settle, spyOnShowModal } from './load.mjs';

const actions = [];
const replies = [
  answer(200, { success: true, message: null, data: null }),
  answer(409, { success: false, message: 'Comment is approved and cannot become rejected', data: null }),
  answer(200, { success: true, message: null, data: null }),
];

const respond = (url, init) => {
  if (url.includes('/api/internal/posts/')) {
    actions.push({ method: init.method, url: url.replace(/^.*\/api\//, ''), body: init.body ?? null });
    return replies.shift();
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/comments', { respond });
const shown = spyOnShowModal();

const rows = () => [...document.querySelectorAll('main .queue-row')];
const row = (id) => document.getElementById(id);
const dialog = () => document.getElementById('delete-comment');
const navCount = () => count('nav.side-nav a[href="/comments"]');

test('the queue shows each new comment with the post, the commenter, the status, and the text', () => {
  assert.equal(heading(), 'Moderation queue');
  assert.deepEqual(rows().map((item) => item.id), ['c-1', 'c-2', 'c-3']);

  const text = row('c-2').textContent;
  assert.match(text, /Grace/);
  assert.match(text, /grace@example\.com/);
  assert.match(text, /Trusted/);
  assert.match(text, /On First/);
  assert.match(text, /The second comment\./);
  assert.equal(navCount(), '3');
});

test('Approve sends the approval, removes the row, and lowers the navigation count', async () => {
  press('Approve', row('c-1'));
  await settle();
  assert.deepEqual(actions, [
    { method: 'PUT', url: 'internal/posts/first/comments/c-1', body: '{"approval":"approved"}' },
  ]);
  assert.equal(row('c-1'), null);
  assert.equal(navCount(), '2');
});

test('a refused change keeps the row and shows the reason', async () => {
  press('Reject', row('c-2'));
  await settle();
  assert.deepEqual(actions[1], { method: 'PUT', url: 'internal/posts/first/comments/c-2', body: '{"approval":"rejected"}' });
  assert.match(row('c-2').querySelector('.alert').textContent, /The rejection did not complete\. Comment is approved/);
  assert.equal(navCount(), '2');
});

test('Delete asks for a confirmation, and Cancel keeps the comment', async () => {
  press('Delete', row('c-3'));
  await settle();
  assert.deepEqual(shown, ['delete-comment']);
  assert.equal(dialog().open, true);
  assert.equal(actions.length, 2, 'No delete before the confirmation');

  press('Cancel', dialog());
  await settle();
  assert.equal(dialog().open, false);
  assert.notEqual(row('c-3'), null);
});

test('Delete in the confirmation deletes the comment and lowers the navigation count', async () => {
  press('Delete', row('c-3'));
  await settle();
  press('Delete', dialog());
  await settle();
  assert.equal(dialog().open, false);
  assert.deepEqual(actions[2], { method: 'DELETE', url: 'internal/posts/second/comments/c-3', body: null });
  assert.equal(row('c-3'), null);
  assert.equal(navCount(), '1');
});
