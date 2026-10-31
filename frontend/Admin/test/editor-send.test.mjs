import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const live = {
  slug: 'my-post',
  state: 'published',
  pendingEdit: false,
  publishDate: '2026-02-04T13:00:00Z',
  eTag: null,
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: noDetails,
  text: '',
  published: { title: 'My post', excerpt: 'About it.', body: 'Some text.\n', details: noDetails },
  deliveryStarted: null,
};

const started = { queued: 2, startedDate: '2026-10-30T12:00:00Z' };

const ok = (data) => answer(200, { success: true, message: null, data });

const calls = [];
let sendAnswer = () => ok(started);

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/deliveries')) {
    calls.push({ method: init.method, body: JSON.parse(init.body) });
    return sendAnswer();
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(live);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const menuItem = (label) => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim().startsWith(label));
const button = (root, label) => [...root.querySelectorAll('button')].find((item) => item.textContent.trim() === label);
const dialog = () => document.querySelector('#send-dialog');

const type = async (element, text) => {
  element.value = text;
  element.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('the menu of a deployed published post offers Send to subscribers', () => {
  assert.equal(menuItem('Send to subscribers').getAttribute('aria-disabled'), null);
});

test('Cancel closes the dialog and sends nothing', async () => {
  menuItem('Send to subscribers').click();
  await settle();
  assert.ok(dialog().open, 'The dialog is open');

  button(dialog(), 'Cancel').click();
  await settle();
  assert.ok(!dialog().open, 'The dialog closes');
  assert.equal(calls.length, 0);
});

test('a refusal shows the reason in the dialog, and Send then sends the intro and shows the count', async () => {
  sendAnswer = () => answer(409, { success: false, message: "The latest change to post 'my-post' is not deployed yet", data: null });

  menuItem('Send to subscribers').click();
  await settle();
  await type(dialog().querySelector('#send-intro'), 'A short note.');
  button(dialog(), 'Send').click();
  await settle();

  assert.ok(dialog().open, 'The dialog stays open');
  assert.match(dialog().textContent, /The latest change to post 'my-post' is not deployed yet/);

  sendAnswer = () => ok(started);
  button(dialog(), 'Send').click();
  await settle();

  assert.deepEqual(calls, [
    { method: 'POST', body: { intro: 'A short note.' } },
    { method: 'POST', body: { intro: 'A short note.' } },
  ]);
  assert.ok(!dialog().open, 'The dialog closes');
  assert.match(document.querySelector('main').textContent, /The delivery started\./);
  assert.match(document.querySelector('main').textContent, /The admin queued the email for 2 active subscriptions\./);
});

test('after the start, Send to subscribers is not available', () => {
  assert.equal(menuItem('Send to subscribers').getAttribute('aria-disabled'), 'true');
});
