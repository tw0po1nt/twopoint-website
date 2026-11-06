import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, follow, heading, load, press, settle } from './load.mjs';

const subscription = (id, name, isActive, createdDate) => ({
  id,
  email: `${id}@example.com`,
  name,
  createdDate,
  isActive,
  canceledDate: isActive ? null : '2026-02-03T04:05:06Z',
});

const subscriptions = [
  subscription('ada', 'Ada Lovelace', true, '2026-01-04T00:00:00Z'),
  subscription('grace', 'Grace Hopper', false, '2026-01-03T00:00:00Z'),
  subscription('alan', null, true, '2026-01-02T00:00:00Z'),
];

const deliveries = [
  { id: 'd-2', createdDate: '2026-02-05T00:00:00Z', type: 'newpost', postSlug: 'second', postTitle: 'Second' },
  { id: 'd-1', createdDate: '2026-01-03T00:00:00Z', type: 'welcome', postSlug: 'first', postTitle: 'First' },
];

const reads = [];
const actions = [];

const respond = (url, init = {}) => {
  const path = url.replace(/^.*\/api\//, '');

  if (init.method === 'POST' && path === 'internal/subscriptions/ada/cancellations') {
    actions.push({ method: init.method, url: path });
    return answer(200, {
      success: true,
      message: null,
      data: { ...subscriptions[0], isActive: false, canceledDate: '2026-03-01T12:00:00Z' },
    });
  }

  if (init.method === 'DELETE') {
    actions.push({ method: init.method, url: path });
    return answer(200, { success: true, message: null, data: null });
  }

  if (path === 'internal/subscriptions') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: subscriptions });
  }

  if (path === 'internal/subscriptions/grace/deliveries') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: deliveries });
  }

  if (path === 'internal/subscriptions/ada/deliveries') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: [] });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/subscriptions', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);
const search = () => document.querySelector('main .toolbar input[type="search"]');
const sheet = () => document.getElementById('subscription-sheet');
const open = (id) => document.getElementById(id).querySelector('.row-link').click();

