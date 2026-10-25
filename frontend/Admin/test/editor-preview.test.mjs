import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle, stubSignIn } from './load.mjs';

const post = {
  slug: 'my-post',
  state: 'draft',
  pendingEdit: false,
  publishDate: null,
  eTag: '"v1"',
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true },
  text: '',
  publishedText: null,
};

const article = {
  content: '<dl class="front-matter"></dl><h1>My post</h1><div class="prose"><p>Some text.</p></div>',
  outline: '<aside class="outline" aria-label="Outline"></aside>',
};
const previews = [];
let previewAnswer = { article, problems: [] };

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/preview')) {
    const headers = new Headers(init.headers);
    previews.push({ method: init.method, authorization: headers.get('Authorization'), body: JSON.parse(init.body) });
    return answer(200, { success: true, message: null, data: previewAnswer });
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return answer(200, { success: true, message: null, data: post });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond, signIn: stubSignIn() });

const view = () => document.querySelector('.cm-content').cmTile.view;
const column = () => document.querySelector('.editor-column');
const frame = () => document.querySelector('.preview-frame');
const button = () => [...document.querySelectorAll('.editor-bar button')].find((item) => item.textContent.trim() === 'Preview');
const escape = () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

test('Preview sends the text of the editor, also before a save, and shows the article in the place of the text', async () => {
  view().dispatch({ changes: { from: view().state.doc.length, insert: 'Unsaved text.\n' } });
  press('Preview');
  await settle();

  assert.equal(previews.length, 1);
  assert.equal(previews[0].method, 'POST');
  assert.equal(previews[0].authorization, 'Bearer token-1');
  assert.deepEqual(previews[0].body, { title: 'My post', excerpt: 'About it.', body: 'Some text.\nUnsaved text.\n' });

  assert.ok(column().classList.contains('is-hidden'), 'The text is out of sight');
  assert.equal(button().getAttribute('aria-pressed'), 'true');
  assert.equal(frame().getAttribute('sandbox'), 'allow-same-origin', 'No script runs in the frame');
  const document_ = frame().getAttribute('srcdoc');
  assert.match(document_, /<link rel="stylesheet" href="\/preview-styles\/site\.css">/);
  assert.match(document_, /<main class="post-page"><article class="post"><dl class="front-matter">/);
  assert.match(document_, /<\/article><aside class="outline"/);
  assert.equal(document.querySelector('.preview-problems'), null, 'No problem');
});

test('Esc goes back to the text, and the editor keeps the text and the cursor', async () => {
  escape();
  await settle();

  assert.equal(frame(), null);
  assert.ok(!column().classList.contains('is-hidden'));
  assert.equal(button().getAttribute('aria-pressed'), 'false');
  assert.equal(view().state.doc.toString(), 'Some text.\nUnsaved text.\n');
});

test('the problems of the text show with their place, and a text with an error shows no article', async () => {
  previewAnswer = {
    article: null,
    problems: [
      { severity: 'error', place: 'Body line 4', message: 'The output has no page at /posts/missing/.' },
      { severity: 'warning', place: 'Field tags', message: 'A tag has no posts.' },
      { severity: 'error', place: null, message: 'The change breaks a link.' },
    ],
  };

  press('Preview');
  await settle();

  assert.equal(previews.length, 2);
  assert.equal(frame(), null, 'No article');
  const problems = [...document.querySelectorAll('.preview-problem')].map((item) => item.textContent.trim());
  assert.deepEqual(problems, ['Error Body line 4: The output has no page at /posts/missing/.', 'Warning Field tags: A tag has no posts.', 'Error The change breaks a link.']);

  button().click();
  await settle();
  assert.equal(document.querySelector('.preview-problems'), null, 'The button goes back too');
  assert.ok(!column().classList.contains('is-hidden'));
});

test('Esc with no preview open does nothing', async () => {
  escape();
  await settle();
  assert.ok(!column().classList.contains('is-hidden'));
  assert.equal(view().state.doc.toString(), 'Some text.\nUnsaved text.\n');
});
