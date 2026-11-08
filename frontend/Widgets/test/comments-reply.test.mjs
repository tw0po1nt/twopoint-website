import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/posts/a-post/' });

document.body.innerHTML = `
  <details id="comments" open><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post" data-author="Matthew Watt"><p>static</p></div></details>`;

localStorage.setItem('commenterValidations', JSON.stringify({ 'known@example.com': 'validation-1' }));

const at = (id, day, replyTo, commenter) => ({
  id,
  createdDate: `2026-10-${String(day).padStart(2, '0')}T09:30:00.0000000Z`,
  commenter,
  content: `<p>${id}</p>\n`,
  replyTo,
  writer: 'commenter',
});

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const listUrl = 'https://api.test/api/blog/posts/a-post/comments';

const calls = [];
let answer = () => json(200, { success: true, message: null, data: [at('a', 1, null, 'Ada'), at('a1', 2, 'a', 'Grace'), at('b', 3, null, 'Linus')] });
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answer(String(url), init);
};

await import(pathToFileURL(path.resolve('dist', manifest.entries.comments.file)).href);

const settle = () => new Promise((resolve) => setTimeout(resolve, 20));
await settle();

const mount = document.getElementById('good');
const threads = () => [...mount.querySelectorAll('.comment-thread')];
const replyButton = (thread) => thread.querySelector('.comment-thread-end > button');
const replyForm = () => mount.querySelector('.comment-reply-form');
const inForm = (selector) => replyForm().querySelector(selector);
const posts = () => calls.filter((call) => call.url === listUrl && call.init.method === 'POST');

const type = (input, value) => {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
};

const button = (root, text) => [...root.querySelectorAll('button')].find((element) => element.textContent === text);
const submitReply = () => replyForm().dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

test('each thread ends with one [reply], and no reply form is open', () => {
  assert.deepEqual(threads().map((thread) => replyButton(thread).textContent), ['[reply]', '[reply]']);
  assert.equal(replyForm(), null);
});

test('[reply] opens the reply form in place of the control, with the fields for a new reader', async () => {
  replyButton(threads()[0]).click();
  await settle();

  assert.equal(replyForm().id, 'reply-a');
  assert.equal(threads()[0].querySelector('.comment-thread-end > button'), null, 'The form replaces [reply]');
  assert.equal(inForm('.comment-note').textContent, 'reply to Ada');
  assert.ok(inForm('[name="name"]') && inForm('[name="email"]'), 'The name and email fields');
  assert.deepEqual([...replyForm().querySelectorAll('[role="tab"]')].map((tab) => tab.textContent), ['[write]', '[preview]']);
  assert.ok(inForm('textarea[name="reply"]'), 'The text box of the reply');
  assert.match(inForm('.comment-help').textContent, /Markdown works/);
  assert.ok(button(replyForm(), '[post reply]') && button(replyForm(), '[cancel]'), 'The two actions');
  assert.equal(replyForm().querySelector('input[type="checkbox"]'), null, 'No reply-email checkbox');
});

test('a reply with a known email address posts with its top-level comment, and an approved reply ends its thread', async () => {
  answer = () =>
    json(200, {
      success: true,
      message: null,
      data: null,
      comment: { ...at('a2', 4, 'a', 'Reader'), approval: 'approved' },
    });

  type(inForm('[name="name"]'), 'Reader');
  type(inForm('[name="email"]'), 'known@example.com');
  type(inForm('textarea'), ' Me too ');
  submitReply();
  await settle();

  assert.deepEqual(JSON.parse(posts().at(-1).init.body), { ValidationId: 'validation-1', Comment: 'Me too', ReplyTo: 'a' });
  assert.deepEqual([...threads()[0].querySelectorAll('.comment')].map((comment) => comment.id), ['comment-a', 'comment-a1', 'comment-a2']);
  assert.equal(replyForm(), null, 'The form is closed');
  assert.equal(replyButton(threads()[0]).textContent, '[reply]');
  assert.equal(mount.querySelector('form.comment-form:not(.comment-reply-form) [name="email"]').value, 'known@example.com', 'The page form shares the email address');
});

test('a verified reader gets one line, and [change] opens the fields', async () => {
  replyButton(threads()[1]).click();
  await settle();

  assert.equal(inForm('.comment-note + .comment-note').textContent, 'as Reader (known@example.com) [change]');
  assert.equal(inForm('[name="email"]'), null, 'No fields');

  button(replyForm(), '[change]').click();
  await settle();
  assert.equal(inForm('[name="email"]').value, 'known@example.com', 'The fields have the shared values');
});

test('one reply form is open at a time, and a closed form keeps its text', async () => {
  type(inForm('textarea'), 'A draft');
  replyButton(threads()[0]).click();
  await settle();
  assert.equal(replyForm().id, 'reply-a', 'Only the form of the first thread is open');
  assert.equal(inForm('textarea').value, '', 'The posted reply left no text');

  button(replyForm(), '[cancel]').click();
  await settle();
  assert.equal(replyForm(), null, '[cancel] closes the form');

  replyButton(threads()[1]).click();
  await settle();
  assert.equal(inForm('textarea').value, 'A draft', 'The text comes back');
});

test('a new reply shows nothing, and its thread says that it waits for approval', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  submitReply();
  await settle();

  assert.equal(replyForm(), null, 'The form is closed');
  assert.deepEqual([...threads()[1].querySelectorAll('.comment')].map((comment) => comment.id), ['comment-b']);
  assert.equal(threads()[1].querySelector(':scope > .comment-status').textContent, "Thanks! Your reply will appear here once it's approved.");
});

test('a refused reply keeps the form and tells the reader why', async () => {
  answer = () => json(409, { success: false, message: "This comment isn't on the post anymore, so you can't reply to it.", data: null });
  replyButton(threads()[0]).click();
  await settle();
  type(inForm('textarea'), 'Late');
  submitReply();
  await settle();

  assert.equal(inForm('.comment-status').className, 'comment-status is-failed');
  assert.equal(inForm('.comment-status').textContent, "This comment isn't on the post anymore, so you can't reply to it.");
  assert.equal(inForm('textarea').value, 'Late', 'The text stays');
});
