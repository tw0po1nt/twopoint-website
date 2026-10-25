import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, heading, load, settle, stubPush } from './load.mjs';

const queueUrl = '/api/internal/comments';
const waiting = [];
let queueAnswers = 0;

const respond = (url) => {
  if (url.endsWith(queueUrl)) {
    queueAnswers += 1;
    return answer(200, { success: true, message: null, data: waiting });
  }

  if (url.includes('/api/internal/posts/') && url.endsWith('/comments')) {
    return answer(200, { success: true, message: null, data: waiting.map((item) => item.comment) });
  }

  return apiAnswer(url);
};

const queued = (id) => ({
  postSlug: 'first',
  postTitle: 'First',
  comment: {
    id,
    createdDate: '2026-02-04T00:00:00Z',
    commenter: { id: 'ada-id', email: 'ada@example.com', name: 'Ada', status: 'new', createdDate: '2026-01-01T00:00:00Z' },
    status: 'new',
    content: 'A comment.',
  },
});

const scrolled = [];

let newComment;
let openTarget;
const push = stubPush({
  onNewComment: (listener) => {
    newComment = listener;
  },
  onOpenRequest: (listener) => {
    openTarget = listener;
  },
});

waiting.push(queued('c-1'));

await load('http://localhost:4322/comments', { respond, push });

Element.prototype.scrollIntoView = function () {
  scrolled.push(this.id);
};

const badges = () => push.events.filter((event) => event.startsWith('setBadge:'));

test('the page sets the badge to the count of the moderation queue', () => {
  assert.deepEqual(badges(), ['setBadge:1']);
  assert.equal(count('nav.side-nav a[href="/comments"]'), '1');
});

test('a new-comment message from the worker loads the queue again, and the badge shows the new count', async () => {
  const before = queueAnswers;
  waiting.push(queued('c-2'));

  newComment();
  await settle();

  assert.equal(queueAnswers, before + 1);
  assert.equal(count('nav.side-nav a[href="/comments"]'), '2');
  assert.deepEqual(badges(), ['setBadge:1', 'setBadge:2']);
});

test('an open request keeps the fragment, shows the row of the comment, and scrolls to it', async () => {
  scrolled.length = 0;

  openTarget('/comments#c-2');
  await settle();

  assert.equal(window.location.pathname + window.location.hash, '/comments#c-2');
  assert.ok(document.getElementById('c-2'));
  assert.deepEqual(scrolled, ['c-2']);
});

test('an open request for the comments of a post scrolls to the comment after the list loads', async () => {
  scrolled.length = 0;

  openTarget('/posts/first/comments#c-1');
  await settle();

  assert.equal(heading(), 'Comments on first');
  assert.ok(document.getElementById('c-1'));
  assert.deepEqual(scrolled, ['c-1']);
});

test('the page clears the badge when the moderation queue is empty', async () => {
  openTarget('/comments');
  await settle();
  waiting.length = 0;

  newComment();
  await settle();

  assert.equal(badges().at(-1), 'setBadge:0');
});
