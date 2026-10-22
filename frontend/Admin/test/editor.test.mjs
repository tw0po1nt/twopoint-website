import assert from 'node:assert/strict';
import { test } from 'node:test';

import { follow, heading, links, load } from './load.mjs';

await load('http://localhost:4322/posts/hello%20world');

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
