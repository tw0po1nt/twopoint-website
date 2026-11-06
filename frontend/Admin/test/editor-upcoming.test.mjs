import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, deferred, load, settle } from './load.mjs';

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
const plain = { outcome: 'plain', edits: [], problems: [] };
let checks = 0;
let checkAnswer = () => ok(plain);

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/removal/check') && init.method === 'POST') {
    checks += 1;
    return checkAnswer();
  }

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
  assert.equal(menuItem('Change date').getAttribute('aria-disabled'), null);
  assert.equal(menuItem('Unpublish').getAttribute('aria-disabled'), null);
});

test('Change the date opens a dialog with the current date, and the date sends the ETag and the UTC time', async () => {
  moveAnswer = () => ok({ publishDate: '2099-06-01T08:30:00Z', state: 'upcoming', eTag: '"w2"', productionRun: 7, warnings: [] });

  menuItem('Change date').click();
  await settle();

  assert.ok(changeDate().open, 'The dialog is open');
  assert.equal(changeDate().querySelector('#new-publish-date').value, '2099-12-31T00:00');

  await type(changeDate().querySelector('#new-publish-date'), '2099-06-01T08:30');
  button(changeDate(), 'Change date').click();
  await settle();

  assert.deepEqual(calls, [{ url: 'date', method: 'POST', ifMatch: '"w1"', body: { publishDate: '2099-06-01T08:30:00Z' } }]);
  assert.ok(!changeDate().open, 'The dialog closes');
  assert.match(status(), /^Upcoming Jun 1, 2099 · deployed/);
});

const openUnpublish = async () => {
  menuItem('Unpublish').click();
  await settle();
};

const keepUnpublish = async () => {
  button(unpublish(), 'Cancel').click();
  await settle();
};

test('the unpublish dialog checks the links one time when it opens, and Unpublish is disabled while the check runs', async () => {
  const pending = deferred();
  checkAnswer = () => pending.promise;
  const checksBefore = checks;

  await openUnpublish();
  assert.ok(unpublish().open, 'The dialog is open');
  assert.equal(checks, checksBefore + 1, 'One check');
  assert.match(unpublish().textContent, /Checking for links to this post…/);
  assert.equal(button(unpublish(), 'Unpublish').disabled, true);

  pending.resolve(ok(plain));
  await settle();
  assert.doesNotMatch(unpublish().textContent, /Checking for links to this post…/);
  assert.doesNotMatch(unpublish().textContent, /Other posts link to this post/);
  assert.equal(button(unpublish(), 'Unpublish').disabled, false);
  assert.equal(checks, checksBefore + 1, 'No second check');
  await keepUnpublish();
});

test('a blocked check shows the problems and keeps Unpublish disabled', async () => {
  checkAnswer = () =>
    ok({
      outcome: 'blocked',
      edits: [],
      problems: [{ severity: 'error', place: null, message: "The post 'c-post' links to the post and has no pending edit." }],
    });

  await openUnpublish();
  assert.match(unpublish().textContent, /Can't unpublish: other posts still link here\./);
  assert.match(unpublish().textContent, /The post 'c-post' links to the post and has no pending edit/);
  assert.equal(button(unpublish(), 'Unpublish').disabled, true);
  await keepUnpublish();
});

test('a failed check shows the reason and keeps Unpublish disabled', async () => {
  checkAnswer = () => answer(500, { success: false, message: 'The post store did not answer.', data: null });

  await openUnpublish();
  assert.match(unpublish().textContent, /Couldn't check for links to this post/);
  assert.match(unpublish().textContent, /The post store did not answer\./);
  assert.equal(button(unpublish(), 'Unpublish').disabled, true);
  await keepUnpublish();
});

test('a publish and remove lists the posts and sends each edit, and a refusal shows its reason and checks again', async () => {
  calls.length = 0;
  checkAnswer = () =>
    ok({
      outcome: 'publish-and-remove',
      edits: [
        { slug: 'a-post', title: 'A post', eTag: '"a1"' },
        { slug: 'c-post', title: 'C post', eTag: '"c1"' },
      ],
      problems: [],
    });
  moveAnswer = () =>
    answer(409, {
      success: false,
      message: "The admin published the pending edits of the posts 'a-post' and did not unpublish the post 'my-post'.",
      data: null,
    });

  await openUnpublish();
  assert.match(unpublish().textContent, /Their unpublished changes remove those links and will be published first, in the same rebuild\./);
  const titles = [...unpublish().querySelectorAll('.removal-edits li')].map((item) => item.textContent.trim());
  assert.deepEqual(titles, ['A post', 'C post']);

  const checksBefore = checks;
  button(unpublish(), 'Publish 2 edits and unpublish').click();
  await settle();

  assert.deepEqual(calls, [
    {
      url: 'unpublish',
      method: 'POST',
      ifMatch: '"w2"',
      body: {
        edits: [
          { slug: 'a-post', ifMatch: '"a1"' },
          { slug: 'c-post', ifMatch: '"c1"' },
        ],
      },
    },
  ]);
  assert.ok(unpublish().open, 'The dialog stays open');
  assert.match(unpublish().textContent, /did not unpublish the post 'my-post'/);
  assert.equal(checks, checksBefore + 1, 'The check runs again');
  await keepUnpublish();
});

test('Keep closes the unpublish dialog and sends nothing, and Unpublish shows a draft', async () => {
  calls.length = 0;
  checkAnswer = () => ok(plain);
  moveAnswer = () => ok({ publishDate: null, state: 'draft', eTag: '"w3"', productionRun: 7, warnings: [] });

  menuItem('Unpublish').click();
  await settle();
  assert.ok(unpublish().open, 'The dialog is open');

  button(unpublish(), 'Cancel').click();
  await settle();
  assert.ok(!unpublish().open, 'The dialog closes');
  assert.equal(calls.length, 0);

  menuItem('Unpublish').click();
  await settle();
  button(unpublish(), 'Unpublish').click();
  await settle();

  assert.deepEqual(calls, [{ url: 'unpublish', method: 'POST', ifMatch: '"w2"', body: { edits: [] } }]);
  assert.ok(!unpublish().open, 'The dialog closes');
  assert.match(status(), /^Draft/);
  assert.match(menuItem('Unpublish').textContent, /Only for upcoming posts\./);
  assert.match(menuItem('Change date').textContent, /Only for upcoming posts\./);
});
