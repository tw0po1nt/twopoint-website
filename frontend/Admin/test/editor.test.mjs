import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, follow, heading, links, load } from './load.mjs';

const post = { slug: 'hello world', state: 'draft', pendingEdit: false, publishDate: null, eTag: '"v1"', title: 'Hello', excerpt: '', body: '', details: { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true }, text: '', publishedText: null };

const respond = (url) =>
  url.endsWith('/api/internal/posts/hello%20world') ? answer(200, { success: true, message: null, data: post }) : apiAnswer(url);

await load('http://localhost:4322/posts/hello%20world', { respond });

test('the editor of a post shows the slug and no navigation', () => {
  assert.equal(heading(), 'hello world');
  assert.deepEqual(links(), []);
  assert.ok(document.querySelector('.shell.is-bare'), 'The layout has one column');
});

test('the Posts link of the editor goes back to the posts with the navigation', () => {
  follow('Posts');
  assert.equal(location.pathname, '/posts');
  assert.equal(heading(), 'Posts');
  assert.equal(links().find((link) => link.current === 'page').text, 'Posts');
});
