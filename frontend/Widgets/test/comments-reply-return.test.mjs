import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

// The link in the validation email of a reply: the widget added the thread, and the backend added the validation ID.
GlobalRegistrator.register({
  url: 'http://localhost:4321/posts/a-post/?email=reader%40example.com&name=Reader&comment=Me%20too&replyTo=a&commenterValidationId=validation-2#comments',
});

document.body.innerHTML = `
  <details id="comments"><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post" data-author="Matthew Watt"><p>static</p></div></details>`;

const scrolled = [];
Element.prototype.scrollIntoView = function () {
  scrolled.push(this.id);
};

const comments = [
  { id: 'a', createdDate: '2026-10-01T09:30:00Z', commenter: 'Ada', content: '<p>a</p>\n', replyTo: null, writer: 'commenter' },
  { id: 'b', createdDate: '2026-10-02T09:30:00Z', commenter: 'Linus', content: '<p>b</p>\n', replyTo: null, writer: 'commenter' },
];

globalThis.fetch = async () =>
  new Response(JSON.stringify({ success: true, message: null, data: comments }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

await import(pathToFileURL(path.resolve('dist', manifest.entries.comments.file)).href);
await new Promise((resolve) => setTimeout(resolve, 50));

const mount = document.getElementById('good');
const form = () => mount.querySelector('.comment-reply-form');

test('the return of a reply opens the reply form of its thread with the text, and scrolls to it', () => {
  assert.equal(document.getElementById('comments').open, true, 'The panel is open');
  assert.equal(form().id, 'reply-a');
  assert.equal(form().querySelector('textarea[name="reply"]').value, 'Me too');
  assert.deepEqual(scrolled, ['reply-a'], 'One scroll to the reply form');
});

test('the reply form shows the verified line and tells the reader to post', () => {
  assert.equal(form().querySelector('.comment-note + .comment-note').textContent, 'as Reader (reader@example.com) [change]');
  assert.equal(form().querySelector('.comment-status').textContent, 'Email verified. Click [post reply] to post.');
});

test('the page form stays empty, and the URL loses its query', () => {
  assert.equal(mount.querySelector('textarea[name="comment"]').value, '');
  assert.equal(mount.querySelector('form.comment-form:not(.comment-reply-form) .comment-status').textContent, '');
  assert.equal(location.href, 'http://localhost:4321/posts/a-post/#comments');
});
