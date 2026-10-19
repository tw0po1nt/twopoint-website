import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/unsubscribe/' });

const errors = [];
console.error = (...args) => errors.push(args.join(' '));

const calls = [];
let answer;
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answer();
};

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const settle = () => new Promise((resolve) => setTimeout(resolve, 10));

const entry = pathToFileURL(path.resolve('dist', manifest.entries.unsubscribe.file)).href;
let loads = 0;

// The module loader runs a module only one time for each URL.
const open = async (search) => {
  calls.length = 0;
  history.replaceState(null, '', `/unsubscribe/${search}`);
  document.body.innerHTML = '<div id="mount" data-widget="unsubscribe" data-api="https://api.test"><p>static</p></div>';
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
};

const mount = () => document.getElementById('mount');
const status = () => mount().querySelector('.unsubscribe-status');
const note = () => mount().querySelector('.unsubscribe-note');

test('a link with no subscription ID shows the error, and sends no request', async () => {
  await open('');
  assert.equal(mount().textContent.includes('static'), false, 'The static HTML is gone');
  assert.equal(calls.length, 0, 'No request');
  assert.equal(status().className, 'unsubscribe-status is-failed');
  assert.match(status().textContent, /no subscription ID/);
  assert.equal(note(), null, 'No subscribe link');
});

test('a link with an empty subscription ID shows the error, and sends no request', async () => {
  await open('?subscriptionId=');
  assert.equal(calls.length, 0, 'No request');
  assert.match(status().textContent, /no subscription ID/);
});

test('an unsubscribe that goes through shows the result and links to /subscribe/', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  await open('?subscriptionId=sub-123');
  assert.equal(calls.length, 1, 'One request');
  assert.equal(calls[0].url, 'https://api.test/api/blog/subscriptions/cancellations');
  assert.equal(calls[0].init.method, 'POST');
  assert.deepEqual(JSON.parse(calls[0].init.body), { SubscriptionId: 'sub-123' });
  assert.equal(status().className, 'unsubscribe-status is-done');
  assert.match(status().textContent, /You are unsubscribed/);
  // The Solid template writes `<a href=/subscribe/>`. A browser reads `/subscribe/`, and happy-dom 21 drops the last `/`.
  assert.match(note().querySelector('a').getAttribute('href'), /^\/subscribe\/?$/);
  assert.equal(note().querySelector('a').textContent, 'subscribe page');
});

test('a refused unsubscribe shows the message of the API', async () => {
  answer = () => json(400, { success: false, message: 'The subscription ID is not valid', data: null });
  await open('?subscriptionId=sub-123');
  assert.equal(calls.length, 1, 'One request');
  assert.equal(status().className, 'unsubscribe-status is-failed');
  assert.equal(status().textContent, 'The subscription ID is not valid');
  assert.equal(note(), null, 'No subscribe link');
});

test('a refused unsubscribe with no message shows a general message', async () => {
  answer = () => json(500, { success: false, message: null, data: null });
  await open('?subscriptionId=sub-123');
  assert.match(status().textContent, /did not go through/);
});

test('a request with no answer shows that the site cannot get to the server', async () => {
  answer = () => Promise.reject(new TypeError('Failed to fetch'));
  await open('?subscriptionId=sub-123');
  assert.equal(calls.length, 1, 'One request');
  assert.equal(status().className, 'unsubscribe-status is-failed');
  assert.match(status().textContent, /cannot get to the server/);
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', async () => {
  errors.length = 0;
  history.replaceState(null, '', '/unsubscribe/?subscriptionId=sub-123');
  document.body.innerHTML = '<div id="bad" data-widget="unsubscribe" data-api="https://api.test/"><p>static</p></div>';
  calls.length = 0;
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
  assert.equal(document.getElementById('bad').innerHTML, '<p>static</p>');
  assert.equal(calls.length, 0, 'No request');

  assert.deepEqual(errors, [
    "unsubscribe: The 'data-api' of the unsubscribe mount is 'https://api.test/'. Give an http or https origin with no '/' at the end.",
  ]);
});
