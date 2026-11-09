import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/subscribe/confirm/' });

const errors = [];
console.error = (...args) => errors.push(args.join(' '));

const subscriptionId = '2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10';
const confirmUrl = `https://api.test/api/blog/subscriptions/${subscriptionId}/confirm`;

const calls = [];
let answers = [];
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answers.shift()();
};

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const state = (data) => () => json(200, { success: true, message: null, data });

const settle = () => new Promise((resolve) => setTimeout(resolve, 10));

const entry = pathToFileURL(path.resolve('dist', manifest.entries['confirm-subscription'].file)).href;
let loads = 0;

// The module loader runs a module only one time for each URL.
const open = async (search) => {
  calls.length = 0;
  history.replaceState(null, '', `/subscribe/confirm/${search}`);
  document.body.innerHTML = '<div id="mount" data-widget="confirm-subscription" data-api="https://api.test"><p>static</p></div>';
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
};

const mount = () => document.getElementById('mount');
const status = () => mount().querySelector('.confirm-subscription-status');
const button = () => mount().querySelector('button');
const error = () => mount().querySelector('.confirm-subscription-error');
const note = () => mount().querySelector('.confirm-subscription-note');

test('a link with no subscription ID says so, and sends no request', async () => {
  answers = [];
  await open('');
  assert.equal(mount().textContent.includes('static'), false, 'The static HTML is gone');
  assert.equal(calls.length, 0, 'No request');
  assert.equal(status().className, 'confirm-subscription-status is-failed');
  assert.match(status().textContent, /missing a subscription ID/);
  assert.equal(button(), null, 'No button');
});

test('the page sends nothing when it opens, and only the button sends the confirm', async () => {
  answers = [state({ state: 'confirmed' })];
  await open(`?subscriptionId=${subscriptionId}`);
  assert.equal(calls.length, 0, 'No request when the page opens');
  assert.equal(status().textContent, 'Confirm your subscription to get new posts by email.');
  assert.equal(button().textContent, '[confirm subscription]');

  button().click();
  await settle();
  assert.equal(calls.length, 1, 'One confirm');
  assert.equal(calls[0].url, confirmUrl);
  assert.equal(calls[0].init.method, 'POST');
  assert.equal(status().className, 'confirm-subscription-status is-done');
  assert.equal(status().textContent, "You're subscribed! The next post will land in your inbox.");
  assert.equal(button(), null, 'No button after the confirm');
});

test('an unknown subscription says the link does not work, and links to the subscribe page', async () => {
  answers = [state({ state: 'unknown' })];
  await open(`?subscriptionId=${subscriptionId}`);
  button().click();
  await settle();
  assert.equal(status().className, 'confirm-subscription-status is-failed');
  assert.equal(status().textContent, "This link doesn't work.");
  // The Solid template writes `<a href=/subscribe/>`. A browser reads `/subscribe/`, and happy-dom 21 drops the last `/`.
  assert.match(note().querySelector('a').getAttribute('href'), /^\/subscribe\/?$/);
  assert.equal(button(), null, 'No button');
});

test('a failed confirm keeps the button and shows the failure', async () => {
  answers = [() => json(500, { success: false, message: 'The table is down', data: null }), state({ state: 'confirmed' })];
  await open(`?subscriptionId=${subscriptionId}`);
  button().click();
  await settle();
  assert.equal(button().textContent, '[confirm subscription]');
  assert.equal(error().textContent, "Couldn't confirm your subscription. Try again.");

  button().click();
  await settle();
  assert.equal(calls.length, 2, 'A second confirm');
  assert.equal(status().className, 'confirm-subscription-status is-done');
  assert.equal(error(), null, 'No failure after the confirm');
});

test('a confirm with no answer shows that the site cannot get to the server', async () => {
  answers = [() => Promise.reject(new TypeError('Failed to fetch'))];
  await open(`?subscriptionId=${subscriptionId}`);
  button().click();
  await settle();
  assert.equal(error().textContent, "Couldn't reach the server. Try again.");
  assert.equal(button().textContent, '[confirm subscription]');
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', async () => {
  errors.length = 0;
  answers = [];
  history.replaceState(null, '', `/subscribe/confirm/?subscriptionId=${subscriptionId}`);
  document.body.innerHTML = '<div id="bad" data-widget="confirm-subscription" data-api="https://api.test/"><p>static</p></div>';
  calls.length = 0;
  loads += 1;
  await import(`${entry}?load=${loads}`);
  await settle();
  assert.equal(document.getElementById('bad').innerHTML, '<p>static</p>');
  assert.equal(calls.length, 0, 'No request');

  assert.deepEqual(errors, [
    "confirm-subscription: The 'data-api' of the confirm-subscription mount is 'https://api.test/'. Give an http or https origin with no '/' at the end.",
  ]);
});
