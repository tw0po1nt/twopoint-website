import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, queue } from './load.mjs';

const unknownStatus = queue.map((item) => ({ ...item, comment: { ...item.comment, commenter: { ...item.comment.commenter, status: 'Trusted' } } }));

const respond = (url) =>
  url.endsWith('/api/internal/comments') ? answer(200, { success: true, message: null, data: unknownStatus }) : apiAnswer(url);

await load('http://localhost:4322/comments', { respond });

test('a commenter status that the admin does not know stops the queue and shows the reason', () => {
  assert.equal(document.querySelectorAll('main .queue-row').length, 0);
  const alert = document.querySelector('main .alert').textContent;
  assert.match(alert, /Couldn't load the moderation queue\./);
  assert.match(alert, /The server sent an unexpected response\./);
});
