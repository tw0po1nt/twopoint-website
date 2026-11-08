import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle } from './load.mjs';

const ada = { id: 'ada-id', email: 'ada@example.com', name: 'Ada', status: 'trusted', createdDate: '2026-01-03T00:00:00Z' };

const topLevel = (status) => ({
  id: 'c-0',
  commenter: { id: 'grace-id', email: 'grace@example.com', name: 'Grace', status: 'trusted', createdDate: '2026-01-01T00:00:00Z' },
  writer: 'commenter',
  status,
  excerpt: 'Why does List.map keep the order?',
  content: '<p>Why does <code>List.map</code> keep the order?</p>',
  getsReplyEmails: false,
});

const posted = (id, comment) => ({
  postSlug: 'first',
  postTitle: 'First',
  comment: { id, createdDate: '2026-02-04T00:00:00Z', commenter: ada, writer: 'commenter', status: 'approved', content: '<p>It keeps it.</p>', replyCount: 0, getsReplyEmails: false, ...comment },
});

const page = {
  commenter: ada,
  comments: [
    posted('c-7', { replyTo: 'c-0', topLevel: topLevel('approved') }),
    posted('c-2', { content: '<p>A top-level comment.</p>' }),
    posted('c-8', { replyTo: 'c-0', topLevel: topLevel('rejected') }),
  ],
  otherReplies: 0,
};

const respond = (url) => {
  if (url.endsWith('/api/internal/commenters/ada-id')) {
    return answer(200, { success: true, message: null, data: page });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/commenters/ada-id', { respond });

const row = (id) => document.getElementById(id);
const rows = () => [...document.querySelectorAll('main .queue-row:not(.commenter)')].map((item) => item.id);
const context = (id) => row(id).querySelector('.reply-context');

test('the commenter page stays a flat list, and a reply row names its top-level comment with an excerpt', () => {
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
  assert.ok(context('c-7').querySelector('button.reply-excerpt'));
});

test('a reply under a rejected top-level comment says that readers will not see it', () => {
  assert.match(context('c-8').textContent, /Readers won't see this reply, because Grace's comment is rejected\./);
  assert.doesNotMatch(context('c-7').textContent, /Readers won't see/);
});
