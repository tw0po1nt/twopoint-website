import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, queue, settle } from './load.mjs';

const topLevel = (status) => ({
  id: 'c-0',
  commenter: { id: 'grace-id', email: 'grace@example.com', name: 'Grace', status: 'trusted', createdDate: '2026-01-01T00:00:00Z' },
  writer: 'commenter',
  status,
  excerpt: 'Why does List.map keep the order?',
  content: '<p>Why does <code>List.map</code> keep the order?</p>',
});

const reply = (id, status) => ({
  ...queue[0],
  comment: { ...queue[0].comment, id, content: '<p>It keeps it.</p>', replyTo: 'c-0', topLevel: topLevel(status) },
});

const comments = [reply('c-7', 'approved'), queue[1], reply('c-8', 'rejected')];

const respond = (url) => {
  if (url.endsWith('/api/internal/comments')) {
    return answer(200, { success: true, message: null, data: comments });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/comments', { respond });

const row = (id) => document.getElementById(id);
const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const context = (id) => row(id).querySelector('.reply-context');
const buttons = (id) => [...row(id).querySelectorAll('.actions button')].map((item) => item.textContent.trim());

test('the queue stays oldest first, and a reply row names its top-level comment with an excerpt', () => {
  assert.deepEqual(rows(), ['c-7', 'c-2', 'c-8']);
  assert.match(context('c-7').textContent, /Reply to Grace/);
  assert.equal(context('c-7').querySelector('button.reply-excerpt').textContent.trim(), 'Why does List.map keep the order?');
  assert.equal(context('c-2'), null);
});

test('the excerpt opens the full HTML of the top-level comment, and Show less closes it', async () => {
  context('c-7').querySelector('button.reply-excerpt').click();
  await settle();
  assert.equal(context('c-7').querySelector('.queue-text code').textContent, 'List.map');
  assert.equal(context('c-8').querySelector('.queue-text'), null, 'Only the clicked row opens');

  press('Show less', context('c-7'));
  await settle();
  assert.equal(context('c-7').querySelector('.queue-text'), null);
});

test('a reply under a rejected top-level comment says that readers will not see it, and offers no reply', () => {
  assert.match(context('c-8').textContent, /Readers won't see this reply, because Grace's comment is rejected\./);
  assert.doesNotMatch(context('c-7').textContent, /Readers won't see/);
  assert.ok(!buttons('c-8').includes('Reply and approve'));
  assert.ok(buttons('c-7').includes('Reply and approve'));
});
