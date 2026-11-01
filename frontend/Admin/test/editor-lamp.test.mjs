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
const nameOf = (selector) => document.querySelector(selector)?.style.getPropertyValue('view-transition-name') ?? null;

// A stub of the View Transitions API: the browser calls the update later, after the snapshot of the old screen.
const transitions = [];
document.startViewTransition = (update) => {
  const record = { before: { path: location.pathname, glow: nameOf('.lamp-glow'), text: nameOf('.editor-screen') } };
  transitions.push(record);

  const done = Promise.resolve()
    .then(update)
    .then(() => {
      record.after = { path: location.pathname, text: nameOf('.editor-screen') };
    });

  return { ready: done, finished: done, updateCallbackDone: done };
};

test('a post opens in the light, and the text of the editor comes in as its own part', async () => {
  follow('First');

  assert.equal(transition(), 'lamp-in');
  assert.deepEqual(transitions[0].before, { path: '/series', glow: 'lamp-glow', text: null });

  await settle();
  assert.deepEqual(transitions[0].after, { path: '/posts/first', text: 'paper-text' });
  assert.equal(heading(), 'first');
});

test('after the transition, nothing of the light stays', () => {
  assert.equal(transition(), null);
  assert.equal(document.querySelector('.lamp-glow'), null);
  assert.equal(document.querySelectorAll("[style*='view-transition-name']").length, 0);
});

test('Posts takes the light back, and the text of the editor leaves as its own part', async () => {
  follow('Posts');

  assert.equal(transition(), 'lamp-out');
  assert.equal(transitions[1].before.text, 'paper-text');
  assert.equal(transitions[1].before.path, '/posts/first', 'The route changes in the update');

  await settle();
  assert.equal(transitions[1].after.path, '/posts');
  assert.equal(heading(), 'Posts');
  assert.equal(transition(), null);
});