const type = async (text) => {
  const input = search();
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('the list shows each subscription with its status, and the counts of the audience', () => {
  assert.equal(heading(), 'Subscriptions');
  assert.deepEqual(rows(), ['ada', 'grace', 'alan']);
  assert.match(document.getElementById('grace').textContent, /grace@example\.com/);
  assert.match(document.getElementById('grace').textContent, /Inactive/);
  assert.match(document.getElementById('alan').textContent, /No name/);
  assert.match(document.querySelector('main .toolbar').textContent, /2 active, 1 inactive/);
});

test('the filter shows only the active or the inactive subscriptions, with no API call', async () => {
  tab('Inactive').click();
  await settle();
  assert.deepEqual(rows(), ['grace']);

  tab('Active').click();
  await settle();
  assert.deepEqual(rows(), ['ada', 'alan']);
  assert.match(document.querySelector('main .toolbar').textContent, /2 active, 1 inactive/);
  assert.equal(reads.filter((path) => path === 'internal/subscriptions').length, 1);

  tab('All').click();
  await settle();
});

test('the search keeps its input, and shows the subscriptions that match', async () => {
  const input = search();
  await type('hopper');

  assert.equal(search(), input, 'A typed letter does not replace the input');
  assert.equal(search().value, 'hopper');
  assert.deepEqual(rows(), ['grace']);

  await type('nothing');
  assert.deepEqual(rows(), []);
  assert.match(document.querySelector('main').textContent, /No subscriptions match your search\./);

  await type('');
  assert.deepEqual(rows(), ['ada', 'grace', 'alan']);
});

test('a tap on a row opens the sheet with the fields, the canceled date, and the deliveries, newest first', async () => {
  assert.equal(document.getElementById('grace').querySelectorAll('button').length, 1, 'The row has no Details button');
  open('grace');
  await settle();

  assert.equal(sheet().open, true);
  assert.deepEqual(reads.slice(-1), ['internal/subscriptions/grace/deliveries']);
  const text = sheet().textContent;
  assert.match(text, /grace@example\.com/);
  assert.match(text, /Grace Hopper/);
  assert.match(text, /Canceled/);
  assert.match(text, /2026-02-03/);

  const items = [...sheet().querySelectorAll('.deliveries li')];
  assert.deepEqual(
    items.map((item) => [item.firstElementChild.textContent.trim(), [...item.querySelectorAll('.badge-line > *')].map((part) => part.textContent.trim())]),
    [
      ['Second', ['New post', '2026-02-05']],
      ['First', ['Welcome', '2026-01-03']],
    ],
  );
  assert.deepEqual(
    [...sheet().querySelectorAll('.deliveries a')].map((link) => link.getAttribute('href')),
    ['https://twopoint.dev/posts/second/', 'https://twopoint.dev/posts/first/'],
  );
});

test('the sheet closes, and a sheet of an active subscription has no canceled date', async () => {
  sheet().close();
  await settle();
  assert.equal(sheet().open, false);

  open('ada');
  await settle();
  assert.equal(sheet().open, true);
  assert.doesNotMatch(sheet().textContent, /Canceled/);
  assert.match(sheet().textContent, /No deliveries yet\./);

  sheet().close();
  await settle();
});

test('the navigation link opens All after a visit to another page, and the list loads again', async () => {
  tab('Inactive').click();
  await settle();
  assert.equal(location.pathname + location.search, '/subscriptions?filter=inactive');

  follow('Dashboard');
  await settle();
  follow('Subscriptions');
  await settle();

  assert.equal(location.pathname + location.search, '/subscriptions');
  assert.equal(tab('All').getAttribute('aria-selected'), 'true');
  assert.deepEqual(rows(), ['ada', 'grace', 'alan']);
  assert.equal(reads.filter((path) => path === 'internal/subscriptions').length, 2);
});

test('Cancel subscription cancels an active subscription, sets the canceled date, and keeps the sheet open', async () => {
  tab('All').click();
  await settle();

  open('ada');
  await settle();
  assert.deepEqual(
    [...sheet().querySelectorAll('.actions button')].map((item) => item.textContent.trim()),
    ['Cancel subscription', 'Delete'],
  );

  press('Cancel subscription', sheet());
  await settle();

  assert.deepEqual(actions, [{ method: 'POST', url: 'internal/subscriptions/ada/cancellations' }]);
  assert.equal(sheet().open, true);
  assert.match(sheet().textContent, /Canceled/);
  assert.match(sheet().textContent, /2026-03-01/);
  assert.match(document.getElementById('ada').textContent, /Inactive/);
  assert.match(document.querySelector('main .toolbar').textContent, /1 active, 2 inactive/);
  assert.deepEqual(
    [...sheet().querySelectorAll('.actions button')].map((item) => item.textContent.trim()),
    ['Delete'],
  );
});

test('Delete asks for a confirmation, and Keep in the dialog deletes nothing', async () => {
  press('Delete', sheet());
  await settle();

  const dialog = document.getElementById('delete-subscription');
  assert.equal(dialog.open, true);
  assert.deepEqual(actions.filter((action) => action.method === 'DELETE'), []);

  press('Cancel', dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(actions.filter((action) => action.method === 'DELETE'), []);
  assert.ok(document.getElementById('ada'));
});

test('Delete in the confirmation deletes the subscription, and the row and the sheet go away', async () => {
  press('Delete', sheet());
  await settle();

  const dialog = document.getElementById('delete-subscription');
  press('Delete', dialog);
  await settle();

  assert.equal(dialog.open, false);
  assert.deepEqual(actions.slice(-1), [{ method: 'DELETE', url: 'internal/subscriptions/ada' }]);
  assert.equal(sheet().open, false);
  assert.deepEqual(rows(), ['grace', 'alan']);
});
