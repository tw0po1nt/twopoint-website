import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const post = {
  slug: 'my-post',
  state: 'draft',
  pendingEdit: false,
  publishDate: null,
  eTag: '"v1"',
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: noDetails,
  text: '',
  published: null,
};

const ok = (data) => answer(200, { success: true, message: null, data });

const checks = [];
const publishes = [];
let checkAnswer = () => ok({ problems: [] });
let publishAnswer = () => ok({ publishDate: '2026-03-01T09:30:45Z', state: 'published', warnings: [] });

const respond = (url, init = {}) => {
  const headers = new Headers(init.headers);

  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post/publish/check')) {
    checks.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
    return checkAnswer();
  }

  if (url.endsWith('/api/internal/posts/my-post/publish')) {
    publishes.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
    return publishAnswer();
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(post);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const dialog = () => document.querySelector('#publish-dialog');
const status = () => document.querySelector('.editor-status').textContent.trim();
const bar = () => document.querySelector('.editor-bar');
const confirm = () => [...dialog().querySelectorAll('.actions button')].find((item) => item.textContent.trim() === 'Publish');
const problems = () => [...dialog().querySelectorAll('.preview-problem')].map((item) => item.textContent.trim());

const type = async (element, text) => {
  element.value = text;
  element.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('the menu shows each action of a draft, and an action that is not available shows its reason', () => {
  const items = [...document.querySelectorAll('#editor-menu [role="menuitem"]')].map((item) => ({
    text: item.textContent.trim(),
    disabled: item.getAttribute('aria-disabled'),
  }));

  assert.deepEqual(items, [
    { text: 'SaveNo unsaved changes.', disabled: 'true' },
    { text: 'Compare with liveOnly for published posts.', disabled: 'true' },
    { text: 'Discard changesOnly for published posts.', disabled: 'true' },
    { text: 'Change dateOnly for upcoming posts.', disabled: 'true' },
    { text: 'UnpublishOnly for upcoming posts.', disabled: 'true' },
    { text: 'RevisionsOnly for published posts.', disabled: 'true' },
    { text: 'Send to subscribersOnly for published posts.', disabled: 'true' },
    { text: 'Delete', disabled: null },
    { text: 'Add image', disabled: null },
  ]);
});

test('Publish opens the dialog, asks the API for a check of now, and enables Publish after a check with no error', async () => {
  assert.ok(!dialog().open, 'The dialog starts closed');
  assert.equal(bar().querySelector('.editor-primary').disabled, false);

  press('Publish', bar());
  await settle();

  assert.ok(dialog().open, 'The dialog is open');
  assert.equal(checks.length, 1, 'A check, though the load had no problems');
  assert.deepEqual(checks[0], { ifMatch: '"v1"', body: { publishDate: null } });
  assert.equal(confirm().disabled, false);
});

test('a check with an error lists the problems and disables Publish, and a warning does not', async () => {
  checkAnswer = () =>
    ok({
      problems: [
        { severity: 'warning', place: null, message: 'Add the tag 2026.' },
        { severity: 'error', place: null, message: 'The output has no page at /posts/nowhere/.' },
      ],
    });

  dialog().querySelector('input[type="radio"]').dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  assert.deepEqual(problems(), ['Warning Add the tag 2026.', 'Error The output has no page at /posts/nowhere/.']);
  assert.equal(confirm().disabled, true);

  checkAnswer = () => ok({ problems: [{ severity: 'warning', place: null, message: 'Add the tag 2026.' }] });
  dialog().querySelector('input[type="radio"]').dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  assert.deepEqual(problems(), ['Warning Add the tag 2026.']);
  assert.equal(confirm().disabled, false);
});

test('a date asks for a check of that UTC time, and Publish waits for a complete date', async () => {
  const sent = checks.length;
  const radios = dialog().querySelectorAll('input[type="radio"]');
  radios[1].dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  assert.equal(checks.length, sent, 'No check for an empty date');
  assert.equal(confirm().disabled, true);

  await type(dialog().querySelector('#publish-date'), '2099-12-31T13:00');
  assert.equal(checks.length, sent + 1);
  assert.deepEqual(checks.at(-1).body, { publishDate: '2099-12-31T13:00:00Z' });
  assert.equal(confirm().disabled, false);
});

test('Publish sends the ETag and the date, closes the dialog, and shows the new state', async () => {
  publishAnswer = () => ok({ publishDate: '2099-12-31T13:00:00Z', state: 'upcoming', warnings: ['site-rebuild'] });

  confirm().click();
  await settle();

  assert.deepEqual(publishes, [{ ifMatch: '"v1"', body: { publishDate: '2099-12-31T13:00:00Z' } }]);
  assert.ok(!dialog().open, 'The dialog closes');
  assert.equal(status(), 'Upcoming Dec 31, 2099 · deployed');
  assert.match(document.querySelector('main .alert').textContent, /Couldn't start the rebuild/);
  assert.equal(bar().querySelector('.editor-primary').disabled, true, 'The published post has no pending edit');
  assert.equal(bar().querySelector('.editor-primary').textContent.trim(), 'Publish changes');

  press('Dismiss', document.querySelector('main'));
  await settle();
  assert.equal(document.querySelector('main .alert'), null);
});
