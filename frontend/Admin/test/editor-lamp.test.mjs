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

await load('http://localhost:4322/posts', { respond });
follow('Series');
await settle();

const transition = () => document.documentElement.getAttribute('data-transition');
const nameOf = (selector) => document.querySelector(selector)?.style.getPropertyValue('view-transition-name') ?? null;
const lamp = () => ['--lamp-x', '--lamp-y'].map((property) => document.documentElement.style.getPropertyValue(property));

// happy-dom has no layout, so a test gives the box of each element that the light aims at.
const boxes = new Map();
const layout = Element.prototype.getBoundingClientRect;
Element.prototype.getBoundingClientRect = function () {
  const box = [...boxes].find(([selector]) => this.matches(selector))?.[1];
  return box ? { ...box, x: box.left, y: box.top, right: box.left + box.width, bottom: box.top + box.height } : layout.call(this);
};

const tap = (x, y) => document.body.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, clientX: x, clientY: y }));

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

test('Posts takes the light back into the row of the post, and the text of the editor leaves as its own part', async () => {
  boxes.set('article#first', { left: 100, top: 200, width: 400, height: 80 });
  follow('Posts');

  assert.equal(transition(), 'lamp-out');
  assert.equal(transitions[1].before.text, 'paper-text');
  assert.equal(transitions[1].before.path, '/posts/first', 'The route changes in the update');

  await settle();
  assert.equal(transitions[1].after.path, '/posts');
  assert.equal(heading(), 'Posts');
  assert.deepEqual(lamp(), ['300px', '240px'], 'The center of the row');
  assert.equal(transition(), null);
});

test('the light starts at the tap, and after a key it starts at the focused link', async () => {
  follow('Series');
  await settle();
  tap(10, 20);
  follow('First');
  assert.deepEqual(lamp(), ['10px', '20px'], 'The tap');
  await settle();

  follow('Posts');
  await settle();
  follow('Series');
  await settle();
  boxes.set('a[href="/posts/first"]', { left: 40, top: 60, width: 100, height: 20 });
  tap(10, 20);
  document.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Enter' }));
  document.querySelector('a[href="/posts/first"]').focus();
  follow('First');
  assert.deepEqual(lamp(), ['90px', '70px'], 'The focused link, not the old tap');
  await settle();
});
