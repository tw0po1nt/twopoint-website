import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, count, heading, load, settle, stubPush } from './load.mjs';

const queueUrl = '/api/internal/comments';
const waiting = [];
let queueAnswers = 0;

const respond = (url, init) => {
  if (url.endsWith(queueUrl)) {
    queueAnswers += 1;
    return answer(200, { success: true, message: null, data: waiting.length > 0 ? waiting : [] });
  }

  if (url.includes('/api/internal/posts/')) {
    waiting.shift();
    return answer(200, { success: true, message: null, data: null });
  }

  return apiAnswer(url);
};

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

waiting.push(
  JSON.parse(
    '{"postSlug":"first","postTitle":"First","comment":{"id":"c-1","createdDate":"2026-02-04T00:00:00Z","commenter":{"id":"ada-id","email":"ada@example.com","name":"Ada","status":"new","createdDate":"2026-01-01T00:00:00Z"},"status":"new","content":"The first comment."}}',
  ),
);

await load('http://localhost:4322/comments', { respond, push });

test('the page does not clear the badge while the moderation queue has a comment', () => {
  assert.ok(!push.events.includes('clearBadge'));
  assert.equal(count('nav.side-nav a[href="/comments"]'), '1');
});

test('a new-comment message from the worker loads the moderation queue again', async () => {
  const before = queueAnswers;
  waiting.push({ ...waiting[0], comment: { ...waiting[0].comment, id: 'c-2' } });

  newComment();
  await settle();

  assert.equal(queueAnswers, before + 1);
  assert.equal(count('nav.side-nav a[href="/comments"]'), '2');
});

test('an open request from the worker opens the target page, and ignores the fragment', async () => {
  openTarget('/posts/first/comments#c-1');
  await settle();

  assert.equal(heading(), 'Comments on first');
  assert.equal(window.location.pathname, '/posts/first/comments');
});

test('the page clears the badge when the moderation queue is empty', async () => {
  openTarget('/comments#c-1');
  await settle();
  waiting.length = 0;

  newComment();
  await settle();

  assert.ok(push.events.includes('clearBadge'));
});
