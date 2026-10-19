import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/posts/a-post/' });

document.body.innerHTML = `
  <details id="panel"><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post"><p>static</p></div></details>
  <details id="open-panel" open><summary>$ comments --list</summary><div id="open" data-widget="comments" data-api="https://api.test" data-slug="open-post"><p>static</p></div></details>
  <details><summary>$ comments --list</summary><div id="bad" data-widget="comments" data-api="https://api.test" data-slug="A Post"><p>static</p></div></details>`;

localStorage.setItem('commenterValidations', JSON.stringify({ 'known@example.com': 'validation-1' }));

const errors = [];
console.error = (...args) => errors.push(args.join(' '));

const calls = [];
let answer = () => json(200, { success: true, message: null, data: [] });
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answer(String(url), init);
};

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const listUrl = 'https://api.test/api/blog/posts/a-post/comments';
const callsTo = (url) => calls.filter((call) => call.url === url);

await import(pathToFileURL(path.resolve('dist', manifest.entries.comments.file)).href);

const settle = () => new Promise((resolve) => setTimeout(resolve, 20));

const panel = document.getElementById('panel');
const mount = document.getElementById('good');
const field = (name) => mount.querySelector(`[name="${name}"]`);
const status = () => mount.querySelector('.comment-status');

const type = (input, value) => {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
};

const submit = () => mount.querySelector('form.comment-form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

test('a closed panel makes no API call and keeps its static HTML', async () => {
  await settle();
  assert.equal(callsTo(listUrl).length, 0, 'No fetch before toggle');
  assert.equal(mount.innerHTML, '<p>static</p>');
});

test('a panel that is open at the start loads the comments at once', async () => {
  await settle();
  assert.equal(callsTo('https://api.test/api/blog/posts/open-post/comments').length, 1, 'One fetch with no toggle');
  assert.ok(document.getElementById('open').querySelector('form.comment-form'), 'The open panel has the form');
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', () => {
  assert.equal(document.getElementById('bad').innerHTML, '<p>static</p>');

  assert.deepEqual(errors, [
    "comments: The 'data-slug' of the comments mount is 'A Post'. Give a post slug of lowercase letters, digits, and '-'.",
  ]);
});

test('the first toggle loads the view and fetches the comments one time', async () => {
  answer = () =>
    json(200, {
      success: true,
      message: null,
      data: [
        { id: '1', createdDate: '2026-10-17T09:30:00Z', commenter: 'Ada', content: 'First.\nSecond line.' },
        { id: '2', createdDate: '2026-10-18T10:00:00Z', commenter: 'Grace', content: '<b>not bold</b>' },
      ],
    });

  panel.querySelector('summary').click();
  assert.equal(panel.open, true, 'The panel is open');
  await settle();

  const fetched = callsTo(listUrl);
  assert.equal(fetched.length, 1, 'One fetch after toggle');
  assert.equal(fetched[0].init.method, 'GET');
  assert.equal(mount.textContent.includes('static'), false, 'The static HTML is gone');
  assert.equal(mount.querySelector('.comments-count').textContent, '2 comments');

  const items = [...mount.querySelectorAll('.comment')];
  assert.equal(items.length, 2);
  assert.equal(items[0].querySelector('.comment-meta').textContent, '2026-10-17 · Ada');
  assert.equal(items[0].querySelector('time').getAttribute('datetime'), '2026-10-17T09:30:00Z');
  assert.equal(items[1].querySelector('.comment-text').textContent, '<b>not bold</b>', 'A comment is text, not HTML');
  assert.equal(items[1].querySelector('b'), null);
});

test('a second toggle makes no second fetch', async () => {
  panel.querySelector('summary').click();
  panel.querySelector('summary').click();
  await settle();
  assert.equal(callsTo(listUrl).length, 1, 'Still one fetch');
});

test('a submit with no comment shows the errors and sends nothing', async () => {
  const before = calls.length;
  type(field('email'), 'not-an-email');
  submit();
  await settle();
  assert.equal(calls.length, before, 'No request');

  assert.deepEqual(
    [...mount.querySelectorAll('.comment-error')].map((error) => error.textContent),
    ['Give a valid email address.', 'Write a comment.'],
  );
});

test('a refused comment shows the message of the API', async () => {
  answer = () => json(400, { success: false, message: 'The commenter is banned', data: null });
  type(field('email'), 'known@example.com');
  type(field('comment'), ' Nice post & thanks ');
  submit();
  await settle();
  assert.equal(callsTo(listUrl).filter((call) => call.init.method === 'POST').length, 1, 'One post request');
  assert.equal(status().className, 'comment-status is-failed');
  assert.equal(status().textContent, 'The commenter is banned');
});

test('a known email address posts the comment with its stored validation ID and empties the form', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  submit();
  await settle();

  const posted = callsTo(listUrl).filter((call) => call.init.method === 'POST');
  assert.equal(posted.length, 2, 'A second post request');
  assert.deepEqual(JSON.parse(posted[1].init.body), { ValidationId: 'validation-1', Comment: 'Nice post & thanks' });
  assert.match(status().textContent, /Your comment is posted/);
  assert.equal(field('email').value, '', 'The email input is empty');
  assert.equal(field('comment').value, '', 'The comment input is empty');
  assert.equal(mount.querySelectorAll('.comment').length, 2, 'The list stays');
});

test('a request with no answer shows that the site cannot get to the server', async () => {
  answer = () => Promise.reject(new TypeError('Failed to fetch'));
  type(field('email'), 'known@example.com');
  type(field('comment'), 'Again');
  submit();
  await settle();
  assert.match(status().textContent, /cannot get to the server/);
});

test('a new email address gets a validation email that links back to the panel, and the form locks', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  type(field('name'), ' Reader ');
  type(field('email'), 'reader@example.com');
  type(field('comment'), ' Nice post & thanks ');
  submit();
  await settle();

  const sent = callsTo('https://api.test/api/blog/commenters');
  assert.equal(sent.length, 1, 'One validation request');
  assert.equal(sent[0].init.method, 'POST');

  assert.deepEqual(JSON.parse(sent[0].init.body), {
    EmailAddress: 'reader@example.com',
    Name: 'Reader',
    RedirectUri: 'http://localhost:4321/posts/a-post/?email=reader%40example.com&name=Reader&comment=Nice%20post%20%26%20thanks#comments',
  });

  assert.equal(status().className, 'comment-status is-done');
  assert.match(status().textContent, /sent an email to reader@example\.com/);

  assert.deepEqual(
    ['name', 'email', 'comment'].map((name) => field(name).disabled),
    [true, true, true],
    'Each field is locked',
  );
  assert.equal(mount.querySelector('button[type="submit"]').disabled, true, 'The button is locked');

  const before = calls.length;
  submit();
  await settle();
  assert.equal(calls.length, before, 'A second submit sends nothing');
});
