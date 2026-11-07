import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/posts/a-post/' });

document.body.innerHTML = `
  <details id="panel"><summary>$ comments --list</summary><div id="good" data-widget="comments" data-api="https://api.test" data-slug="a-post" data-author="Matthew Watt"><p>static</p></div></details>
  <details id="open-panel" open><summary>$ comments --list</summary><div id="open" data-widget="comments" data-api="https://api.test" data-slug="open-post" data-author="Matthew Watt"><p>static</p></div></details>
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

// The HTML that the backend makes from the Markdown of a comment.
const formatted = [
  '<p>Use <code>Option</code> here:</p>',
  '<pre tabindex="0"><code>let x = 1\n</code></pre>',
  '<ul>\n<li>one</li>\n<li>two</li>\n</ul>',
  '<blockquote>\n<p>a quote</p>\n</blockquote>',
  '<p><a href="https://example.com/" target="_blank" rel="nofollow ugc noopener noreferrer">a source</a> and <a href="https://twopoint.dev/posts/a-post/">a post</a></p>',
].join('\n');

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
        { id: '1', createdDate: '2026-10-17T09:30:00Z', commenter: 'Ada', content: '<p>First.<br />\nSecond line.</p>\n' },
        { id: '2', createdDate: '2026-10-18T10:00:00Z', commenter: 'Grace', content: formatted },
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

  const plain = items[0].querySelector('.comment-text');
  assert.equal(plain.tagName, 'DIV', 'The comment text is a div, because the HTML has its own paragraphs');
  assert.equal(plain.innerHTML, '<p>First.<br>\nSecond line.</p>\n', 'A comment with no syntax keeps its line break');

  const text = items[1].querySelector('.comment-text');
  assert.equal(text.querySelector('p > code').textContent, 'Option', 'Inline code is code');
  assert.equal(text.querySelector('pre > code').textContent, 'let x = 1\n', 'A code block is code');
  assert.deepEqual([...text.querySelectorAll('ul > li')].map((item) => item.textContent), ['one', 'two'], 'A list is a list');
  assert.equal(text.querySelector('blockquote > p').textContent, 'a quote', 'A quote is a quote');

  const [external, own] = text.querySelectorAll('a');
  assert.equal(external.getAttribute('href'), 'https://example.com/');
  assert.equal(external.getAttribute('target'), '_blank', 'An external link opens in a new tab');
  assert.equal(external.getAttribute('rel'), 'nofollow ugc noopener noreferrer');
  assert.equal(own.getAttribute('href'), 'https://twopoint.dev/posts/a-post/');
  assert.equal(own.getAttribute('target'), null, 'A link to twopoint.dev opens in the same tab');
});

test('the page form has an unchecked reply-email checkbox, and the email field says what the address is for', () => {
  const box = field('replyEmails');
  assert.equal(box.type, 'checkbox');
  assert.equal(box.checked, false, 'The checkbox is unchecked');
  assert.equal(box.closest('label').textContent, 'email me when someone replies');
  assert.equal(field('email').getAttribute('placeholder'), 'to verify you, and for reply emails if you ask');
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
    ['Enter a valid email address.', 'Write a comment.'],
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

test('a known email address posts the comment with its stored validation ID, and empties the text only', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  submit();
  await settle();

  const posted = callsTo(listUrl).filter((call) => call.init.method === 'POST');
  assert.equal(posted.length, 2, 'A second post request');
  assert.deepEqual(JSON.parse(posted[1].init.body), { ValidationId: 'validation-1', Comment: 'Nice post & thanks', ReplyEmails: false });
  assert.equal(status().textContent, "Thanks! Your comment will appear here once it's approved.");
  assert.equal(field('email').value, 'known@example.com', 'The email input keeps the address for the next comment or reply');
  assert.equal(field('comment').value, '', 'The comment input is empty');
  assert.equal(mount.querySelectorAll('.comment').length, 2, 'The list stays');
});

test('a checked box posts its value, and the next comment starts with the box unchecked', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  type(field('email'), 'known@example.com');
  type(field('comment'), 'Tell me more');
  const box = field('replyEmails');
  box.checked = true;
  box.dispatchEvent(new Event('change', { bubbles: true }));
  submit();
  await settle();

  const posted = callsTo(listUrl).filter((call) => call.init.method === 'POST');
  assert.deepEqual(JSON.parse(posted.at(-1).init.body), { ValidationId: 'validation-1', Comment: 'Tell me more', ReplyEmails: true });
  assert.equal(field('replyEmails').checked, false, 'The checkbox is unchecked after the post');
});

test('the comment of a trusted commenter shows in the list at once', async () => {
  answer = () =>
    json(200, {
      success: true,
      message: null,
      data: null,
      comment: { id: '3', createdDate: '2026-10-19T08:00:00Z', commenter: 'Reader', content: '<p>Trusted <em>words</em></p>\n', approval: 'approved' },
    });
  type(field('email'), 'known@example.com');
  type(field('comment'), 'Trusted words');
  submit();
  await settle();

  assert.equal(status().className, 'comment-status is-done');
  assert.equal(status().textContent, 'Comment posted.');
  assert.equal(mount.querySelector('.comments-count').textContent, '3 comments');

  const items = [...mount.querySelectorAll('.comment')];
  assert.equal(items.length, 3, 'The comment is in the list');
  assert.equal(items[2].querySelector('.comment-meta').textContent, '2026-10-19 · Reader');
  assert.equal(items[2].querySelector('.comment-text').innerHTML, '<p>Trusted <em>words</em></p>\n', 'The list shows the HTML of the answer');
  assert.equal(field('comment').value, '', 'The comment input is empty');
});

test('a request with no answer shows that the site cannot get to the server', async () => {
  answer = () => Promise.reject(new TypeError('Failed to fetch'));
  type(field('email'), 'known@example.com');
  type(field('comment'), 'Again');
  submit();
  await settle();
  assert.match(status().textContent, /Couldn't reach the server/);
});

const previewUrl = 'https://api.test/api/blog/comments/preview';
const tabs = () => [...mount.querySelectorAll('[role="tab"]')];
const tab = (name) => tabs().find((element) => element.textContent === `[${name}]`);
const preview = () => mount.querySelector('.comment-preview');
const previewAnswer = (html) => (url) =>
  url === previewUrl ? json(200, { success: true, message: null, data: html }) : json(200, { success: true, message: null, data: null });

test('the comment field has the write and preview tabs, and write is selected', () => {
  assert.deepEqual(
    tabs().map((element) => [element.textContent, element.getAttribute('aria-selected')]),
    [
      ['[write]', 'true'],
      ['[preview]', 'false'],
    ],
  );
  assert.equal(preview(), null, 'No preview on the write tab');
});

test('the preview of an empty text says that there is nothing to preview, with no call', async () => {
  type(field('comment'), '  ');
  const before = calls.length;
  tab('preview').click();
  await settle();

  assert.equal(calls.length, before, 'No request');
  assert.equal(tab('preview').getAttribute('aria-selected'), 'true');
  assert.equal(field('comment').classList.contains('is-hidden'), true, 'The preview replaces the text box');
  assert.equal(preview().textContent, 'Nothing to preview. Write a comment first.');

  tab('write').click();
  await settle();
  assert.equal(preview(), null, 'The write tab shows the text box');
  assert.equal(field('comment').classList.contains('is-hidden'), false);
});

test('the preview calls the backend one time and shows the HTML, and an open with the same text makes no call', async () => {
  answer = previewAnswer('<p>Use <code>List.map</code></p>\n');
  type(field('comment'), ' Use `List.map` ');
  tab('preview').click();
  assert.equal(preview().textContent, 'Loading preview…');
  await settle();

  const sent = callsTo(previewUrl);
  assert.equal(sent.length, 1, 'One preview request');
  assert.equal(sent[0].init.method, 'POST');
  assert.deepEqual(JSON.parse(sent[0].init.body), { content: 'Use `List.map`' });
  assert.equal(preview().querySelector('.comment-text').innerHTML, '<p>Use <code>List.map</code></p>\n');

  tab('write').click();
  tab('preview').click();
  await settle();
  assert.equal(callsTo(previewUrl).length, 1, 'Still one preview request');
  assert.equal(preview().querySelector('.comment-text code').textContent, 'List.map', 'The kept HTML');
});

test('a failed preview says that the comment can still go, and [try again] calls again', async () => {
  answer = (url) => (url === previewUrl ? json(500, { success: false, message: 'down', data: null }) : json(200, {}));
  tab('write').click();
  type(field('comment'), 'Use `List.iter`');
  tab('preview').click();
  await settle();

  assert.equal(preview().className, 'comment-preview is-failed');
  assert.equal(preview().textContent, "Couldn't load the preview. You can still post your comment. [try again]");

  answer = previewAnswer('<p>Use <code>List.iter</code></p>\n');
  preview().querySelector('button').click();
  await settle();
  assert.equal(callsTo(previewUrl).length, 3, 'A call for the edit and a call for [try again]');
  assert.equal(preview().querySelector('.comment-text code').textContent, 'List.iter');
});

test('[post comment] posts the comment from the preview tab', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  submit();
  await settle();

  const posted = callsTo(listUrl).filter((call) => call.init.method === 'POST');
  assert.deepEqual(JSON.parse(posted.at(-1).init.body), { ValidationId: 'validation-1', Comment: 'Use `List.iter`', ReplyEmails: false });
  assert.equal(status().textContent, "Thanks! Your comment will appear here once it's approved.");
  assert.equal(tab('write').getAttribute('aria-selected'), 'true', 'The empty form shows the write tab');
  assert.equal(field('comment').value, '', 'The comment input is empty');
});

test('[markdown help] opens and closes the table, with no call', async () => {
  const before = calls.length;
  const help = mount.querySelector('.comment-help button');
  assert.equal(help.textContent, '[markdown help]');
  assert.equal(mount.querySelector('.comment-help-sheet'), null, 'The help is closed');

  help.click();
  await settle();
  assert.equal(help.getAttribute('aria-expanded'), 'true');
  assert.deepEqual([...mount.querySelectorAll('.comment-help-sheet th')].map((cell) => cell.textContent), ['you type', 'readers see']);

  const rows = [...mount.querySelectorAll('.comment-help-sheet tbody tr')];
  assert.equal(rows.length, 7, 'Italic, bold, inline code, a link, a list, a quote, and a code block');
  assert.equal(rows[0].querySelector('pre').textContent, '*italic*');
  assert.equal(rows[0].querySelector('.comment-text').innerHTML, '<p><em>italic</em></p>\n');
  assert.match(mount.querySelector('.comment-help-sheet > p').textContent, /Headings, images, and tables show as plain text/);

  help.click();
  await settle();
  assert.equal(mount.querySelector('.comment-help-sheet'), null, 'The help is closed again');
  assert.equal(calls.length, before, 'No request');
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
  assert.match(status().textContent, /sent a verification link to reader@example\.com/);

  assert.deepEqual(
    ['name', 'email', 'comment', 'replyEmails'].map((name) => field(name).disabled),
    [true, true, true, true],
    'Each field is locked',
  );
  assert.equal(mount.querySelector('button[type="submit"]').disabled, true, 'The button is locked');

  const before = calls.length;
  submit();
  await settle();
  assert.equal(calls.length, before, 'A second submit sends nothing');
});
