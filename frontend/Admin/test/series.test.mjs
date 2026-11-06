import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, heading, load, press, settle } from './load.mjs';

const series = [
  {
    slug: 'fable',
    title: 'Fable',
    excerpt: 'Posts about Fable',
    image: null,
    eTag: '"fable-1"',
    posts: [
      { slug: 'a-draft', title: 'A draft', state: 'draft', publishDate: null },
      { slug: 'second', title: 'Second', state: 'upcoming', publishDate: '2026-12-01T00:00:00Z' },
      { slug: 'first', title: 'First', state: 'published', publishDate: '2026-01-02T00:00:00Z' },
    ],
  },
  { slug: 'notes', title: 'Notes', excerpt: 'Short notes', image: null, eTag: '"notes-1"', posts: [] },
];

const reads = [];
const saves = [];

const respond = (url, init = {}) => {
  const path = url.replace(/^.*\/api\//, '');

  if (init.method === 'PUT' && path.startsWith('internal/series/')) {
    const headers = new Headers(init.headers);
    saves.push({ path, ifMatch: headers.get('If-Match'), ifNoneMatch: headers.get('If-None-Match'), body: JSON.parse(init.body) });
    return answer(200, { success: true, message: null, data: { slug: 'matts-notes', eTag: '"m-1"', productionRun: 41, warnings: [] } });
  }

  if (path === 'internal/series') {
    reads.push(path);
    return answer(200, { success: true, message: null, data: series });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/series', { respond });

const rows = () => [...document.querySelectorAll('main .queue-row')].map((item) => item.id);
const sheet = () => document.getElementById('series-sheet');
const field = (id) => document.getElementById(id);
const errors = () => [...sheet().querySelectorAll('.err')].map((item) => item.textContent.trim());

const type = async (id, text) => {
  const input = field(id);
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

const editButton = (slug) => document.getElementById(`series-${slug}`).querySelector('button');

test('the page lists each series with its posts in publish date order, and the drafts after them', () => {
  assert.equal(heading(), 'Series');
  assert.deepEqual(rows(), ['series-fable', 'series-notes']);

  const posts = [...document.querySelectorAll('#series-fable .series-post a')].map((item) => item.textContent.trim());
  assert.deepEqual(posts, ['First', 'Second', 'A draft']);

  const lines = [...document.querySelectorAll('#series-fable .series-post .badge-line')].map((line) =>
    [...line.children].map((part) => part.textContent.trim()),
  );
  assert.deepEqual(lines, [['2026-01-02', 'Published'], ['2026-12-01', 'Upcoming'], ['Draft']]);
});

test('the slug of a series with a published post is locked, and the series cannot be deleted', async () => {
  editButton('fable').click();
  await settle();

  assert.ok(sheet().open, 'The form opens');
  assert.equal(field('series-slug').value, 'fable');
  assert.equal(field('series-slug').disabled, true, 'The slug is locked');

  const remove = [...sheet().querySelectorAll('button')].find((item) => item.textContent.trim() === 'Delete');
  assert.equal(remove.disabled, true, 'No delete');

  sheet().querySelector('.sheet-head button').click();
  await settle();
});

test('a new series gives the slug from the title, shows its errors, and saves', async () => {
  press('New series');
  await settle();

  await type('series-title', "Matt's Notes");
  assert.equal(field('series-slug').value, 'matts-notes', 'The slug follows the title');

  press('Save', sheet());
  await settle();
  assert.deepEqual(errors(), ['Add an excerpt.']);
  assert.equal(saves.length, 0, 'No save with an error');

  await type('series-excerpt', 'Notes of all kinds');
  const before = reads.length;
  press('Save', sheet());
  await settle();

  assert.deepEqual(saves, [
    {
      path: 'internal/series/matts-notes',
      ifMatch: null,
      ifNoneMatch: '*',
      body: { slug: 'matts-notes', title: "Matt's Notes", excerpt: 'Notes of all kinds', image: null },
    },
  ]);

  assert.equal(reads.length, before + 1, 'The list loads again');
  assert.ok(!sheet().open, 'The form closes');
  assert.match(document.querySelector('main [role="status"]').textContent, /The site is rebuilding/);
});
