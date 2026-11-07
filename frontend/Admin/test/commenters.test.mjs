import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, expectedComment, follow, heading, load, press, queue, rendered, settle, shownComment } from './load.mjs';

const commenter = (name, status, createdDate) => ({
  id: `${name.toLowerCase()}-id`,
  email: `${name.toLowerCase()}@example.com`,
  name,
  status,
  createdDate,
});

const ada = commenter('Ada', 'new', '2026-01-03T00:00:00Z');
const commenters = [ada, commenter('Grace', 'trusted', '2026-01-02T00:00:00Z'), commenter('Alan', 'banned', '2026-01-01T00:00:00Z')];

const posted = (id, approval, content) => ({
  postSlug: 'first',
  postTitle: 'First',
  comment: { id, createdDate: '2026-02-04T00:00:00Z', commenter: ada, status: approval, content, replyCount: 0 },
});

const page = { commenter: ada, comments: [posted('c-1', 'new', 'The first comment.'), posted('c-6', 'approved', rendered('An old comment.'))], otherReplies: 2 };

const reads = [];
const actions = [];

const respond = (url, init) => {
  const path = url.replace(/^.*\/api\//, '');

  if (init.method === 'PUT' || init.method === 'DELETE') {
    actions.push({ method: init.method, url: path, body: init.body ?? null });
    return answer(200, { success: true, message: null, data: null });
  }

  if (path === 'internal/commenters') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: commenters });
  }

  if (path === 'internal/commenters/ada-id') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: page });
  }

  if (path === 'internal/comments') {
    reads.push(path);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/comments', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);
const buttons = () => [...document.querySelectorAll('main .commenter .actions button')].map((item) => item.textContent.trim());
const approvals = () => ['c-1', 'c-6'].map((id) => document.getElementById(id).querySelector('.badge').textContent.trim());

test('the name on a queue row opens the commenter page', async () => {
  follow('Ada', document.getElementById(queue[0].comment.id));
  await settle();

  assert.equal(location.pathname, '/commenters/ada-id');
  assert.equal(heading(), 'Commenter');
  assert.deepEqual(reads.slice(-1), ['internal/commenters/ada-id']);
  assert.match(document.querySelector('main .commenter').textContent, /ada@example\.com/);
  assert.match(document.querySelector('main .commenter').textContent, /2026-01-03/);
  assert.deepEqual(buttons(), ['Trust', 'Ban', 'Delete']);
  assert.deepEqual(rows().slice(1), ['c-1', 'c-6']);
  assert.deepEqual(shownComment(document.getElementById('c-6')), expectedComment);
});

test('Ban bans the commenter, rejects the new comment, and loads the queue again', async () => {
  const before = reads.filter((path) => path === 'internal/comments').length;
  press('Ban');
  await settle();

  assert.deepEqual(actions, [{ method: 'PUT', url: 'internal/commenters/ada-id', body: '{"status":"banned"}' }]);
  assert.match(document.querySelector('main .commenter').textContent, /Banned/);
  assert.deepEqual(buttons(), ['Unban', 'Delete']);
  assert.deepEqual(approvals(), ['Rejected', 'Approved']);
  assert.equal(reads.filter((path) => path === 'internal/comments').length, before + 1);
});

test('Delete in the confirmation deletes the commenter and opens the commenter list', async () => {
  press('Delete', document.querySelector('main .commenter'));
  await settle();
  const dialog = document.getElementById('delete-commenter');
  assert.equal(dialog.open, true);
  assert.match(dialog.textContent, /all their comments, and 2 other replies in their threads\./);

  press('Delete', dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(actions[1], { method: 'DELETE', url: 'internal/commenters/ada-id', body: null });
  assert.equal(location.pathname, '/commenters');
  assert.equal(heading(), 'Commenters');
});

test('the commenter list shows each commenter, and the filter shows only the commenters with its status', async () => {
  assert.deepEqual(rows(), ['ada-id', 'grace-id', 'alan-id']);
  assert.match(document.getElementById('grace-id').textContent, /Verified: 2026-01-02/);
  assert.equal(tab('All').getAttribute('aria-selected'), 'true');

  tab('Banned').click();
  await settle();
  assert.deepEqual(rows(), ['alan-id']);

  tab('Trusted').click();
  await settle();
  assert.deepEqual(rows(), ['grace-id']);
  assert.equal(reads.filter((path) => path === 'internal/commenters').length, 1, 'The filter calls no API');
});
