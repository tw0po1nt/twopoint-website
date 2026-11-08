import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, load, press, queue, settle } from './load.mjs';

const later = { ...queue[0], comment: { ...queue[0].comment, id: 'c-4', content: 'A later comment.' } };
const linus = {
  ...queue[2],
  comment: { ...queue[2].comment, id: 'c-5', commenter: { ...queue[2].comment.commenter, id: 'linus-id', name: 'Linus' } },
};

const actions = [];
const replies = [
  answer(200, { success: true, message: null, data: null }),
  answer(200, { success: true, message: null, data: null }),
  answer(409, { success: false, message: 'Commenter is banned and cannot become trusted', data: null }),
  answer(200, { success: true, message: null, data: null }),
  answer(409, { success: false, message: 'Comment is approved and cannot become approved', data: null }),
];

const respond = (url, init) => {
  if (url.endsWith('/api/internal/comments')) {
    return answer(200, { success: true, message: null, data: [...queue, later, linus] });
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
  assert.deepEqual(buttons('c-1'), ['Approve', 'Approve and trust', 'Reject', 'Delete', 'Reply and approve']);
  assert.deepEqual(buttons('c-2'), ['Approve', 'Reject', 'Delete', 'Reply and approve']);
});

test('Approve and trust trusts the commenter, then approves the comment and removes the row', async () => {
  press('Approve and trust', row('c-1'));
  await settle();

  assert.deepEqual(actions, [
    { method: 'PUT', url: 'internal/commenters/ada-id', body: '{"status":"trusted"}' },
    { method: 'PUT', url: 'internal/posts/first/comments/c-1', body: '{"approval":"approved"}' },
  ]);
  assert.equal(row('c-1'), null);
  assert.equal(navCount(), '4');
  assert.match(row('c-4').textContent, /Trusted/);
  assert.deepEqual(buttons('c-4'), ['Approve', 'Reject', 'Delete', 'Reply and approve']);
});

test('a refused trust sends no approval, keeps the row, and shows the reason', async () => {
  press('Approve and trust', row('c-3'));
  await settle();

  assert.deepEqual(actions.slice(2), [{ method: 'PUT', url: 'internal/commenters/alan-id', body: '{"status":"trusted"}' }]);
  assert.match(row('c-3').querySelector('.alert').textContent, /Couldn't trust the commenter, so the comment wasn't approved\. Commenter is banned/);
  assert.equal(navCount(), '4');
});

test('a failed approval after the trust shows the trust and the reason, and keeps the row', async () => {
  press('Approve and trust', row('c-5'));
  await settle();

  assert.deepEqual(actions.slice(3), [
    { method: 'PUT', url: 'internal/commenters/linus-id', body: '{"status":"trusted"}' },
    { method: 'PUT', url: 'internal/posts/second/comments/c-5', body: '{"approval":"approved"}' },
  ]);
  assert.match(row('c-5').querySelector('.alert').textContent, /Couldn't approve the comment\. Comment is approved/);
  assert.match(row('c-5').textContent, /Trusted/);
  assert.deepEqual(buttons('c-5'), ['Approve', 'Reject', 'Delete', 'Reply and approve']);
  assert.equal(navCount(), '4');
});
