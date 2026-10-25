import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, banner, load, settle, stubSignIn } from './load.mjs';

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

const saves = [];
let saved = 0;

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post')) {
    if (init.method === 'PUT') {
      const headers = new Headers(init.headers);
      saves.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
      saved += 1;
      return answer(200, { success: true, message: null, data: { eTag: `"v${saved + 1}"`, check: { kind: 'found', problems: [] } } });
    }

    return answer(200, { success: true, message: null, data: post });
  }

  return apiAnswer(url);
};

const tokens = ['token-1', 'token-1', 'token-1', 'token-1', 'token-1', null];

const signIn = stubSignIn({
  token: async () => tokens.shift() ?? null,
});

await load('http://localhost:4322/posts/my-post', { respond, signIn });

const status = () => document.querySelector('.editor-status').textContent.trim();
const view = () => document.querySelector('.cm-content').cmTile.view;
const type = (text) => view().dispatch({ changes: { from: view().state.doc.length, insert: text } });
const wait = async (milliseconds) => {
  await new Promise((resolve) => setTimeout(resolve, milliseconds));
  await settle();
};

test('the editor shows the title, the excerpt, the body, and the status text', () => {
  assert.equal(document.querySelector('textarea[aria-label="Title"]').value, 'My post');
  assert.equal(document.querySelector('textarea[aria-label="Excerpt"]').value, 'About it.');
  assert.equal(view().state.doc.toString(), 'Some text.\n');
  assert.equal(status(), 'Draft · saved');
});

test('the editor has no line numbers, no gutter, and the slots of the top bar', () => {
  assert.equal(document.querySelector('.cm-gutters'), null);
  const slots = [...document.querySelectorAll('.editor-actions > button')].map((item) => item.textContent.trim());
  assert.deepEqual(slots, ['Preview', 'Details', 'Publish', '•••']);
  assert.equal(document.querySelector('.editor-bar a').textContent.trim(), 'Posts');
});

test('a save happens 2 seconds after the last input and not before', async () => {
  type('One. ');
  assert.equal(status(), 'Draft · unsaved changes');
  assert.ok(document.querySelector('.editor-bar.is-typing'), 'The top bar fades');

  await wait(1500);
  type('Two. ');
  await wait(1500);
  assert.equal(saves.length, 0, 'The timer starts again at each input');

  await wait(700);
  assert.equal(saves.length, 1);
  assert.equal(saves[0].ifMatch, '"v1"');
  assert.deepEqual(saves[0].body, {
    title: 'My post',
    excerpt: 'About it.',
    body: 'Some text.\nOne. Two. ',
    details: { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true },
  });
  assert.equal(status(), 'Draft · saved');
});

test('the keys Command and S save at once, with the ETag of the last save', async () => {
  type('Three.');
  const event = new KeyboardEvent('keydown', { key: 's', metaKey: true, cancelable: true, bubbles: true });
  window.dispatchEvent(event);
  await settle();
  assert.ok(event.defaultPrevented, 'The browser does not save the page');
  assert.equal(saves.length, 2);
  assert.equal(saves[1].ifMatch, '"v2"');
  assert.match(saves[1].body.body, /Three\.$/);
  assert.equal(status(), 'Draft · saved');
});

test('when the token needs a new sign-in during a save, the text stays and the Sign in again banner shows', async () => {
  type(' Four.');
  const event = new KeyboardEvent('keydown', { key: 's', ctrlKey: true, cancelable: true, bubbles: true });
  window.dispatchEvent(event);
  await settle();
  assert.equal(saves.length, 2, 'The save did not go out');
  assert.match(banner(), /^Your session ended\. Sign in again to continue\./);
  assert.match(view().state.doc.toString(), /Four\.$/);
  assert.equal(status(), 'Draft · saving…');
});
