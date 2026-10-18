import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

GlobalRegistrator.register({ url: 'http://localhost:4321/subscribe/' });

document.body.innerHTML = `
  <div id="good" data-widget="subscribe" data-api="https://api.test"><p>static</p></div>
  <div id="bad" data-widget="subscribe" data-api="https://api.test/"><p>static</p></div>`;

const errors = [];
console.error = (...args) => errors.push(args.join(' '));

const calls = [];
let answer;
globalThis.fetch = async (url, init) => {
  calls.push({ url: String(url), init });
  return answer();
};

const json = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

await import(pathToFileURL(path.resolve('dist', manifest.entries.subscribe.file)).href);

const mount = document.getElementById('good');
const form = () => mount.querySelector('form.subscribe');
const email = () => mount.querySelector('input[name="email"]');
const name = () => mount.querySelector('input[name="name"]');
const status = () => mount.querySelector('.subscribe-status');

const type = (input, value) => {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
};

const submit = () => form().dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

const settle = () => new Promise((resolve) => setTimeout(resolve, 10));

test('the entry replaces the static HTML of a good mount with the form', () => {
  assert.ok(form(), 'The mount has the form');
  assert.equal(mount.textContent.includes('static'), false, 'The static HTML is gone');
  assert.ok(email() && name(), 'The form has the email and the name inputs');
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', () => {
  assert.equal(document.getElementById('bad').innerHTML, '<p>static</p>');

  assert.deepEqual(errors, [
    "subscribe: The 'data-api' of the subscribe mount is 'https://api.test/'. Give an http or https origin with no '/' at the end.",
  ]);
});

test('an email address that is not valid shows an error, and sends no request', async () => {
  type(email(), 'not-an-email');
  submit();
  await settle();
  assert.equal(calls.length, 0, 'No request');
  assert.match(mount.querySelector('.subscribe-error').textContent, /valid email address/);
});

test('a refused subscription shows the message of the API', async () => {
  answer = () => json(400, { success: false, message: 'The email address is not valid', data: null });
  type(email(), 'reader@example.com');
  type(name(), ' Reader ');
  submit();
  await settle();
  assert.equal(calls.length, 1, 'One request');
  assert.equal(calls[0].url, 'https://api.test/api/blog/subscriptions');
  assert.equal(calls[0].init.method, 'POST');
  assert.deepEqual(JSON.parse(calls[0].init.body), { EmailAddress: 'reader@example.com', Name: 'Reader' });
  assert.equal(mount.querySelector('.subscribe-error'), null, 'The email error is gone');
  assert.equal(status().className, 'subscribe-status is-failed');
  assert.equal(status().textContent, 'The email address is not valid');
});

test('a request with no answer shows that the site cannot get to the server', async () => {
  answer = () => Promise.reject(new TypeError('Failed to fetch'));
  submit();
  await settle();
  assert.equal(calls.length, 2, 'A second request');
  assert.match(status().textContent, /cannot get to the server/);
});

test('a subscription that goes through shows the result and empties the form', async () => {
  answer = () => json(200, { success: true, message: null, data: null });
  submit();
  await settle();
  assert.equal(calls.length, 3, 'A third request');
  assert.equal(status().className, 'subscribe-status is-subscribed');
  assert.match(status().textContent, /You are subscribed/);
  assert.equal(email().value, '', 'The email input is empty');
  assert.equal(name().value, '', 'The name input is empty');
});
