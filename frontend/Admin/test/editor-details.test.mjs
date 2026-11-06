import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, press, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const post = (slug) => ({
  slug,
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
});

const terms = {
  categories: [{ slug: 'dev', title: 'Dev', value: 'Dev', posts: 4 }],
  tags: [{ slug: 'functional-programming', title: 'Functional programming', value: 'Functional programming', posts: 3 }],
};

const series = [{ slug: 'my-series', title: 'My series' }];

const saves = [];
const slugChanges = [];
const seriesSaves = [];
let saved = 0;
let answerSeries = () => {};

const ok = (data) => answer(200, { success: true, message: null, data });

const respond = (url, init = {}) => {
  const headers = new Headers(init.headers);

  if (url.endsWith('/api/internal/terms')) return ok(terms);
  if (url.endsWith('/api/internal/series')) return ok(series);

  if (url.endsWith('/api/internal/series/f-for-fun')) {
    seriesSaves.push({ method: init.method, ifNoneMatch: headers.get('If-None-Match'), body: JSON.parse(init.body) });
    return new Promise((resolve) => {
      answerSeries = () => resolve(ok({ slug: 'f-for-fun', eTag: '"f-1"', warnings: [] }));
    });
  }

  if (url.endsWith('/api/internal/posts/my-post/slug')) {
    slugChanges.push({ method: init.method, ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
    return ok({ slug: 'new-slug', eTag: '"v9"' });
  }

  if (url.endsWith('/api/internal/posts/new-slug')) return ok(post('new-slug'));

  if (url.endsWith('/api/internal/posts/my-post')) {
    if (init.method === 'PUT') {
      saves.push({ ifMatch: headers.get('If-Match'), body: JSON.parse(init.body) });
      saved += 1;
      return ok({ eTag: `"v${saved + 1}"`, check: { kind: 'found', problems: [] } });
    }

    return ok(post('my-post'));
  }

  return apiAnswer(url);
};

const { calls } = await load('http://localhost:4322/posts/my-post', { respond });

const sheet = () => document.querySelector('#details-sheet');
const field = (id) => document.querySelector(`#${id}`);
const chips = () => [...sheet().querySelectorAll('.chip')].map((item) => item.textContent.trim());

const type = async (element, text) => {
  element.value = text;
  element.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

const enter = async (element) => {
  element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  await settle();
};

const saveNow = async () => {
  window.dispatchEvent(new KeyboardEvent('keydown', { key: 's', metaKey: true, cancelable: true, bubbles: true }));
  await settle();
  return saves.at(-1);
};

test('the Details button opens the sheet and loads the suggestions and the series', async () => {
  assert.ok(!sheet().open, 'The sheet starts closed');
  press('Details', document.querySelector('.editor-bar'));
  await settle();
  assert.ok(sheet().open, 'The sheet is open');
  assert.ok(calls.some((call) => call.url.endsWith('/api/internal/terms')));
  assert.ok(calls.some((call) => call.url.endsWith('/api/internal/series')));
  assert.deepEqual(
    [...sheet().querySelectorAll('.field > label')].map((item) => item.textContent.trim()),
    ['Slug', 'Category', 'Tags', 'Series', 'Card image', 'Author'],
  );
  assert.equal(field('details-category').value, '', 'A new draft has no category');
});

test('the tag picker suggests each tag with its number of posts, and a typed tag matches by slug', async () => {
  assert.match(field('details-tags').closest('.field').querySelector('.suggestions').textContent, /Functional programming\s*3 posts/);

  const input = field('details-tags');
  await type(input, 'functional Programming');
  assert.ok(field('details-tags') === input, 'The input stays, so it keeps its focus');
  await enter(input);
  assert.equal(input.value, '', 'The input clears');
  assert.deepEqual(chips(), ['Functional programming']);

  const save = await saveNow();
  assert.deepEqual(save.body.details.tags, ['Functional programming']);
  assert.equal(save.body.details.category, null);
});

test('the category picker suggests the categories, and a typed category matches by slug', async () => {
  const input = field('details-category');
  await type(input, 'dEV');
  assert.ok(field('details-category') === input, 'The input stays, so it keeps its focus');
  assert.match(input.closest('.field').querySelector('.suggestions').textContent, /Dev\s*4 posts/);
  await enter(input);
  assert.deepEqual(chips().filter((item) => item === 'Dev'), ['Dev']);

  const save = await saveNow();
  assert.equal(save.body.details.category, 'Dev');
  input.closest('.field').querySelector('.chip button').click();
  await settle();
  assert.ok(!chips().includes('Dev'), 'The chip goes');
});

test('a series sets the category to Series and locks the category picker', async () => {
  const select = field('details-series');
  select.value = 'my-series';
  select.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  assert.ok(chips().includes('Series'), 'The category shows');
  assert.equal(field('details-category').disabled, true);

  const save = await saveNow();
  assert.equal(save.body.details.series, 'my-series');
  assert.equal(save.body.details.category, 'Series');
});

test('the removal of the series leaves the post with no category and unlocks the picker', async () => {
  const select = field('details-series');
  select.value = '';
  select.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  assert.ok(!chips().includes('Series'));
  assert.equal(field('details-category').disabled, false);

  const save = await saveNow();
  assert.equal(save.body.details.series, null);
  assert.equal(save.body.details.category, null);
});

test('New series in the picker saves a series and selects it, and the editor stays', async () => {
  const select = field('details-series');
  select.value = '+new';
  select.dispatchEvent(new Event('change', { bubbles: true }));
  await settle();

  const saveSeries = () => [...sheet().querySelectorAll('button')].find((item) => item.textContent.trim() === 'Save the series');
  saveSeries().click();
  await settle();
  assert.match(sheet().textContent, /Give the series a title\./, 'The errors of the Series page');
  assert.equal(seriesSaves.length, 0, 'No save with problems');

  const title = field('new-series-title');
  await type(title, 'F# for fun');
  assert.ok(field('new-series-title') === title, 'The input stays, so it keeps its focus');
  assert.equal(field('new-series-slug').value, 'f-for-fun', 'The slug from the title');
  field('new-series-excerpt').value = 'Small programs in F#.';
  field('new-series-excerpt').dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
  saveSeries().click();
  await settle();
  assert.equal(field('details-series').disabled, true, 'No other choice in the picker during the save');
  answerSeries();
  await settle();
  assert.equal(field('details-series').disabled, false, 'The picker works after the save');

  assert.deepEqual(seriesSaves, [
    {
      method: 'PUT',
      ifNoneMatch: '*',
      body: { slug: 'f-for-fun', title: 'F# for fun', excerpt: 'Small programs in F#.', image: null },
    },
  ]);
  assert.equal(field('new-series-title'), null, 'The form closes');
  assert.equal(field('details-series').value, 'f-for-fun', 'The picker selects the new series');
  assert.ok(chips().includes('Series'), 'The category is Series');
  assert.equal(location.pathname, '/posts/my-post', 'The author stays in the editor');

  const save = await saveNow();
  assert.equal(save.body.details.series, 'f-for-fun');
  assert.equal(save.body.details.category, 'Series');
});

test('the slug of a draft changes with the ETag, and the editor goes to the new address', async () => {
  const change = [...sheet().querySelectorAll('button')].find((item) => item.textContent.trim() === 'Change');
  assert.equal(change.disabled, true, 'The slug has not changed');

  const input = field('details-slug');
  await type(input, 'new-slug');
  assert.ok(field('details-slug') === input, 'The input stays, so it keeps its focus');
  assert.equal(change.disabled, false);
  change.click();
  await settle();

  assert.equal(slugChanges.length, 1);
  assert.equal(slugChanges[0].method, 'PUT');
  assert.equal(slugChanges[0].ifMatch, `"v${saved + 1}"`, 'The ETag of the last save');
  assert.deepEqual(slugChanges[0].body, { slug: 'new-slug' });
  assert.equal(location.pathname, '/posts/new-slug');
});
