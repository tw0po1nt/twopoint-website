import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, follow, heading, load, settle } from './load.mjs';

const post = { slug: 'first', state: 'published', pendingEdit: false, publishDate: '2026-01-02T00:00:00Z', eTag: '"v1"', title: 'First', excerpt: '', body: '', details: { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true }, text: '', publishedText: null };

const respond = (url) => {
  if (url.endsWith('/api/internal/posts/first')) {
    return answer(200, { success: true, message: null, data: post });
  }

  if (url.endsWith('/api/internal/posts/first/comments')) {
    return answer(200, { success: true, message: null, data: [] });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts', { respond });

const card = () => document.getElementById('first');

test('the title of a post card is the link of the card to the editor', () => {
  const links = [...card().querySelectorAll('a.row-link')];
  assert.equal(links.length, 1);
  assert.equal(links[0].textContent.trim(), 'First');
  assert.equal(links[0].getAttribute('href'), '/posts/first');
});

test('a click on the link of a post card opens the post in the editor', async () => {
  follow('First', card());
  await settle();
  assert.equal(location.pathname, '/posts/first');
  assert.ok(document.querySelector('.shell.is-bare'), 'The editor has no navigation');
});

test('a click on the Comments link of a post card opens the comments of the post', async () => {
  const back = new Promise((resolve) => window.addEventListener('popstate', resolve, { once: true }));
  history.back();
  await back;
  await settle();
  assert.equal(location.pathname, '/posts');

  follow('Comments', card());
  await settle();
  assert.equal(location.pathname, '/posts/first/comments');
  assert.equal(heading(), 'Comments on first');
});
