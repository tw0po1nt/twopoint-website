import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

// The link of a reply email: the query names the top-level comment, and the fragment names the reply.
GlobalRegistrator.register({ url: 'http://localhost:4321/posts/a-post/?thread=a#comment-a1' });

document.body.innerHTML = `
  <details id="comments"><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post" data-author="Matthew Watt"><p>static</p></div></details>`;

const scrolled = [];
Element.prototype.scrollIntoView = function () {
  scrolled.push(this.id);
};

const at = (id, day, replyTo, writer) => ({
  id,
  createdDate: `2026-10-${String(day).padStart(2, '0')}T09:30:00.0000000Z`,
  commenter: writer === 'author' ? '' : `Reader ${id}`,
  content: `<p>${id}</p>\n`,
  replyTo,
  writer,
});

const comments = [
  at('a', 1, null, 'commenter'),
  at('a1', 2, 'a', 'commenter'),
  at('a2', 3, 'a', 'author'),
  at('a3', 4, 'a', 'commenter'),
  at('a4', 5, 'a', 'commenter'),
  at('b', 6, null, 'commenter'),
  at('b1', 7, 'b', 'commenter'),
  at('b2', 8, 'b', 'commenter'),
  at('b3', 9, 'b', 'author'),
  at('b4', 10, 'b', 'commenter'),
];

globalThis.fetch = async () =>
  new Response(JSON.stringify({ success: true, message: null, data: comments }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

await import(pathToFileURL(path.resolve('dist', manifest.entries.comments.file)).href);
await new Promise((resolve) => setTimeout(resolve, 50));

const mount = document.getElementById('good');
const ids = (thread) => [...thread.querySelectorAll('.comment')].map((comment) => comment.id);
const [threadA, threadB] = mount.querySelectorAll('.comment-thread');

test('a comment link opens the panel, opens the earlier replies that hide the comment, and scrolls to it with a mark', () => {
  assert.equal(document.getElementById('comments').open, true, 'The panel is open');
  assert.deepEqual(ids(threadA), ['comment-a', 'comment-a1', 'comment-a2', 'comment-a3', 'comment-a4']);
  assert.equal(threadA.querySelector('.comment-earlier'), null, 'No control for the open thread');
  assert.deepEqual(scrolled, ['comment-a1'], 'One scroll to the reply');
  assert.ok(document.getElementById('comment-a1').classList.contains('is-marked'), 'The reply has the mark');
});

test('a thread with more than 3 replies shows the 2 newest and [show N earlier replies]', () => {
  assert.deepEqual(ids(threadB), ['comment-b', 'comment-b3', 'comment-b4']);
  assert.equal(threadB.querySelector('.comment-earlier button').textContent, '[show 2 earlier replies]');
});

test('a reply from the author shows the author name of the post, the badge, and its own line', () => {
  const reply = document.getElementById('comment-a2');
  assert.equal(reply.className, 'comment is-author');
  assert.equal(reply.querySelector('.comment-meta').textContent, '2026-10-03 · Matthew Watt [author]');
  assert.equal(reply.querySelector('.comment-badge').textContent, ' [author]');

  const other = document.getElementById('comment-a1');
  assert.equal(other.className.includes('is-author'), false);
  assert.equal(other.querySelector('.comment-meta').textContent, '2026-10-02 · Reader a1');
});

test('[show N earlier replies] shows each reply of the thread', async () => {
  threadB.querySelector('.comment-earlier button').click();
  await new Promise((resolve) => setTimeout(resolve, 20));
  const [, thread] = mount.querySelectorAll('.comment-thread');
  assert.deepEqual(ids(thread), ['comment-b', 'comment-b1', 'comment-b2', 'comment-b3', 'comment-b4']);
  assert.equal(thread.querySelector('.comment-earlier'), null, 'The control is gone');
});

test('typing in the form keeps the nodes of the threads', async () => {
  const before = mount.querySelector('.comment-thread');
  const box = mount.querySelector('[name="comment"]');
  box.value = 'Hi';
  box.dispatchEvent(new Event('input', { bubbles: true }));
  await new Promise((resolve) => setTimeout(resolve, 20));
  assert.equal(mount.querySelector('.comment-thread'), before, 'The same node');
});
