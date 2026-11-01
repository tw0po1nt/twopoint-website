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

test('with no View Transitions, the editor opens in the light from the screen center', async () => {
  assert.equal(typeof document.startViewTransition, 'undefined', 'happy-dom has no View Transitions');

  follow('First');

  assert.equal(transition(), 'alone-in');
  assert.ok(document.querySelector('.lamp-glow'), 'The warm edge of the light');
  assert.equal(document.documentElement.style.getPropertyValue('--lamp-x'), `${window.innerWidth / 2}px`);

  await settle();
  assert.equal(location.pathname, '/posts/first');
  assert.equal(heading(), 'first');
});

test('with no View Transitions, Posts goes back, and the admin fades in', async () => {
  follow('Posts');

  assert.equal(transition(), 'alone-out');
  assert.equal(document.querySelector('.lamp-glow'), null, 'No light on a back');

  await settle();
  assert.equal(location.pathname, '/posts');
  assert.equal(heading(), 'Posts');
});
