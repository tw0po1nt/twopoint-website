import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const post = {
  slug: 'my-post',
  state: 'draft',
  pendingEdit: false,
  publishDate: null,
  eTag: '"v1"',
  title: 'My post',
  excerpt: 'About it.',
  body: 'Intro.\n\nSee below.\n\n![](https://example.com/a.png)\n',
  details: noDetails,
  text: '',
  publishedText: null,
};

const problems = [
  { severity: 'warning', place: { kind: 'body', field: null, line: 5 }, message: 'The image has no alt text.' },
  { severity: 'error', place: { kind: 'field', field: 'category', line: 0 }, message: 'The post has no category.' },
  { severity: 'error', place: { kind: 'field', field: 'title', line: 0 }, message: 'The post has no title.' },
];

let nextCheck = { kind: 'found', problems };
let saved = 0;

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post')) {
    if (init.method === 'PUT') {
      saved += 1;
      return answer(200, { success: true, message: null, data: { eTag: `"v${saved + 1}"`, check: nextCheck } });
    }

    return answer(200, { success: true, message: null, data: post });
  }

  if (url.endsWith('/api/internal/terms')) return answer(200, { success: true, message: null, data: { categories: [], tags: [] } });
  if (url.endsWith('/api/internal/series')) return answer(200, { success: true, message: null, data: [] });

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const view = () => document.querySelector('.cm-content').cmTile.view;
const dots = () => document.querySelectorAll('.editor-actions .problem-dot').length;
const buttonWithDot = () => [...document.querySelectorAll('.editor-actions button')].filter((button) => button.querySelector('.problem-dot')).map((button) => button.textContent.trim());
const sheet = () => document.querySelector('#details-sheet');
const links = () => [...sheet().querySelectorAll('.problem-link')];
const wait = async (milliseconds) => {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
  await settle();
};

const saveNow = async () => {
  view().dispatch({ changes: { from: view().state.doc.length, insert: 'x' } });
  window.dispatchEvent(new KeyboardEvent('keydown', { key: 's', metaKey: true, cancelable: true, bubbles: true }));
  await settle();
};

test('before a save answer, no dot shows and the sheet has no problem list', () => {
  assert.equal(dots(), 0);
  assert.equal(sheet().querySelector('.details-problems'), null);
});

test('a save answer with problems puts a dot on Details and on the primary action, and no error text in the writing column', async () => {
  await saveNow();
  assert.deepEqual(buttonWithDot(), ['Details, has problems', 'Publish, has problems']);
  assert.equal(document.querySelector('.editor-column [role="alert"]'), null);
  assert.doesNotMatch(document.querySelector('.editor-column').textContent, /no alt text/);
});

test('the Details sheet lists each problem, and a problem with a place is a link', () => {
  const items = [...sheet().querySelectorAll('.details-problems li')].map((item) => item.textContent.trim());
  assert.deepEqual(items, [
    'Warning Body line 5: The image has no alt text.',
    'Error Field category: The post has no category.',
    'Error Field title: The post has no title.',
  ]);
  assert.equal(links().length, 3);
});

test('a body problem moves the cursor to its line in the body and focuses the body', async () => {
  document.querySelector('.editor-actions button:nth-of-type(2)').click();
  await settle();
  links()[0].click();
  await wait(20);

  const { state } = view();
  assert.equal(state.doc.lineAt(state.selection.main.head).number, 5);
  assert.equal(state.selection.main.head, state.doc.line(5).from);
  assert.equal(sheet().open, false, 'The sheet closes');
});

test('a field problem of the sheet focuses its control, and a title problem closes the sheet', async () => {
  document.querySelector('.editor-actions button:nth-of-type(2)').click();
  await settle();
  links()[1].click();
  await wait(20);
  assert.equal(document.activeElement.id, 'details-category');
  assert.equal(sheet().open, true, 'The sheet stays open');

  links()[2].click();
  await wait(20);
  assert.equal(document.activeElement.id, 'editor-title');
  assert.equal(sheet().open, false, 'The sheet closes');
});

test('a save answer with no problem takes the dot away', async () => {
  nextCheck = { kind: 'found', problems: [] };
  await saveNow();
  assert.equal(dots(), 0);
});

test('a check that did not run shows a note in the sheet and no dot', async () => {
  nextCheck = { kind: 'unavailable', problems: [] };
  await saveNow();
  assert.equal(dots(), 0);
  assert.match(sheet().textContent, /The check did not run/);
});
