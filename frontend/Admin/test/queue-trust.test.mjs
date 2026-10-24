import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, load, press, queue, settle } from './load.mjs';

// A second comment of Ada shows the trust on its row after the first one.
const later = { ...queue[0], comment: { ...queue[0].comment, id: 'c-4', content: 'A later comment.' } };

const actions = [];
const replies = [
  answer(200, { success: true, message: null, data: null }),
  answer(200, { success: true, message: null, data: null }),
  answer(409, { success: false, message: 'Commenter is banned and cannot become trusted', data: null }),
];

const respond = (url, init) => {
  if (url.endsWith('/api/internal/comments')) {
    return answer(200, { success: true, message: null, data: [...queue, later] });
  }

  if (url.includes('/api/internal/commenters/') || url.includes('/api/internal/posts/')) {
    actions.push({ method: init.method, url: url.replace(/^.*\/api\//, ''), body: init.body ?? null });
    return replies.shift();
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/comments', { respond });

const row = (id) => document.getElementById(id);
const buttons = (id) => [...row(id).querySelectorAll('.actions button')].map((item) => item.textContent.trim());
const navCount = () => count('nav.side-nav a[href="/comments"]');

test('a row of a new commenter offers Approve and trust, and a row of a trusted commenter does not', () => {
  assert.deepEqual(buttons('c-1'), ['Approve', 'Approve and trust', 'Reject', 'Delete']);
  assert.deepEqual(buttons('c-2'), ['Approve', 'Reject', 'Delete']);
});

test('Approve and trust trusts the commenter, then approves the comment and removes the row', async () => {
  press('Approve and trust', row('c-1'));
  await settle();

  assert.deepEqual(actions, [
    { method: 'PUT', url: 'internal/commenters/ada-id', body: '{"status":"trusted"}' },
    { method: 'PUT', url: 'internal/posts/first/comments/c-1', body: '{"approval":"approved"}' },
  ]);
  assert.equal(row('c-1'), null);
  assert.equal(navCount(), '3');
  assert.match(row('c-4').textContent, /Trusted/);
  assert.deepEqual(buttons('c-4'), ['Approve', 'Reject', 'Delete']);
});

test('a refused trust sends no approval, keeps the row, and shows the reason', async () => {
  press('Approve and trust', row('c-3'));
  await settle();

  assert.deepEqual(actions.slice(2), [{ method: 'PUT', url: 'internal/commenters/alan-id', body: '{"status":"trusted"}' }]);
  assert.match(row('c-3').querySelector('.alert').textContent, /The approval and the trust did not complete\. Commenter is banned/);
  assert.equal(navCount(), '3');
});
