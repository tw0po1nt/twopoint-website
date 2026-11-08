import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/comments/stop/' });

const errors = [];
console.error = (...args) => errors.push(args.join(' '));

const token = '4e3d2c1b-0a9f-4e8d-8c7b-6a5f4e3d2c1b';
const stateUrl = `https://api.test/api/blog/reply-emails/${token}`;

const calls = [];
let answers = [];
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answers.shift()();
};

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const state = (data) => () => json(200, { success: true, message: null, data });

const settle = () => new Promise((resolve) => setTimeout(resolve, 10));

const entry = pathToFileURL(path.resolve('dist', manifest.entries['stop-reply-emails'].file)).href;
let loads = 0;

// The module loader runs a module only one time for each URL.
const open = async (search) => {
  calls.length = 0;
  history.replaceState(null, '', `/comments/stop/${search}`);
  document.body.innerHTML = '<div id="mount" data-widget="stop-reply-emails" data-api="https://api.test"><p>static</p></div>';
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
};

const mount = () => document.getElementById('mount');
const status = () => mount().querySelector('.stop-reply-emails-status');
const button = () => mount().querySelector('button');
const error = () => mount().querySelector('.stop-reply-emails-error');

test('a link with no token shows that the link does not work, and sends no request', async () => {
  answers = [];
  await open('');
  assert.equal(mount().textContent.includes('static'), false, 'The static HTML is gone');
  assert.equal(calls.length, 0, 'No request');
  assert.equal(status().className, 'stop-reply-emails-status is-failed');
  assert.match(status().textContent, /This link doesn't work/);
  assert.equal(button(), null, 'No button');
});

test('an active token asks with the post title, and only the button sends the stop', async () => {
  answers = [state({ state: 'active', postTitle: 'A post' }), state({ state: 'stopped', postTitle: 'A post' })];
  await open(`?token=${token}`);
  assert.equal(calls.length, 1, 'One read');
  assert.equal(calls[0].url, stateUrl);
  assert.equal(calls[0].init.method, undefined, 'The read is a GET');
  assert.equal(status().textContent, 'Stop reply emails for your comment on A post?');
  assert.equal(button().textContent, '[stop reply emails]');

  button().click();
  await settle();
  assert.equal(calls.length, 2, 'One stop');
  assert.equal(calls[1].url, `${stateUrl}/stop`);
  assert.equal(calls[1].init.method, 'POST');
  assert.equal(status().className, 'stop-reply-emails-status is-done');
  assert.equal(status().textContent, "You won't get any more reply emails for this comment.");
  assert.equal(button(), null, 'No button after the stop');
});

test('a stopped token shows the stop, with no button', async () => {
  answers = [state({ state: 'stopped', postTitle: 'A post' })];
  await open(`?token=${token}`);
  assert.equal(calls.length, 1, 'One read');
  assert.equal(status().textContent, "You won't get any more reply emails for this comment.");
  assert.equal(button(), null, 'No button');
});

test('an unknown token shows that the link does not work', async () => {
  answers = [state({ state: 'unknown', postTitle: null })];
  await open(`?token=${token}`);
  assert.equal(status().className, 'stop-reply-emails-status is-failed');
  assert.match(status().textContent, /This link doesn't work/);
  assert.equal(button(), null, 'No button');
});

test('a failed stop keeps the button and shows the failure', async () => {
  answers = [state({ state: 'active', postTitle: 'A post' }), () => json(500, { success: false, message: 'The table is down', data: null })];
  await open(`?token=${token}`);
  button().click();
  await settle();
  assert.equal(calls.length, 2, 'One stop');
  assert.equal(status().textContent, 'Stop reply emails for your comment on A post?');
  assert.equal(button().textContent, '[stop reply emails]');
  assert.equal(error().textContent, "Couldn't stop the reply emails. Try again.");
});

test('a read with no answer shows that the site cannot get to the server', async () => {
  answers = [() => Promise.reject(new TypeError('Failed to fetch'))];
  await open(`?token=${token}`);
  assert.equal(status().className, 'stop-reply-emails-status is-failed');
  assert.match(status().textContent, /Couldn't reach the server/);
  assert.equal(button(), null, 'No button');
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', async () => {
  errors.length = 0;
  answers = [];
  history.replaceState(null, '', `/comments/stop/?token=${token}`);
  document.body.innerHTML = '<div id="bad" data-widget="stop-reply-emails" data-api="https://api.test/"><p>static</p></div>';
  calls.length = 0;
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
  assert.equal(document.getElementById('bad').innerHTML, '<p>static</p>');
  assert.equal(calls.length, 0, 'No request');

  assert.deepEqual(errors, [
    "stop-reply-emails: The 'data-api' of the stop-reply-emails mount is 'https://api.test/'. Give an http or https origin with no '/' at the end.",
  ]);
});
