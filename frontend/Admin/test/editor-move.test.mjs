import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const upcoming = {
  slug: 'my-post',
  state: 'upcoming',
  pendingEdit: true,
  publishDate: '2099-12-31T00:00:00Z',
  eTag: '"w1"',
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: noDetails,
  text: '',
  published: { title: 'My post', excerpt: 'About it.', body: 'Some text.\n', details: noDetails },
};

const ok = (data) => answer(200, { success: true, message: null, data });

const calls = [];
let moveAnswer = () => ok({ publishDate: null, state: 'draft', eTag: '"w2"', productionRun: 7, warnings: [] });

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/unpublish') || url.endsWith('/api/internal/posts/my-post/date')) {
    const headers = new Headers(init.headers);
    calls.push({ url: url.split('/').pop(), method: init.method, ifMatch: headers.get('If-Match'), body: init.body ? JSON.parse(init.body) : null });
    return moveAnswer();
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(upcoming);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const status = () => document.querySelector('.editor-status').textContent.trim();
const menuItem = (label) => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim().startsWith(label));
const button = (root, label) => [...root.querySelectorAll('button')].find((item) => item.textContent.trim() === label);
const unpublish = () => document.querySelector('#unpublish-dialog');
const changeDate = () => document.querySelector('#change-date-dialog');

const type = async (element, text) => {
  element.value = text;
  element.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('the menu of a saved upcoming post offers Change the date and Unpublish', () => {
  assert.equal(menuItem('Change the date').getAttribute('aria-disabled'), null);
  assert.equal(menuItem('Unpublish').getAttribute('aria-disabled'), null);
});

test('Change the date opens a dialog with the current date, and the date sends the ETag and the UTC time', async () => {
  moveAnswer = () => ok({ publishDate: '2099-06-01T08:30:00Z', state: 'upcoming', eTag: '"w2"', productionRun: 7, warnings: [] });

  menuItem('Change the date').click();
  await settle();

  assert.ok(changeDate().open, 'The dialog is open');
  assert.equal(changeDate().querySelector('#new-publish-date').value, '2099-12-31T00:00');

  await type(changeDate().querySelector('#new-publish-date'), '2099-06-01T08:30');
  button(changeDate(), 'Change the date').click();
  await settle();

  assert.deepEqual(calls, [{ url: 'date', method: 'POST', ifMatch: '"w1"', body: { publishDate: '2099-06-01T08:30:00Z' } }]);
  assert.ok(!changeDate().open, 'The dialog closes');
  assert.match(status(), /^Upcoming Jun 1, 2099 · deployed/);
});

test('Keep closes the unpublish dialog and sends nothing, and Unpublish shows a draft', async () => {
  calls.length = 0;
  moveAnswer = () => ok({ publishDate: null, state: 'draft', eTag: '"w3"', productionRun: 7, warnings: [] });

  menuItem('Unpublish').click();
  await settle();
  assert.ok(unpublish().open, 'The dialog is open');

  button(unpublish(), 'Keep').click();
  await settle();
  assert.ok(!unpublish().open, 'The dialog closes');
  assert.equal(calls.length, 0);

  menuItem('Unpublish').click();
  await settle();
  button(unpublish(), 'Unpublish').click();
  await settle();

  assert.deepEqual(calls, [{ url: 'unpublish', method: 'POST', ifMatch: '"w2"', body: {} }]);
  assert.ok(!unpublish().open, 'The dialog closes');
  assert.match(status(), /^Draft/);
  assert.match(menuItem('Unpublish').textContent, /You can unpublish only an upcoming post\./);
  assert.match(menuItem('Change the date').textContent, /Only an upcoming post can change its date\./);
});
