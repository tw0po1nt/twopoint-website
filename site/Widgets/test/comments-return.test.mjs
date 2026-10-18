import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

// The link in the validation email adds the validation ID to the redirect URI of the widget.
GlobalRegistrator.register({
  url: 'http://localhost:4321/posts/a-post/?email=reader%40example.com&name=Reader&comment=Nice%20post&commenterValidationId=validation-2#comments',
});

document.body.innerHTML = `
  <details id="panel"><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post"><p>static</p></div></details>`;

localStorage.setItem('commenterValidations', JSON.stringify({ 'known@example.com': 'validation-1' }));

const calls = [];
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return new Response(JSON.stringify({ success: true, message: null, data: [] }), { status: 200 });
};

await import(pathToFileURL(path.resolve('dist', manifest.entries.comments.file)).href);
await new Promise((resolve) => setTimeout(resolve, 20));

const mount = document.getElementById('good');
const field = (name) => mount.querySelector(`[name="${name}"]`);

test('the return from the validation email opens the panel and loads the comments', () => {
  assert.equal(document.getElementById('panel').open, true, 'The panel is open');
  assert.equal(calls.length, 1, 'One fetch');
  assert.equal(mount.querySelector('.comments-count').textContent, '0 comments. Be the first.');
});

test('the form gets the comment back, and the status tells the reader to post it', () => {
  assert.equal(field('email').value, 'reader@example.com');
  assert.equal(field('name').value, 'Reader');
  assert.equal(field('comment').value, 'Nice post');
  assert.match(mount.querySelector('.comment-status').textContent, /Your email address is verified/);
});

test('the widget stores the validation ID beside the others, and the URL loses its query', () => {
  assert.deepEqual(JSON.parse(localStorage.getItem('commenterValidations')), {
    'known@example.com': 'validation-1',
    'reader@example.com': 'validation-2',
  });

  assert.equal(location.href, 'http://localhost:4321/posts/a-post/#comments');
});
