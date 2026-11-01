import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, follow, heading, load, settle } from './load.mjs';

const series = [
  { slug: 'notes', title: 'Notes', excerpt: 'Short notes', image: null, eTag: '"notes-1"', posts: [{ slug: 'first', title: 'First', state: 'published', publishDate: '2026-01-02T00:00:00Z' }] },
];

const post = { slug: 'first', state: 'published', pendingEdit: false, publishDate: '2026-01-02T00:00:00Z', eTag: '"v1"', title: 'First', excerpt: '', body: '', details: { author: null, category: null, tags: [], series: 'notes', image: null, showOnHomepage: true }, text: '', publishedText: null };

const respond = (url) => {
  if (url.endsWith('/api/internal/series')) {
    return answer(200, { success: true, message: null, data: series });
  }

  if (url.endsWith('/api/internal/posts/first')) {
    return answer(200, { success: true, message: null, data: post });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/series', { respond });

const transition = () => document.documentElement.getAttribute('data-transition');

const media = window.matchMedia.bind(window);
window.matchMedia = (query) => (query === '(prefers-reduced-motion: reduce)' ? { ...media(query), matches: true } : media(query));

document.startViewTransition = (update) => {
  const done = Promise.resolve().then(update);
  return { ready: done, finished: done, updateCallbackDone: done };
};

test('with reduce motion, the move is a cross-fade with no light', async () => {
  follow('First');

  assert.equal(transition(), 'fade');
  assert.equal(document.querySelector('.lamp-glow'), null);

  await settle();
  assert.equal(heading(), 'first');
});

test('after the cross-fade, the transition mark goes away', () => {
  assert.equal(transition(), null);
});
